using System.Collections.Concurrent;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using System.Reflection;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Basic.DapTests;

/// <summary>
/// Runs X16D and talks DAP to it, the way VS Code (or X16M) does.
/// </summary>
internal sealed class DapClient : IDisposable
{
    private readonly Process _process;
    private readonly DebugProtocolHost _host;
    private readonly BlockingCollection<StoppedEvent> _stops = new();
    private readonly TaskCompletionSource _terminated = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<string> _output = new();

    public static string Setting(string key) =>
        Environment.GetEnvironmentVariable(key.ToUpperInvariant() + "_PATH") ??
        typeof(DapClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";

    public static string X16D => Setting("X16D");
    public static string DebugMode => Setting("DebugMode");
    public static string RomDirectory => Environment.GetEnvironmentVariable("X16_ROM_DIR") ?? Setting("X16RomDirectory");

    public IEnumerable<string> Output => _output;
    public bool Terminated => _terminated.Task.IsCompleted;

    // true: X16Ds own rom.bin, with no symbols, as BitMagic uses out of the box
    private readonly bool _x16dRom;

    private DapClient(Process process, bool x16dRom)
    {
        _process = process;
        _x16dRom = x16dRom;
        _host = new DebugProtocolHost(process.StandardInput.BaseStream, process.StandardOutput.BaseStream, registerStandardHandlers: false);
        _host.RegisterEventType<StoppedEvent>(e => _stops.Add(e));
        _host.RegisterEventType<TerminatedEvent>(_ => _terminated.TrySetResult());
        _host.RegisterEventType<ExitedEvent>(_ => { });
        _host.RegisterEventType<OutputEvent>(e => _output.Enqueue(e.Output ?? ""));
        _host.RegisterEventType<BreakpointEvent>(_ => { });
        _host.RegisterEventType<InitializedEvent>(_ => { });
        _host.RegisterEventType<MemoryEvent>(_ => { });
        _host.RegisterEventType<LoadedSourceEvent>(_ => { });
        _host.RegisterEventType<InvalidatedEvent>(_ => { });
        _host.Run();
    }

    public static DapClient Start(bool x16dRom = false)
    {
        foreach (var (name, path) in new[] { ("X16D", X16D), ("the debug mode", DebugMode), ("rom.bin", Path.Combine(RomDirectory, "rom.bin")) })
        {
            if (!File.Exists(path))
                Assert.Inconclusive($"No {name} at '{path}'. Build it first, or set the path.");
        }

        var psi = new ProcessStartInfo(X16D)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(X16D)!,
        };
        if (!x16dRom)
            psi.Environment["BITMAGIC_ROM"] = Path.Combine(RomDirectory, "rom.bin");

        // a cache of its own, so packages from an earlier run aren't used
        PackageCache = Path.Combine(Path.GetTempPath(), "BitMagic.X16Basic.DapTests", "cache-" + Guid.NewGuid().ToString("N"));
        psi.Environment["BITMAGIC_DEBUGMODE_CACHE"] = PackageCache;

        var process = Process.Start(psi) ?? throw new Exception("X16D didn't start.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return new DapClient(process, x16dRom);
    }

    /// <summary>
    /// Writes a project for <paramref name="basic"/> in a new folder and launches it.
    /// </summary>
    /// <summary>Where this X16D keeps debug mode packages.</summary>
    public static string PackageCache { get; private set; } = "";

    /// <summary>Where the build puts this repo's NuGet packages.</summary>
    public static string PackageFolder => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(DebugMode)!, "..", "..", "..", "..", "..", "..", "artifacts", "packages"));

    /// <param name="debugMode">The project's debugMode, without its settings; the mode's assembly if not given.</param>
    public string Launch(string basic, JObject? settings = null, IEnumerable<int>? breakpointLines = null, JObject? debugMode = null)
    {
        var folder = Path.Combine(Path.GetTempPath(), "BitMagic.X16Basic.DapTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "program.bas");
        File.WriteAllText(source, basic);

        settings ??= new JObject();
        settings["source"] = "program.bas";

        var project = new JObject
        {
            ["startStepping"] = false,
            ["autobootRun"] = false,
            ["muteAudio"] = true,
            ["romFile"] = Path.Combine(RomDirectory, "rom.bin"),
            ["symbols"] = new JArray(
                new JObject { ["symbols"] = Path.Combine(RomDirectory, "kernal.sym"), ["romBank"] = 0 },
                new JObject { ["symbols"] = Path.Combine(RomDirectory, "basic.sym"), ["romBank"] = 4 }),
        };
        debugMode ??= new JObject { ["assembly"] = DebugMode };
        debugMode["settings"] = settings;
        project["debugMode"] = debugMode;
        if (_x16dRom)
        {
            project.Remove("romFile");
            project.Remove("symbols");
        }

        var projectFile = Path.Combine(folder, "project.json");
        File.WriteAllText(projectFile, project.ToString());

        _host.SendRequestSync(new InitializeRequest("dap-tests"));

        var launched = new TaskCompletionSource();
        _host.SendRequest<LaunchArguments>(new LaunchRequest
        {
            ConfigurationProperties = new Dictionary<string, JToken?>
            {
                ["program"] = projectFile,
                ["cwd"] = folder,
                ["stopOnEntryOverride"] = false,
                ["muteAudioOverride"] = true,
            }
        }, _ => launched.TrySetResult(), (_, e) => launched.TrySetException(e));

        if (breakpointLines != null)
            SetBreakpoints(source, breakpointLines.ToArray());

        if (!launched.Task.Wait(TimeSpan.FromSeconds(60)))
            Assert.Fail("Launch didn't finish.");
        if (launched.Task.IsFaulted)
            Assert.Fail($"Launch failed: {launched.Task.Exception?.InnerException?.Message}{Environment.NewLine}{string.Join("", _output)}");

        _host.SendRequestSync(new ConfigurationDoneRequest());
        return source;
    }

    public SetBreakpointsResponse SetBreakpoints(string source, params int[] lines) =>
        SetBreakpoints(source, lines.Select(l => new SourceBreakpoint(l)).ToArray());

    public SetBreakpointsResponse SetBreakpoints(string source, params SourceBreakpoint[] breakpoints) =>
        _host.SendRequestSync(new SetBreakpointsRequest(new Source { Path = source, Name = Path.GetFileName(source) }, breakpoints.ToList()));

    public StoppedEvent WaitForStop(int seconds = 30)
    {
        if (_stops.TryTake(out var stop, TimeSpan.FromSeconds(seconds)))
            return stop;
        Assert.Fail($"Didn't stop within {seconds}s.{Environment.NewLine}{string.Join("", _output)}");
        return null!;
    }

    public bool WaitForTerminate(int seconds = 30) => _terminated.Task.Wait(TimeSpan.FromSeconds(seconds));

    public void Continue() => _host.SendRequestSync(new ContinueRequest(0));
    public void StepIn() => _host.SendRequestSync(new StepInRequest(0));
    public void StepOver() => _host.SendRequestSync(new NextRequest(0));
    public void StepOut() => _host.SendRequestSync(new StepOutRequest(0));
    public void Pause() => _host.SendRequestSync(new PauseRequest(0));

    public List<StackFrame> StackTrace() => _host.SendRequestSync(new StackTraceRequest(0)).StackFrames;

    public List<Scope> Scopes(int frameId) => _host.SendRequestSync(new ScopesRequest(frameId)).Scopes;

    public List<Variable> Variables(int reference, int? start = null, int? count = null) =>
        _host.SendRequestSync(new VariablesRequest(reference) { Start = start, Count = count }).Variables;

    /// <summary>The variables in the named scope of the top frame, by name.</summary>
    public Dictionary<string, Variable> ScopeVariables(string scope)
    {
        var frame = StackTrace()[0];
        var reference = Scopes(frame.Id).Single(s => s.Name == scope).VariablesReference;
        return Variables(reference).ToDictionary(v => v.Name);
    }

    public string Evaluate(string expression, EvaluateArguments.ContextValue context = EvaluateArguments.ContextValue.Watch) =>
        _host.SendRequestSync(new EvaluateRequest(expression) { Context = context }).Result;

    public SetVariableResponse SetVariable(int reference, string name, string value) =>
        _host.SendRequestSync(new SetVariableRequest(reference, name, value));

    public void WriteMemory(int address, params byte[] data) =>
        _host.SendRequestSync(new WriteMemoryRequest("main", Convert.ToBase64String(data)) { Offset = address });

    public string SourceText(int sourceReference) =>
        _host.SendRequestSync(new SourceRequest(sourceReference) { Source = new Source { SourceReference = sourceReference } }).Content;

    public JToken? ModeRequest(string command) => _host.SendRequestSync(new ModeRequest(command)).Result;

    public ExceptionInfoResponse ExceptionInfo() => _host.SendRequestSync(new ExceptionInfoRequest(0));

    public void Dispose()
    {
        try
        {
            _host.SendRequestSync(new DisconnectRequest { TerminateDebuggee = true });
        }
        catch
        {
            // already gone
        }

        try
        {
            if (!_process.WaitForExit(10000))
                _process.Kill(true);
        }
        catch
        {
            // ignore
        }

        _host.Stop();
        _process.Dispose();
    }
}
