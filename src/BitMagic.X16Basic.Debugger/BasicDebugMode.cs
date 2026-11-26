using System.Globalization;
using System.Text.RegularExpressions;
using BitMagic.X16Debugger.Modes;
using BitMagic.X16Emulator;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol;
using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Basic.Debugger;

/// <summary>
/// Debugs BASIC. It follows the interpreter through system breakpoints in the BASIC ROM:
/// <list type="bullet">
/// <item><c>nmain</c>: BASIC is waiting at READY after booting, so load the program and RUN it;</item>
/// <item><c>gone</c>: a statement is about to run; breakpoints and stepping are decided here;</item>
/// <item><c>error</c>: stop on a BASIC error;</item>
/// <item><c>ready</c>: the program has ended.</item>
/// </list>
/// </summary>
public sealed partial class BasicDebugMode : IX16DebugMode
{
    public string Name => "BASIC";
    public int ApiVersion => DebugModeApi.Version;

    private IX16DebugHost _host = null!;
    private Emulator _emulator = null!;
    private EmulatorMemory _memory = null!;
    private BasicSettings _settings = new();
    private BasicRomSymbols _symbols = BasicRomSymbols.Default;
    private KeywordTable _keywords = KeywordTable.Default;

    private BasicProgram? _program;
    private SourceMap? _map;          // where the program is in memory, once it's loaded
    private string? _sourcePath;
    private string[] _sourceLines = [];
    private Source? _source;

    private bool _loaded;
    private bool _programRunning;
    private bool _stopOnEntry;

    // set on the protocol thread, read on the emulator thread
    private volatile IReadOnlyDictionary<int, BasicBreakpoint> _breakpoints = new Dictionary<int, BasicBreakpoint>();
    private volatile StepState? _step;

    // the stack pointer at the last statement, where BASIC's frames start
    private int _statementStackPointer = 0xff;

    private readonly Dictionary<int, Func<VariablesArguments, List<Variable>>> _references = new();
    private readonly Dictionary<int, string> _arrayReferences = new(); // element list reference -> array name
    private readonly HashSet<int> _frames = new();

    private sealed record StepState(DebugModeStep Kind, int Depth);
    private sealed record BasicBreakpoint(Breakpoint Breakpoint, SourceBreakpoint Requested, int LineNumber)
    {
        public int Hits { get; set; }
    }

    private int Rom(int address) => (_symbols.BasicBank << 16) | address;

    public void Launch(IX16DebugHost host, JObject? settings)
    {
        _host = host;
        _emulator = host.Emulator;
        _memory = new EmulatorMemory(_emulator);
        _settings = settings?.ToObject<BasicSettings>() ?? new BasicSettings();
        _stopOnEntry = _settings.StopOnEntry;

        LoadSymbols();

        if (!string.IsNullOrWhiteSpace(_settings.Source))
            LoadSource(_settings.Source);
        else
            host.Logger.LogLine("No BASIC source given, so debugging whatever program BASIC has.");

        host.SetSystemBreakpoint(Rom(_symbols.Nmain));
        host.SetSystemBreakpoint(Rom(_symbols.Gone));
        host.SetSystemBreakpoint(Rom(_symbols.Error));
        host.SetSystemBreakpoint(Rom(_symbols.Ready));
    }

    private void LoadSymbols()
    {
        var file = _settings.Symbols;
        if (!string.IsNullOrWhiteSpace(file) && !Path.IsPathRooted(file))
            file = Path.GetFullPath(file, _host.BasePath);
        file ??= _host.RomSymbolsFile(BasicRomSymbols.Default.BasicBank);

        var bank = _emulator.RomBank.Slice(BasicRomSymbols.Default.BasicBank * 0x4000, 0x4000);

        if (file != null && File.Exists(file))
        {
            var missing = new List<string>();
            var loaded = BasicRomSymbols.Load(file, missing);
            if (missing.Count > 0)
                _host.Logger.LogLine($"BASIC symbols '{file}' are missing {string.Join(", ", missing)}; using the defaults for those.");

            if (Matches(loaded))
            {
                _symbols = loaded;
                return;
            }

            _host.Logger.LogError($"'{file}' doesn't match the ROM being used, so finding the addresses in the ROM instead.");
        }

        // no symbols (BitMagic's own rom.bin doesn't come with them), or the wrong ones
        var detected = BasicRomSymbols.Detect(bank, out var problem);
        if (detected != null && Matches(detected))
        {
            _symbols = detected;
            _host.Logger.LogLine($"Found BASIC in the ROM: statements at ${detected.Gone:X4}, variables from ${detected.Vartab:X4}.");
            return;
        }

        if (Matches(BasicRomSymbols.Default))
        {
            _symbols = BasicRomSymbols.Default;
            return;
        }

        throw new Exception($"Couldn't find X16 BASIC in ROM bank {BasicRomSymbols.Default.BasicBank} ({problem}). Add the ROM's basic.sym to the project's symbols for that bank.");
    }

