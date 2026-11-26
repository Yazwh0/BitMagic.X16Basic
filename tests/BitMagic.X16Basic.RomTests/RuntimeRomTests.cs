namespace BitMagic.X16Basic.RomTests;

/// <summary>
/// Runs programs on the real ROM and checks what we read back from BASIC's memory: where
/// execution is, the variable and array tables, and the GOSUB / FOR frames on the stack.
/// </summary>
[TestClass]
public sealed class RuntimeRomTests
{
    private static readonly string[] Program =
    [
        "10 A=1:B%=-2:C$=\"HI\"+\"THERE\":D=3.25",
        "20 DIM E(2,3),F%(4),G$(1)",
        "30 FOR I=0 TO 2:FOR J=0 TO 3:E(I,J)=I*10+J:NEXT J,I",
        "40 F%(3)=-300:G$(1)=\"X\"",
        "50 DEF FNS(X)=X*2:Y=FNS(4)",
        "60 GOSUB 100",
        "70 END",
        "100 FOR K=1 TO 5 STEP 2",
        "110 H=K",
        "120 NEXT K:RETURN",
    ];

    private static (RomMachine Machine, SourceMap Map) Load(string[] source)
    {
        var machine = RomMachine.Boot();
        machine.Exec(source);

        var program = BasicTokeniser.Tokenise(string.Join('\n', source), new BasicTokeniserOptions { Keywords = machine.Keywords });
        var map = program.CreateSourceMap(machine.ReadWord(machine.Symbols.Txttab));
        CollectionAssert.AreEqual(program.ToMemoryImage(map.LoadAddress), machine.ProgramBytes());

        return (machine, map);
    }

    [TestMethod]
    public void VariablesAfterRunning()
    {
        var (machine, _) = Load(Program);
        machine.Exec("RUN");

        var s = machine.Symbols;
        var variables = BasicVariables.ReadVariables(machine, s).ToDictionary(v => v.Name);

        Assert.AreEqual(new BasicNumber(1), variables["A"].Value);
        Assert.AreEqual(new BasicInteger(-2), variables["B%"].Value);
        Assert.AreEqual("\"HITHERE\"", variables["C$"].Value.Display());
        Assert.AreEqual(new BasicNumber(3.25), variables["D"].Value);
        Assert.AreEqual(new BasicNumber(3), variables["I"].Value);
        Assert.AreEqual(new BasicNumber(4), variables["J"].Value);
        Assert.AreEqual(new BasicNumber(7), variables["K"].Value);
        Assert.AreEqual(new BasicNumber(5), variables["H"].Value);
        Assert.AreEqual(new BasicNumber(8), variables["Y"].Value);
        Assert.AreEqual(BasicVariableType.Function, variables["FNS"].Type);

        // C$ was built at run time, so it's in the string heap at the top of memory
        var c = (BasicString)variables["C$"].Value;
        Assert.IsGreaterThanOrEqualTo(machine.ReadWord(s.Fretop), c.Address);

        var arrays = BasicVariables.ReadArrays(machine, s).ToDictionary(a => a.Name);
        var e = arrays["E"];
        CollectionAssert.AreEqual(new[] { 3, 4 }, e.Dimensions);
        for (var i = 0; i <= 2; i++)
        for (var j = 0; j <= 3; j++)
            Assert.AreEqual(new BasicNumber(i * 10 + j), e.ReadElement(machine, i, j), $"E({i},{j})");

        Assert.AreEqual(new BasicInteger(-300), arrays["F%"].ReadElement(machine, 3));
        Assert.AreEqual(new BasicInteger(0), arrays["F%"].ReadElement(machine, 4));
        Assert.AreEqual("\"X\"", arrays["G$"].ReadElement(machine, 1).Display());
        Assert.AreEqual("\"\"", arrays["G$"].ReadElement(machine, 0).Display());
    }

    [TestMethod]
    public void WrittenValuesAreUsedByBasic()
    {
        var (machine, _) = Load(["10 A=1:B%=1", "20 STOP", "30 C=A*2+B%"]);
        machine.Exec("RUN");

        var s = machine.Symbols;
        var a = BasicVariables.FindVariable(machine, s, "A")!;
        var b = BasicVariables.FindVariable(machine, s, "B%")!;
        BasicVariables.WriteNumber(machine, a.Type, a.ValueAddress, 20.5);
        BasicVariables.WriteNumber(machine, b.Type, b.ValueAddress, -1000);

        machine.Exec("CONT");
        Assert.AreEqual(new BasicNumber(20.5 * 2 - 1000), BasicVariables.FindVariable(machine, s, "C")!.Value);
    }

    [TestMethod]
    public void LocationAndStackInsideGosubAndFor()
    {
        var (machine, map) = Load(Program);
        var s = machine.Symbols;

        // RUN, and stop at gone the first time line 110 is about to run
        int Curlin() => machine.ReadWord(s.Curlin);
        StartRun(machine);
        machine.RunTo(s.Gone, () => Curlin() == 110);

        var location = map.Locate(Curlin(), machine.ReadWord(s.Txtptr));
        Assert.IsNotNull(location);
        Assert.AreEqual(110, location.Line.LineNumber);
        Assert.AreEqual(0, location.Statement);

        var frames = BasicStack.Read(machine, machine.Emulator.StackPointer & 0xff, s);
        Assert.HasCount(2, frames, string.Join(", ", frames));

        var loop = (ForFrame)frames[0];
        var k = BasicVariables.FindVariable(machine, s, "K")!;
        Assert.AreEqual(k.ValueAddress, loop.VariableAddress);
        Assert.AreEqual(2.0, loop.Step);
        Assert.AreEqual(5.0, loop.Limit);
        Assert.AreEqual(100, loop.LineNumber);

        var gosub = (GosubFrame)frames[1];
        Assert.AreEqual(60, gosub.LineNumber);

        // on to the RETURN, the second statement on line 120
        machine.RunTo(s.Gone, () => Curlin() == 120 && map.Locate(Curlin(), machine.ReadWord(s.Txtptr))!.Statement == 1);
        frames = BasicStack.Read(machine, machine.Emulator.StackPointer & 0xff, s);
        Assert.HasCount(1, frames);
        Assert.IsInstanceOfType<GosubFrame>(frames[0]);

        // and back to line 70, with nothing on the stack
        machine.RunTo(s.Gone, () => Curlin() == 70);
        Assert.IsEmpty(BasicStack.Read(machine, machine.Emulator.StackPointer & 0xff, s));
    }

