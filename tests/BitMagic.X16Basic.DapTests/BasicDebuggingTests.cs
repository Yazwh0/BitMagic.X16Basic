using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;
using Newtonsoft.Json.Linq;

namespace BitMagic.X16Basic.DapTests;

[TestClass]
public sealed class BasicDebuggingTests
{
    // source lines:  1   2                     3          4                              5          6                7      8  9          10
    private const string Program = "10 REM TEST\n20 A=1:B%=2:C$=\"HI\"\n30 DIM D(3)\n40 FOR I=0 TO 3:D(I)=I*I:NEXT\n50 GOSUB 100\n60 PRINT \"DONE\";A\n70 END\n\n100 A=A+1\n110 RETURN\n";

    private static void AssertAt(DapClient client, int sourceLine, string? name = null)
    {
        var top = client.StackTrace()[0];
        Assert.AreEqual(sourceLine, top.Line, $"Stopped at '{top.Name}' line {top.Line}");
        if (name != null)
            Assert.AreEqual(name, top.Name);
    }

    [TestMethod]
    public void BreakpointStackAndVariables()
    {
        using var client = DapClient.Start();
        var source = client.Launch(Program, breakpointLines: [9]);

        var stop = client.WaitForStop();
        Assert.AreEqual(StoppedEvent.ReasonValue.Breakpoint, stop.Reason);

        var frames = client.StackTrace();
        Assert.HasCount(2, frames);
        Assert.AreEqual("100: A=A+1", frames[0].Name);
        Assert.AreEqual(9, frames[0].Line);
        Assert.AreEqual(5, frames[0].Column);
        Assert.AreEqual(source, frames[0].Source.Path);
        Assert.AreEqual(5, frames[1].Line); // the GOSUB on line 50

        var variables = client.ScopeVariables("Variables");
        Assert.AreEqual("1", variables["A"].Value);
        Assert.AreEqual("2", variables["B%"].Value);
        Assert.AreEqual("\"HI\"", variables["C$"].Value);
        Assert.AreEqual("4", variables["I"].Value);

        var arrays = client.ScopeVariables("Arrays");
        var d = arrays["D(3)"];
        var elements = client.Variables(d.VariablesReference);
        CollectionAssert.AreEqual(new[] { "0", "1", "4", "9" }, elements.Select(e => e.Value).ToArray());

        Assert.AreEqual("3", client.Evaluate("A+B%"));
        Assert.AreEqual("4", client.Evaluate("D(2)"));
        Assert.AreEqual("\"HIHI\"", client.Evaluate("C$+C$"));

        client.Continue();
        Assert.IsTrue(client.WaitForTerminate(5) == false, "The program should stay at READY by default.");
    }

    /// <summary>BitMagic's own ROM, which has no symbols, so the addresses are found in the ROM.</summary>
    [TestMethod]
    public void StoppingDrawsTheWholeScreen()
    {
        using var client = DapClient.Start();
        client.Launch("10 PRINT \"{CLR}HELLO\"\n20 FOR I=1 TO 5:PRINT I:NEXT\n30 A=1\n40 GOTO 40\n", breakpointLines: [3]);
        Assert.AreEqual(StoppedEvent.ReasonValue.Breakpoint, client.WaitForStop().Reason);

        // what the window has is the same as drawing the screen afresh, so the PRINTs show
        Assert.AreEqual(client.Screen("fromState"), client.Screen());
    }

    [TestMethod]
    public void TypingReachesTheProgram()
    {
        using var client = DapClient.Start();
        client.Launch("10 GET K$:IF K$=\"\" GOTO 10\n20 A=1\n30 GOTO 30\n", breakpointLines: [2]);

        // let BASIC get to the GET loop, then press and let go of a key, as the screen tab does
        System.Threading.Thread.Sleep(2000);
        Assert.IsTrue(client.Key("Q", true));
        System.Threading.Thread.Sleep(100);
        Assert.IsTrue(client.Key("Q", false));

        Assert.AreEqual(StoppedEvent.ReasonValue.Breakpoint, client.WaitForStop().Reason);
        Assert.AreEqual("\"Q\"", client.Evaluate("K$"));
    }

    [TestMethod]
    public void WorksWithX16DsRomAndNoSymbols()
    {
        using var client = DapClient.Start(x16dRom: true);
        client.Launch(Program, breakpointLines: [9]);

        Assert.AreEqual(StoppedEvent.ReasonValue.Breakpoint, client.WaitForStop().Reason);
        AssertAt(client, 9, "100: A=A+1");
        Assert.AreEqual("1", client.Evaluate("A"));
        Assert.AreEqual("9", client.Evaluate("D(3)"));
        StringAssert.Contains(string.Join("", client.Output), "Found BASIC in the ROM");
    }