    /// <summary>
    /// Every hook depends on the addresses matching the ROM, so check they do: the keyword list
    /// should be where they say. Sets <see cref="_keywords"/> if so.
    /// </summary>
    private bool Matches(BasicRomSymbols symbols)
    {
        try
        {
            var keywords = KeywordTable.FromRom(_emulator.RomBank.Slice(symbols.BasicBank * 0x4000, 0x4000), symbols);
            if (keywords.GetKeyword(KeywordTable.EndToken) != "END" || keywords.GetKeyword(KeywordTable.PrintToken) != "PRINT" ||
                keywords.GetExtendedKeyword(KeywordTable.ExtendedFunctionBase) != "VPEEK")
                return false;

            _keywords = keywords;
            return true;
        }
        catch
        {
            return false; // not a keyword list at all
        }
    }

    private void LoadSource(string source)
    {
        _sourcePath = Path.GetFullPath(source, _host.BasePath);
        if (!File.Exists(_sourcePath))
            throw new Exception($"BASIC source '{_sourcePath}' not found.");

        var text = File.ReadAllText(_sourcePath);
        _sourceLines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        _program = BasicTokeniser.Tokenise(text, new BasicTokeniserOptions { Keywords = _keywords, Charset = _settings.PetsciiCase });
        _source = new Source { Name = Path.GetFileName(_sourcePath), Path = _sourcePath };

        foreach (var d in _program.Diagnostics)
        {
            var message = $"{_sourcePath}({d.SourceLine},{d.Column + 1}): {(d.IsError ? "error" : "warning")}: {d.Message}";
            if (d.IsError)
                _host.Logger.LogError(message);
            else
                _host.Logger.LogLine(message);
        }

        if (_program.HasErrors)
            throw new Exception($"'{Path.GetFileName(_sourcePath)}' has errors: {string.Join(" ", _program.Diagnostics.Where(d => d.IsError).Take(3))}");

        _host.Logger.LogLine($"Tokenised '{Path.GetFileName(_sourcePath)}': {_program.Lines.Count} lines, {_program.ToMemoryImage().Length} bytes.");
    }

    #region System breakpoints

    public DebugModeStop OnSystemBreakpoint(int debuggerAddress)
    {
        var address = debuggerAddress & 0xffff;

        if (_sysTarget == debuggerAddress)
            return OnSysTarget();
        if (address == _symbols.Gone)
            return OnStatement();
        if (address == _symbols.Error)
            return OnError();
        if (address == _symbols.Ready)
            return OnReady();
        if (address == _symbols.Nmain)
            return OnWaitingForInput();

        return DebugModeStop.Continue;
    }

    private int Curlin => _memory.ReadWord(_symbols.Curlin);
    private int Txtptr => _memory.ReadWord(_symbols.Txtptr);
    private static bool IsDirect(int curlin) => (curlin & 0xff00) == 0xff00;

    /// <summary>BASIC is at READY, waiting for a line. The first time, load the program and RUN it.</summary>
    private DebugModeStop OnWaitingForInput()
    {
        if (_loaded)
            return DebugModeStop.Continue;

        _loaded = true;
        _host.SetSystemBreakpoint(Rom(_symbols.Nmain), false); // only needed the once

        if (_program == null)
            return DebugModeStop.Continue;

        var start = _memory.ReadWord(_symbols.Txttab);
        var image = _program.ToMemoryImage(start);
        _memory.WriteBytes(start, image);
        var end = start + image.Length;
        _memory.WriteWord(_symbols.Vartab, end);
        _memory.WriteWord(_symbols.Arytab, end);
        _memory.WriteWord(_symbols.Strend, end);
        _map = _program.CreateSourceMap(start);
        _image = image;

        _host.Logger.LogLine($"Loaded the program at ${start:X4} to ${end - 1:X4}.");

        if (_settings.Run)
            Exec("RUN");

        return DebugModeStop.Continue;
    }