    [TestMethod]
    public void EveryStatementPassesThroughGone()
    {
        var (machine, map) = Load(["10 A=1:B=2:C=3", "20 IF A=1 THEN D=4:E=5", "30 GOTO 50", "40 F=6", "50 END"]);
        var s = machine.Symbols;

        var seen = new List<string>();
        StartRun(machine);
        while (true)
        {
            machine.RunTo(s.Gone);
            var location = map.Locate(machine.ReadWord(s.Curlin), machine.ReadWord(s.Txtptr));
            if (location == null)
            {
                if (seen.Count == 0)
                    continue; // the RUN itself, typed in direct mode
                break;
            }
            seen.Add($"{location.Line.LineNumber}.{location.Statement}");
            if (location.Line.LineNumber == 50)
                break;
        }

        // IF ... THEN D=4 runs D=4 inside the IF statement, so only E=5 starts a new one
        CollectionAssert.AreEqual(new[] { "10.0", "10.1", "10.2", "20.0", "20.1", "30.0", "50.0" }, seen, string.Join(" ", seen));
    }

    [TestMethod]
    public void ErrorHasNumberAndLine()
    {
        var (machine, _) = Load(["10 A=1", "20 B=A/0"]);
        var s = machine.Symbols;

        StartRun(machine);
        machine.RunTo(s.Error);

        Assert.AreEqual(20, machine.Emulator.X);
        Assert.AreEqual("DIVISION BY ZERO", BasicErrors.GetMessage(machine.Emulator.X));
        Assert.AreEqual(20, machine.ReadWord(s.Curlin));
    }

    /// <summary>
    /// The cost of leaving the emulator core at every statement, without X16D: a guide for the
    /// debugger's own overhead.
    /// </summary>
    [TestMethod]
    public void SpeedOfStoppingAtEveryStatement()
    {
        var (machine, _) = Load(["10 FOR I=1 TO 3000:NEXT", "20 END"]);
        var s = machine.Symbols;

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var statements = 0;
        machine.StartExec("RUN");
        machine.RunTo(s.Gone, () => { statements++; return machine.ReadWord(s.Curlin) == 20; }, timeout: TimeSpan.FromSeconds(60));
        watch.Stop();

        Console.WriteLine($"{statements} stops in {watch.Elapsed.TotalSeconds:0.00}s, {watch.Elapsed.TotalMilliseconds * 1000 / statements:0.0}us each");
    }

    /// <summary>samples/hello/demo.bas runs to the end, and its names weren't split up by keywords inside them.</summary>
    [TestMethod]
    public void DemoRuns()
    {
        var file = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "samples", "hello", "demo.bas"));
        var source = File.ReadAllLines(file).Where(l => l.Trim().Length > 0).ToArray();

        var (machine, _) = Load(source);
        var s = machine.Symbols;
        machine.StartExec("RUN");
        machine.RunTo(s.Ready, () => true, timeout: TimeSpan.FromSeconds(60));

        // the variables show the names came through whole: SCORE, say, would have been SC OR E
        var names = BasicVariables.ReadVariables(machine, s).Select(v => v.Name).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(new[] { "BE", "C", "DI", "FNSQ", "I", "LI%", "NA$", "PT", "RO", "RO$", "SP", "T", "X", "Y" }, names, string.Join(" ", names));
        Assert.AreEqual(new BasicNumber(20001), BasicVariables.FindVariable(machine, s, "T")!.Value);
        Assert.IsTrue(BasicExpression.ToNumber(BasicVariables.FindVariable(machine, s, "BEST")!.Value) > 0);

        var arrays = BasicVariables.ReadArrays(machine, s).Select(a => a.Name).ToArray();
        CollectionAssert.AreEquivalent(new[] { "SH$", "GR%", "HI" }, arrays);
        Assert.AreEqual("\"PYTHON\"", BasicVariables.FindArray(machine, s, "SHIP$")!.ReadElement(machine, 2).Display());
        Assert.AreEqual(new BasicInteger(4 + 30), BasicVariables.FindArray(machine, s, "GRID%")!.ReadElement(machine, 2, 3));
        Console.WriteLine(machine.ScreenText());
    }

    [TestMethod]
    public void ErrorMessagesMatchRom()
    {
        var machine = RomMachine.Boot();
        var bank = machine.Emulator.RomBank.Slice(machine.Symbols.BasicBank * 0x4000, 0x4000);
        var fromRom = BasicErrors.ReadFromRom(bank, machine.Symbols);
        for (var i = 1; i <= fromRom.Length; i++)
            Assert.AreEqual(fromRom[i - 1], BasicErrors.GetMessage(i), $"Error {i}");
    }

    /// <summary>
    /// Starts RUN through EXEC without waiting for the program to finish.
    /// </summary>
    private static void StartRun(RomMachine machine) => machine.StartExec("RUN");
}