    [TestMethod]
    public void ListingWhenTheProgramChanges()
    {
        using var client = DapClient.Start();
        client.Launch("10 A=1\n20 B=2\n30 STOP\n", new JObject { ["stopOnEntry"] = true });
        client.WaitForStop();

        var frame = client.StackTrace()[0];
        Assert.IsNotNull(frame.Source.Path, "Starts in the source file");

        // change A=1 to C=1, as editing at READY or a POKE would
        client.WriteMemory(0x805, (byte)'C');

        client.StepIn();
        client.WaitForStop();
        frame = client.StackTrace()[0];
        Assert.IsNull(frame.Source.Path);
        Assert.IsGreaterThan(0, frame.Source.SourceReference ?? 0);
        Assert.AreEqual(2, frame.Line);
        Assert.AreEqual("20: B=2", frame.Name);

        var listing = client.SourceText(frame.Source.SourceReference!.Value);
        Assert.AreEqual("10 C=1\n20 B=2\n30 STOP", listing);
    }

    /// <summary>
    /// How long a tight loop takes under the debugger, compared with the same loop without it
    /// (TI is the jiffy clock, 60 a second, so the program times itself in emulated time).
    /// </summary>
    [TestMethod]
    public void Speed()
    {
        using var client = DapClient.Start();
        var source = client.Launch("10 T=TI\n20 FOR I=1 TO 3000:NEXT\n30 E=TI-T\n40 STOP\n", new JObject { ["stopOnEntry"] = true });
        client.WaitForStop();
        client.SetBreakpoints(source, 4);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        client.Continue();
        client.WaitForStop(120);
        watch.Stop();

        var jiffies = double.Parse(client.Evaluate("E"), System.Globalization.CultureInfo.InvariantCulture);
        Console.WriteLine($"3000 iterations: {watch.Elapsed.TotalSeconds:0.00}s real, {jiffies / 60:0.00}s emulated");

        // X16D runs in real time, so near enough the same; it was 200 times slower before the mode's
        // breakpoints skipped X16D's per stop work
        Assert.IsLessThan(jiffies / 60 * 3 + 1, watch.Elapsed.TotalSeconds);
    }