    /// <summary>
    /// Types a line in through BASIC's EXEC, as if it was typed at READY. The text goes at the top
    /// of RAM bank 1, it's read before anything gets to run.
    /// </summary>
    private void Exec(string line)
    {
        var (bytes, _) = Petscii.EncodeLine(line, PetsciiCase.Upper, 0, null);
        var text = bytes.Concat(new byte[] { 13, 0 }).ToArray();
        var address = 0xc000 - text.Length;

        _memory.WriteBank(1, address, text);
        _memory.WriteByte(_symbols.ExecFlag, 0x80);
        _memory.WriteWord(_symbols.ExecAddr, address);
        _memory.WriteByte(_symbols.ExecBank, 1);
    }

    private DebugModeStop OnStatement()
    {
        var curlin = Curlin;
        if (IsDirect(curlin))
            return DebugModeStop.Continue;

        _programRunning = true;
        _statementStackPointer = _emulator.StackPointer & 0xff;

        if (_stopOnEntry)
        {
            _stopOnEntry = false;
            _step = null;
            return new DebugModeStop(true, StoppedEvent.ReasonValue.Entry, "Entry");
        }

        var step = _step;
        if (step != null)
        {
            var stop = step.Kind switch
            {
                DebugModeStep.Into => true,
                DebugModeStep.Over => GosubDepth() <= step.Depth,
                DebugModeStep.Out => GosubDepth() < step.Depth,
                _ => true
            };

            if (stop)
            {
                _step = null;
                return DebugModeStop.Step();
            }
        }

        // breakpoints are by line number, so they work whether or not the program matches the source
        if (_breakpoints.Count == 0 || !_breakpoints.TryGetValue(curlin, out var breakpoint) || !IsFirstStatement(curlin, Txtptr))
            return DebugModeStop.Continue;

        return BreakpointHit(breakpoint);
    }

    /// <summary>
    /// At gone, <c>txtptr</c> is on the last byte of the line's header for its first statement (so
    /// the line number is just before it), or on the <c>:</c> before any other.
    /// </summary>
    private bool IsFirstStatement(int curlin, int txtptr) => _memory.ReadWord(txtptr - 1) == curlin;

    private DebugModeStop BreakpointHit(BasicBreakpoint breakpoint)
    {
        var requested = breakpoint.Requested;

        if (!string.IsNullOrWhiteSpace(requested.Condition))
        {
            try
            {
                if (!new BasicExpression(_memory, _symbols).IsTrue(requested.Condition))
                    return DebugModeStop.Continue;
            }
            catch (Exception e)
            {
                return DebugModeStop.Exception($"Breakpoint condition '{requested.Condition}' on line {breakpoint.LineNumber} failed: {e.Message}");
            }
        }

        breakpoint.Hits++;
        if (!string.IsNullOrWhiteSpace(requested.HitCondition) && !HitConditionMet(requested.HitCondition, breakpoint.Hits))
            return DebugModeStop.Continue;

        if (!string.IsNullOrWhiteSpace(requested.LogMessage))
        {
            _host.SendEvent(new OutputEvent(Interpolate(requested.LogMessage) + Environment.NewLine) { Category = OutputEvent.CategoryValue.Console });
            return DebugModeStop.Continue;
        }

        _step = null;
        return DebugModeStop.Breakpoint($"Breakpoint on line {breakpoint.LineNumber}", breakpoint.Breakpoint.Id is int id ? [id] : null);
    }

    private static bool HitConditionMet(string condition, int hits)
    {
        condition = condition.Trim();
        var op = condition.StartsWith(">=") ? ">=" : condition.StartsWith('>') ? ">" : condition.StartsWith('%') ? "%" : condition.StartsWith("==") ? "==" : condition.StartsWith('=') ? "=" : "";
        if (!int.TryParse(condition[op.Length..].Trim(), out var n))
            return true;

        return op switch
        {
            ">=" => hits >= n,
            ">" => hits > n,
            "%" => n > 0 && hits % n == 0,
            _ => hits == n
        };
    }

    /// <summary>A log point's message, with <c>{expression}</c> replaced by its value.</summary>
    private string Interpolate(string message) =>
        LogExpression().Replace(message, m =>
        {
            try
            {
                return new BasicExpression(_memory, _symbols).Evaluate(m.Groups[1].Value, false).Display(_settings.PetsciiCase);
            }
            catch (Exception e)
            {
                return $"<{e.Message}>";
            }
        });

    [GeneratedRegex(@"\{([^}]+)\}")]
    private static partial Regex LogExpression();

    private DebugModeStop OnError()
    {
        var curlin = Curlin;
        if (IsDirect(curlin))
            return DebugModeStop.Continue;

        _step = null;
        var error = _emulator.X;
        return DebugModeStop.Exception($"?{BasicErrors.GetMessage(error)} ERROR IN {curlin}");
    }

    private DebugModeStop OnReady()
    {
        if (!_programRunning)
            return DebugModeStop.Continue;

        _programRunning = false;
        _step = null;

        if (_settings.TerminateAtEnd)
            return DebugModeStop.End("The BASIC program has finished.");

        return DebugModeStop.Continue;
    }

    /// <summary>How many GOSUBs deep BASIC is, from the frames on the stack at the current statement.</summary>
    private int GosubDepth() => BasicStack.Read(_memory, _emulator.StackPointer & 0xff, _symbols).Count(f => f is GosubFrame);

    #endregion

    #region Execution control

    public bool Step(DebugModeStep step)
    {
        ClearReferences();

        if (step == DebugModeStep.Pause)
            return false; // pause the CPU straight away: a program waiting for input may never reach another statement

        // in machine code (eg after stepping into a SYS), X16D steps the CPU
        if (InMachineCode)
            return false;

        if (step == DebugModeStep.Into)
            ArmSysStepIn();

        _step = new StepState(step, ReadFrames().Count(f => f is GosubFrame));
        return true;
    }

    /// <summary>Stopped in code in RAM rather than in the ROM, so it's the program's machine code.</summary>
    private bool InMachineCode => _emulator.Pc < 0xc000;

    private int? _sysTarget;

    /// <summary>
    /// If the statement about to run is a SYS, stop on its first instruction, so stepping into it goes
    /// into the machine code.
    /// </summary>
    private void ArmSysStepIn()
    {
        if (_emulator.Pc != _symbols.Gone || _emulator.RomBankAct != _symbols.BasicBank)
            return;

        var p = Txtptr + 1;
        while (_memory.ReadByte(p) == ' ')
            p++;
        if (_memory.ReadByte(p) != SysToken)
            return;

        // the rest of the statement is the address
        var body = new List<byte>();
        for (p++; body.Count < 250; p++)
        {
            var b = _memory.ReadByte(p);
            if (b == 0 || b == ':')
                break;
            body.Add(b);
        }

        try
        {
            var text = Detokeniser.DetokeniseLine(body.ToArray(), _keywords);
            var address = (int)BasicExpression.ToNumber(new BasicExpression(_memory, _symbols).Evaluate(text.Split(',')[0], false));
            if (address < 0 || address >= 0xc000)
                return; // into the ROM: nothing of the program's to stop on

            var bank = address >= 0xa000 ? _emulator.Memory[0] : 0;
            _sysTarget = (bank << 16) | address;
            _host.SetSystemBreakpoint(_sysTarget.Value);
        }
        catch (BasicExpressionException)
        {
            // can't tell where it goes, so just step to the next statement
        }
    }

    private const byte SysToken = 0x9e;

    private DebugModeStop OnSysTarget()
    {
        _host.SetSystemBreakpoint(_sysTarget!.Value, false);
        _sysTarget = null;
        _step = null;
        return DebugModeStop.Step("SYS");
    }

    public void Continue()
    {
        _step = null;
        if (_sysTarget != null)
        {
            _host.SetSystemBreakpoint(_sysTarget.Value, false);
            _sysTarget = null;
        }
        ClearReferences();
    }

    #endregion

    #region Breakpoints