    [TestMethod]
    public void LiveStateWhileRunning()
    {
        using var client = DapClient.Start();
        client.Launch("10 A$=\"X\":DIM B(2)\n20 FOR I=1 TO 1E9\n30 NEXT\n");

        // let it get going
        JToken? live = null;
        for (var i = 0; i < 40 && live?["running"]?.Value<bool>() != true; i++)
        {
            System.Threading.Thread.Sleep(250);
            live = client.ModeRequest("live");
        }

        Assert.IsNotNull(live);
        Assert.IsTrue(live["running"]!.Value<bool>(), live.ToString());
        Assert.IsTrue(live["line"]!.Value<int>() is 20 or 30, live.ToString());
        Assert.IsTrue(live["sourceLine"]!.Value<int>() is 2 or 3, live.ToString());

        var variables = live["variables"]!.ToDictionary(v => v["name"]!.Value<string>()!, v => v["value"]!.Value<string>()!);
        Assert.AreEqual("\"X\"", variables["A$"]);
        Assert.IsGreaterThan(0, double.Parse(variables["I"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual("B(2)", live["arrays"]![0]!["name"]!.Value<string>());

        // still running: nothing stopped it
        var again = client.ModeRequest("live")!;
        Assert.IsTrue(again["running"]!.Value<bool>());
    }

    [TestMethod]
    public void StepIntoSysGoesToTheMachineCode()
    {
        using var client = DapClient.Start();
        // $1000: LDA #42, STA $02, RTS
        var source = client.Launch("10 FOR I=0 TO 4:READ B:POKE $1000+I,B:NEXT\n20 SYS $1000\n30 A=PEEK(2)\n40 STOP\n50 DATA 169,42,133,2,96\n",
            breakpointLines: [2]);
        client.WaitForStop();
        AssertAt(client, 2, "20: SYS $1000");

        client.StepIn();
        var sysStop = client.WaitForStop();
        Assert.AreEqual(StoppedEvent.ReasonValue.Step, sysStop.Reason, $"{sysStop.Description} at {client.StackTrace()[0].Name} {client.StackTrace()[0].InstructionPointerReference}");
        var frame = client.StackTrace()[0];
        Assert.IsTrue(frame.InstructionPointerReference?.Contains("1000") == true || frame.Name.Contains("1000"),
            $"Expected to be at $1000, at '{frame.Name}' ({frame.InstructionPointerReference})");

        // and back to BASIC
        client.SetBreakpoints(source, 4);
        client.Continue();
        client.WaitForStop();
        AssertAt(client, 4);
        Assert.AreEqual("42", client.Evaluate("A"));
    }

    /// <summary>
    /// The mode from its NuGet package, from the packages the build makes, as a project names it when
    /// there's no VS Code extension to pass the assembly.
    /// </summary>
    [TestMethod]
    public void LoadsTheModeFromANuGetPackage()
    {
        if (!File.Exists(Path.Combine(DapClient.PackageFolder, "BitMagic.X16Basic.Debugger.0.1.0.nupkg")))
            Assert.Inconclusive($"No packages in '{DapClient.PackageFolder}'; daptests.sh makes them.");

        using var client = DapClient.Start();
        client.Launch(Program, breakpointLines: [9], debugMode: new JObject
        {
            ["package"] = "BitMagic.X16Basic.Debugger",
            ["source"] = DapClient.PackageFolder,
        });

        Assert.AreEqual(StoppedEvent.ReasonValue.Breakpoint, client.WaitForStop().Reason);
        AssertAt(client, 9, "100: A=A+1");

        var output = string.Join("", client.Output);
        StringAssert.Contains(output, "Downloading BitMagic.X16Basic.Debugger 0.1.0");

        // both packages are in the cache, but none of X16D's own assemblies
        var cached = Directory.GetFiles(Path.Combine(DapClient.PackageCache, "bitmagic.x16basic.debugger", "0.1.0")).Select(Path.GetFileName).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "BitMagic.X16Basic.Debugger.dll", "BitMagic.X16Basic.dll" }, cached, string.Join(", ", cached));
        Assert.IsFalse(cached.Any(f => f!.StartsWith("BitMagic.X16Debugger")), string.Join(", ", cached));
    }

    [TestMethod]
    public void Stepping()
    {
        using var client = DapClient.Start();
        client.Launch(Program, new JObject { ["stopOnEntry"] = true });

        Assert.AreEqual(StoppedEvent.ReasonValue.Entry, client.WaitForStop().Reason);
        AssertAt(client, 1);

        client.StepIn();
        client.WaitForStop();
        AssertAt(client, 2, "20: A=1");

        client.StepOver();
        client.WaitForStop();
        AssertAt(client, 2, "20: B%=2");

        // over the loop, a statement at a time, to the GOSUB
        for (var i = 0; i < 100; i++)
        {
            client.StepOver();
            client.WaitForStop();
            if (client.StackTrace()[0].Line == 5)
                break;
        }
        AssertAt(client, 5, "50: GOSUB 100");

        client.StepIn();
        client.WaitForStop();
        AssertAt(client, 9, "100: A=A+1");
        Assert.HasCount(2, client.StackTrace());

        client.StepOut();
        client.WaitForStop();
        AssertAt(client, 6, "60: PRINT \"DONE\";A");

        // and back in to check step over goes over a GOSUB
        client.Continue();
    }

    [TestMethod]
    public void StepOverGosub()
    {
        using var client = DapClient.Start();
        client.Launch(Program, breakpointLines: [5]);
        client.WaitForStop();
        AssertAt(client, 5);

        client.StepOver();
        client.WaitForStop();
        AssertAt(client, 6);
        Assert.AreEqual("2", client.Evaluate("A"));
    }

    [TestMethod]
    public void StopsOnError()
    {
        using var client = DapClient.Start();
        client.Launch("10 A=1\n20 B=A/0\n");

        var stop = client.WaitForStop();
        Assert.AreEqual(StoppedEvent.ReasonValue.Exception, stop.Reason);
        Assert.AreEqual("?DIVISION BY ZERO ERROR IN 20", client.ExceptionInfo().Description);
        AssertAt(client, 2);
    }

    [TestMethod]
    public void TerminatesAtEndIfAsked()
    {
        using var client = DapClient.Start();
        client.Launch("10 A=1\n20 END\n", new JObject { ["onProgramEnd"] = "terminate" });
        Assert.IsTrue(client.WaitForTerminate(), "Expected the session to end with the program.");
    }

    [TestMethod]
    public void ConditionsLogPointsAndSettingVariables()
    {
        using var client = DapClient.Start();
        var source = client.Launch("10 FOR I=1 TO 5\n20 T=T+I\n30 NEXT\n40 PRINT T\n50 STOP\n", new JObject { ["stopOnEntry"] = true });
        client.WaitForStop();

        client.SetBreakpoints(source, new SourceBreakpoint(2) { Condition = "I=3" });
        client.Continue();

        var stop = client.WaitForStop();
        Assert.AreEqual(StoppedEvent.ReasonValue.Breakpoint, stop.Reason);
        Assert.AreEqual("3", client.Evaluate("I"));
        Assert.AreEqual("3", client.Evaluate("T"));

        var frame = client.StackTrace()[0];
        var variables = client.Scopes(frame.Id).Single(s => s.Name == "Variables").VariablesReference;
        Assert.AreEqual("100", client.SetVariable(variables, "T", "100").Value);

        client.SetBreakpoints(source, 5);
        client.Continue();
        client.WaitForStop();
        Assert.AreEqual("112", client.Evaluate("T")); // 100 + 3 + 4 + 5
    }

    [TestMethod]
    public void LogPoint()
    {
        using var client = DapClient.Start();
        var source = client.Launch("10 FOR I=1 TO 3\n20 T=T+I\n30 NEXT\n40 STOP\n", new JObject { ["stopOnEntry"] = true });
        client.WaitForStop();

        client.SetBreakpoints(source,
            new SourceBreakpoint(2) { LogMessage = "I is {I}, T is {T}" },
            new SourceBreakpoint(4));
        client.Continue();
        client.WaitForStop();

        var output = string.Join("", client.Output);
        StringAssert.Contains(output, "I is 1, T is 0");
        StringAssert.Contains(output, "I is 3, T is 3");
    }
}