    private bool IsOurSource(string? path)
    {
        if (path == null || _sourcePath == null)
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(path), _sourcePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // the breakpoints set in the source file and in the listing, merged into _breakpoints
    private IReadOnlyDictionary<int, BasicBreakpoint> _fileBreakpoints = new Dictionary<int, BasicBreakpoint>();
    private IReadOnlyDictionary<int, BasicBreakpoint> _listingBreakpoints = new Dictionary<int, BasicBreakpoint>();

    public SetBreakpointsResponse? SetBreakpoints(SetBreakpointsArguments arguments)
    {
        var path = arguments.Source?.Path;
        var isListing = _listingReference != 0 && arguments.Source?.SourceReference == _listingReference;
        var isBasic = isListing || (path != null && (path.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) || IsOurSource(path)));
        if (!isBasic)
            return null;

        var response = new SetBreakpointsResponse();
        var requested = arguments.Breakpoints ?? [];

        SourceMap? map;
        Source? source;
        if (isListing)
        {
            map = _listing?.Map;
            source = _listing?.Source;
        }
        else
        {
            map = IsOurSource(path) ? _map ?? _program?.CreateSourceMap() : null;
            source = _source;
        }

        if (map == null)
        {
            foreach (var b in requested)
                response.Breakpoints.Add(new Breakpoint { Verified = false, Line = b.Line, Message = "This isn't the BASIC program being debugged." });
            return response;
        }

        var breakpoints = new Dictionary<int, BasicBreakpoint>();
        foreach (var b in requested)
        {
            var line = map.FindBySourceLine(b.Line);
            if (line == null)
            {
                response.Breakpoints.Add(new Breakpoint { Verified = false, Line = b.Line, Source = source, Message = "There's no BASIC line here or after it." });
                continue;
            }

            if (breakpoints.TryGetValue(line.LineNumber, out var existing))
            {
                response.Breakpoints.Add(existing.Breakpoint);
                continue;
            }

            var breakpoint = new Breakpoint { Id = _host.NewId(), Verified = true, Line = line.SourceLine, Source = source };
            breakpoints[line.LineNumber] = new BasicBreakpoint(breakpoint, b, line.LineNumber);
            response.Breakpoints.Add(breakpoint);
        }

        if (isListing)
            _listingBreakpoints = breakpoints;
        else
            _fileBreakpoints = breakpoints;

        var merged = new Dictionary<int, BasicBreakpoint>(_fileBreakpoints);
        foreach (var (line, b) in _listingBreakpoints)
            merged.TryAdd(line, b);
        _breakpoints = merged;

        return response;
    }

    #endregion

    #region Stack, scopes and variables

    private IReadOnlyList<BasicStackFrame> ReadFrames()
    {
        // stopped at a statement the frames start at the stack pointer, anywhere else at where it was at the last one
        var atStatement = _emulator.Pc == _symbols.Gone && _emulator.RomBankAct == _symbols.BasicBank;
        var sp = atStatement ? _emulator.StackPointer & 0xff : _statementStackPointer;
        return BasicStack.Read(_memory, sp, _symbols);
    }

    public StackTraceResponse? StackTrace(StackTraceArguments arguments)
    {
        var curlin = Curlin;
        if (IsDirect(curlin) || !_programRunning || InMachineCode)
            return null; // not in the program, so X16D's own CPU stack

        lock (_references)
        {
            var program = CurrentProgram();
            var response = new StackTraceResponse();
            response.StackFrames.Add(MakeFrame(program, curlin, Txtptr, true));

            foreach (var frame in ReadFrames())
            {
                if (frame is GosubFrame gosub)
                    response.StackFrames.Add(MakeFrame(program, gosub.LineNumber, gosub.TextPointer, false));
            }

            response.TotalFrames = response.StackFrames.Count;
            return response;
        }
    }

    private StackFrame MakeFrame(ShownProgram program, int lineNumber, int textPointer, bool top)
    {
        var id = _host.NewId();
        _frames.Add(id);

        var location = program.Map?.Locate(lineNumber, textPointer);
        if (location == null || program.Source == null)
            return new StackFrame(id, $"line {lineNumber}", 0, 0) { PresentationHint = StackFrame.PresentationHintValue.Label };

        var (start, end) = location.Columns;
        var line = location.Line.SourceLine;
        var text = line - 1 < program.Lines.Length ? program.Lines[line - 1] : "";
        var statement = end <= text.Length && start < end ? text[start..end].Trim() : "";
        if (statement.Length > 40)
            statement = statement[..37] + "...";

        return new StackFrame(id, top ? $"{lineNumber}: {statement}" : $"{lineNumber}: GOSUB", line, start + 1)
        {
            Source = program.Source,
            EndLine = line,
            EndColumn = end + 1,
        };
    }

    public ScopesResponse? Scopes(ScopesArguments arguments)
    {
        lock (_references)
        {
            if (!_frames.Contains(arguments.FrameId))
                return null;

            var response = new ScopesResponse();
            response.Scopes.Add(new Scope("Variables", AddReference(_ => SimpleVariables()), false) { PresentationHint = Scope.PresentationHintValue.Locals });
            response.Scopes.Add(new Scope("Arrays", AddReference(_ => Arrays()), false));
            response.Scopes.Add(new Scope("FOR loops", AddReference(_ => ForLoops()), false));
            response.Scopes.Add(new Scope("BASIC", AddReference(_ => BasicState()), true));
            return response;
        }
    }

    private int AddReference(Func<VariablesArguments, List<Variable>> provider)
    {
        var id = _host.NewId();
        _references[id] = provider;
        return id;
    }

    private int AddArrayReference(BasicArray array)
    {
        var id = AddReference(args => ArrayElements(array, args));
        _arrayReferences[id] = array.Name;
        return id;
    }

    private void ClearReferences()
    {
        lock (_references)
        {
            _references.Clear();
            _arrayReferences.Clear();
            _frames.Clear();
        }
    }

    public VariablesResponse? Variables(VariablesArguments arguments)
    {
        lock (_references)
        {
            if (!_references.TryGetValue(arguments.VariablesReference, out var provider))
                return null;

            var response = new VariablesResponse();
            response.Variables.AddRange(provider(arguments));
            return response;
        }
    }

    private static string TypeName(BasicVariableType type) => type switch
    {
        BasicVariableType.Float => "float",
        BasicVariableType.Integer => "integer",
        BasicVariableType.String => "string",
        BasicVariableType.Function => "function",
        _ => ""
    };

    private List<Variable> SimpleVariables()
    {
        var result = new List<Variable>();
        foreach (var v in BasicVariables.ReadVariables(_memory, _symbols).OrderBy(v => v.Name, StringComparer.Ordinal))
        {
            result.Add(new Variable(v.Name, v.Value.Display(_settings.PetsciiCase), 0)
            {
                Type = TypeName(v.Type),
                EvaluateName = v.Name,
                MemoryReference = $"0x{v.ValueAddress:X4}",
            });
        }
        return result;
    }

    private List<Variable> Arrays()
    {
        var result = new List<Variable>();
        foreach (var array in BasicVariables.ReadArrays(_memory, _symbols).OrderBy(a => a.Name, StringComparer.Ordinal))
        {
            var dims = string.Join(",", array.Dimensions.Select(d => d - 1));
            var elements = AddArrayReference(array);
            result.Add(new Variable($"{array.Name}({dims})", $"{TypeName(array.Type)} array, {array.Count} elements", elements)
            {
                Type = $"{TypeName(array.Type)}[]",
                EvaluateName = array.Name,
                IndexedVariables = array.Count,
                MemoryReference = $"0x{array.DataAddress:X4}",
            });
        }
        return result;
    }

    private List<Variable> ArrayElements(BasicArray array, VariablesArguments arguments)
    {
        var start = arguments.Start ?? 0;
        var count = Math.Min(arguments.Count ?? array.Count, array.Count - start);
        var result = new List<Variable>(Math.Max(count, 0));
        for (var i = start; i < start + count; i++)
        {
            var subscripts = array.SubscriptsOf(i);
            var name = $"({string.Join(",", subscripts)})";
            var value = BasicVariables.ReadElement(_memory, array.Type, array.DataAddress + i * array.ElementSize);
            result.Add(new Variable(name, value.Display(_settings.PetsciiCase), 0)
            {
                Type = TypeName(array.Type),
                EvaluateName = $"{array.Name}{name}",
            });
        }
        return result;
    }

    private List<Variable> ForLoops()
    {
        var variables = BasicVariables.ReadVariables(_memory, _symbols).ToDictionary(v => v.ValueAddress);
        var result = new List<Variable>();
        foreach (var frame in ReadFrames().OfType<ForFrame>())
        {
            var name = variables.TryGetValue(frame.VariableAddress, out var v) ? v.Name : $"${frame.VariableAddress:X4}";
            var current = v?.Value.Display() ?? "?";
            var step = frame.Step == 1 ? "" : $" STEP {BasicFloat.Format(frame.Step)}";
            result.Add(new Variable(name, $"{current} (TO {BasicFloat.Format(frame.Limit)}{step}, line {frame.LineNumber})", 0) { EvaluateName = name });
        }
        return result;
    }

    private List<Variable> BasicState()
    {
        Variable Word(string name, int address, bool hex = true)
        {
            var value = _memory.ReadWord(address);
            return new Variable(name, hex ? $"${value:X4}" : value.ToString(CultureInfo.InvariantCulture), 0) { MemoryReference = $"0x{value:X4}" };
        }

        var curlin = Curlin;
        var vartab = _memory.ReadWord(_symbols.Vartab);
        var strend = _memory.ReadWord(_symbols.Strend);
        var fretop = _memory.ReadWord(_symbols.Fretop);
        var datlin = _memory.ReadWord(_symbols.Datlin);

        return
        [
            new Variable("Line", IsDirect(curlin) ? "direct mode" : curlin.ToString(CultureInfo.InvariantCulture), 0),
            new Variable("DATA line", datlin.ToString(CultureInfo.InvariantCulture), 0),
            Word("DATA pointer", _symbols.Datptr),
            Word("Program start (TXTTAB)", _symbols.Txttab),
            Word("Variables (VARTAB)", _symbols.Vartab),
            Word("Arrays (ARYTAB)", _symbols.Arytab),
            Word("Arrays end (STREND)", _symbols.Strend),
            Word("Strings (FRETOP)", _symbols.Fretop),
            Word("Top of memory (MEMSIZ)", _symbols.Memsiz),
            new Variable("Free memory", $"{fretop - strend} bytes", 0),
            new Variable("Program size", $"{vartab - _memory.ReadWord(_symbols.Txttab)} bytes", 0),
        ];
    }

    public SetVariableResponse? SetVariable(SetVariableArguments arguments)
    {
        lock (_references)
        {
            if (!_references.ContainsKey(arguments.VariablesReference))
                return null;
        }

        var name = arguments.Name;
        var expression = new BasicExpression(_memory, _symbols);
        var target = expression.ResolveTarget(name.StartsWith('(') ? ArrayNameFor(arguments.VariablesReference) + name : name)
            ?? throw new ProtocolException($"Can't find '{name}'.");

        var value = expression.Evaluate(arguments.Value);
        target.Write(_memory, value);

        var updated = expression.Evaluate(target.Expression);
        return new SetVariableResponse { Value = updated.Display(_settings.PetsciiCase) };
    }

    // element rows are named "(1,2)", so the array comes from which list they're in
    private string ArrayNameFor(int reference)
    {
        lock (_references)
            return _arrayReferences.TryGetValue(reference, out var name) ? name : throw new ProtocolException("Can't tell which array this is.");
    }

    public EvaluateResponse? Evaluate(EvaluateArguments arguments)
    {
        if (_program == null && !_programRunning)
            return null;

        var text = arguments.Expression?.Trim() ?? "";
        if (text.Length == 0)
            return null;

        var expression = new BasicExpression(_memory, _symbols);

        // an array on its own, so it can be expanded
        if (BasicExpression.IsName(text))
        {
            var array = BasicVariables.FindArray(_memory, _symbols, text);
            if (array != null && BasicVariables.FindVariable(_memory, _symbols, text) == null)
            {
                lock (_references)
                {
                    var reference = AddArrayReference(array);
                    return new EvaluateResponse($"{TypeName(array.Type)} array, {array.Count} elements", reference) { IndexedVariables = array.Count };
                }
            }
        }

        try
        {
            var value = expression.Evaluate(text);
            return new EvaluateResponse(value.Display(_settings.PetsciiCase), 0);
        }
        catch (BasicExpressionException) when (arguments.Context == EvaluateArguments.ContextValue.Hover)
        {
            return null; // not BASIC, so let X16D try
        }
        catch (BasicExpressionException e) when (arguments.Context != EvaluateArguments.ContextValue.Repl)
        {
            throw new ProtocolException(e.Message);
        }
        catch (BasicExpressionException)
        {
            return null;
        }
    }



    #endregion
}
