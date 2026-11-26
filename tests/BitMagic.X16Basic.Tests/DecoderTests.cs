using BitMagic.X16Basic;

namespace BitMagic.X16Basic.Tests;

[TestClass]
public sealed class FloatTests
{
    [TestMethod]
    [DataRow(0.0, "0000000000")]
    [DataRow(1.0, "8100000000")]
    [DataRow(-1.0, "8180000000")]
    [DataRow(3.0, "8240000000")]
    [DataRow(0.5, "8000000000")]
    [DataRow(10.0, "8420000000")]
    [DataRow(Math.PI, "82490FDAA2")]
    [DataRow(0.1, "7D4CCCCCCD")]
    [DataRow(-32768.0, "9080000000")]
    public void EncodeDecode(double value, string hex)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.AreEqual(hex, Convert.ToHexString(BasicFloat.Encode(value)));
        Assert.AreEqual(value, BasicFloat.Decode(bytes), Math.Abs(value) * 1e-9);
    }

    [TestMethod]
    public void TooBig() =>
        Assert.ThrowsExactly<OverflowException>(() => BasicFloat.Encode(1e39));

    [TestMethod]
    public void Unpacked()
    {
        // STEP -2 as FOR leaves it: mantissa top bit kept, sign separate
        Assert.AreEqual(-2.0, BasicFloat.DecodeUnpacked(Convert.FromHexString("8280000000"), 0xff));
        Assert.AreEqual(2.0, BasicFloat.DecodeUnpacked(Convert.FromHexString("8280000000"), 0x01));
    }

    [TestMethod]
    [DataRow(1.0, "1")]
    [DataRow(-3.5, "-3.5")]
    [DataRow(0.25, ".25")]
    [DataRow(-0.25, "-.25")]
    [DataRow(123456789.0, "123456789")]
    public void Format(double value, string expected) => Assert.AreEqual(expected, BasicFloat.Format(value));
}

[TestClass]
public sealed class VariableTests
{
    // the variable table from the phase 0 spike, after running
    //   10 A=1:B=A+2 / 20 GOSUB100 / 30 PRINTB / 40 END / 100 A$="HI":RETURN
    private static ArrayMemory SpikeMemory()
    {
        var memory = new ArrayMemory();
        var program = BasicTokeniser.Tokenise("10 A=1:B=A+2\n20 GOSUB100\n30 PRINTB\n40 END\n100 A$=\"HI\":RETURN");
        memory.WriteBytes(0x801, program.ToMemoryImage());
        memory.WriteBytes(0x835, Convert.FromHexString("410081000000004200824000000041800 22D080000".Replace(" ", "")));

        var s = BasicRomSymbols.Default;
        memory.WriteWord(s.Txttab, 0x801);
        memory.WriteWord(s.Vartab, 0x835);
        memory.WriteWord(s.Arytab, 0x84a);
        memory.WriteWord(s.Strend, 0x84a);
        return memory;
    }

    [TestMethod]
    public void SimpleVariables()
    {
        var memory = SpikeMemory();
        var variables = BasicVariables.ReadVariables(memory, BasicRomSymbols.Default).ToList();

        Assert.AreEqual(3, variables.Count);
        Assert.AreEqual("A", variables[0].Name);
        Assert.AreEqual(new BasicNumber(1), variables[0].Value);
        Assert.AreEqual("B", variables[1].Name);
        Assert.AreEqual(new BasicNumber(3), variables[1].Value);
        Assert.AreEqual("A$", variables[2].Name);
        Assert.AreEqual(BasicVariableType.String, variables[2].Type);
        Assert.AreEqual("\"HI\"", variables[2].Value.Display());
        Assert.AreEqual(0x82d, ((BasicString)variables[2].Value).Address);
    }

    [TestMethod]
    public void FindAndWrite()
    {
        var memory = SpikeMemory();
        var b = BasicVariables.FindVariable(memory, BasicRomSymbols.Default, "B");
        Assert.IsNotNull(b);

        BasicVariables.WriteNumber(memory, b.Type, b.ValueAddress, -7.25);
        Assert.AreEqual(new BasicNumber(-7.25), BasicVariables.FindVariable(memory, BasicRomSymbols.Default, "B")!.Value);
        Assert.IsNull(BasicVariables.FindVariable(memory, BasicRomSymbols.Default, "B%"));
    }

    [TestMethod]
    [DataRow("A", "4100", "A")]
    [DataRow("AB", "4142", "AB")]
    [DataRow("ABC", "4142", "AB")]
    [DataRow("A%", "C180", "A%")]
    [DataRow("A1$", "41B1", "A1$")]
    [DataRow("FNX", "D800", "FNX")]
    public void Names(string name, string hex, string stored)
    {
        var (n0, n1) = BasicVariables.EncodeName(name);
        Assert.AreEqual(hex, Convert.ToHexString([n0, n1]));
        Assert.AreEqual(stored, BasicVariables.NameOf(n0, n1));
    }

    [TestMethod]
    public void Arrays()
    {
        // DIM A%(2,1): sizes stored high byte first, last subscript first
        var memory = new ArrayMemory();
        var s = BasicRomSymbols.Default;
        var header = Convert.FromHexString("C180" + "1500" + "02" + "0002" + "0003");
        memory.WriteBytes(0x900, header);
        for (var i = 0; i < 6; i++)
        {
            memory.WriteByte(0x909 + i * 2, 0);
            memory.WriteByte(0x90a + i * 2, (byte)(i * 10));
        }
        memory.WriteWord(s.Arytab, 0x900);
        memory.WriteWord(s.Strend, 0x915);

        var array = BasicVariables.ReadArrays(memory, s).Single();
        Assert.AreEqual("A%", array.Name);
        CollectionAssert.AreEqual(new[] { 3, 2 }, array.Dimensions);
        Assert.AreEqual(6, array.Count);
        Assert.AreEqual(new BasicInteger(10), array.ReadElement(memory, 1, 0));
        Assert.AreEqual(new BasicInteger(30), array.ReadElement(memory, 0, 1));
        CollectionAssert.AreEqual(new[] { 2, 1 }, array.SubscriptsOf(5));
    }
}

[TestClass]
public sealed class StackTests
{
    [TestMethod]
    public void GosubFrameFromSpike()
    {
        var memory = new ArrayMemory();
        memory.WriteBytes(0x1f4, Convert.FromHexString("8D140014083ECCE3010104F9"));

        var frames = BasicStack.Read(memory, 0xf3, BasicRomSymbols.Default);
        Assert.AreEqual(1, frames.Count);
        var gosub = (GosubFrame)frames[0];
        Assert.AreEqual(20, gosub.LineNumber);
        Assert.AreEqual(0x814, gosub.TextPointer);
    }

    [TestMethod]
    public void ForThenGosub()
    {
        var memory = new ArrayMemory();
        var p = 0x1e0;
        var forFrame = new List<byte> { 0x81, 0x35, 0x08 };
        forFrame.AddRange(Convert.FromHexString("8180000000")); // step 1, unpacked
        forFrame.Add(0x01);
        forFrame.AddRange(BasicFloat.Encode(10));
        forFrame.AddRange([0x0a, 0x00, 0x0c, 0x08]);
        memory.WriteBytes(p, forFrame.ToArray());
        memory.WriteBytes(p + 18, Convert.FromHexString("8D1400140 83ECC".Replace(" ", "")));

        var frames = BasicStack.Read(memory, p - 0x101, BasicRomSymbols.Default);
        Assert.AreEqual(2, frames.Count);
        var loop = (ForFrame)frames[0];
        Assert.AreEqual(0x835, loop.VariableAddress);
        Assert.AreEqual(1.0, loop.Step);
        Assert.AreEqual(10.0, loop.Limit);
        Assert.AreEqual(10, loop.LineNumber);
        Assert.IsInstanceOfType<GosubFrame>(frames[1]);
    }
}

[TestClass]
public sealed class ListingTests
{
    [TestMethod]
    [DataRow("10 PRINT \"HELLO\"")]
    [DataRow("20 FOR I=1 TO 10 STEP 2:NEXT")]
    [DataRow("30 IF A>=B THEN GOSUB 100")]
    [DataRow("40 VPOKE 1,$1000,X:COLOR 2,6:A=VPEEK(1,0)+MX")]
    [DataRow("50 REM \"STRINGS\" AND PRINT")]
    [DataRow("60 PRINT \"{CLR}{RED}HI\";CHR$(147)")]
    [DataRow("70 DATA 1,2,THREE")]
    public void RoundTrip(string line)
    {
        var program = BasicTokeniser.Tokenise(line);
        var memory = new ArrayMemory();
        memory.WriteBytes(0x801, program.ToMemoryImage());

        var listed = Detokeniser.List(memory, 0x801, KeywordTable.Default).Single();
        Assert.AreEqual(line, listed.ToString());
    }

    [TestMethod]
    public void Keywords()
    {
        var k = KeywordTable.Default;
        Assert.AreEqual(0x4c, k.MainCount);
        Assert.AreEqual("END", k.GetKeyword(0x80));
        Assert.AreEqual("GO", k.GetKeyword(0xcb));
        Assert.AreEqual("^", k.GetKeyword(0xae));
        Assert.AreEqual("MON", k.GetExtendedKeyword(0x80));
        Assert.AreEqual("RING", k.GetExtendedKeyword(0xc0));
        Assert.IsNull(k.GetExtendedKeyword(0xc1));
        Assert.AreEqual("VPEEK", k.GetExtendedKeyword(0xd0));
        Assert.AreEqual("TATTR", k.GetExtendedKeyword(0xdd));
        Assert.IsNull(k.GetExtendedKeyword(0xde));
    }
}

[TestClass]
public sealed class SourceMapTests
{
    [TestMethod]
    public void LocateStatements()
    {
        var program = BasicTokeniser.Tokenise("""
            10 A=1:B=2

            20 PRINT A
            """);
        var map = program.CreateSourceMap();

        // at gone, txtptr is on the ':' before the statement, or the last byte of the line's header
        var line10 = map.FindAddress(10)!.Value;
        var first = map.Locate(10, line10 + 3)!;
        Assert.AreEqual(0, first.Statement);

        var colon = line10 + 4 + 3;
        var second = map.Locate(10, colon)!;
        Assert.AreEqual(1, second.Statement);
        Assert.AreEqual((7, 10), second.Columns);

        Assert.IsNull(map.Locate(0xff00, 0x200));
        Assert.AreEqual(20, map.FindBySourceLine(2)!.LineNumber);
        Assert.AreEqual(3, map.FindBySourceLine(3)!.SourceLine);
        Assert.IsNull(map.FindBySourceLine(4));
    }
}

[TestClass]
public sealed class SymbolTests
{
    [TestMethod]
    public void Parse()
    {
        var missing = new List<string>();
        var symbols = BasicRomSymbols.Parse(["al 00CD00 .gone", "al 0003F0 .curlin", "junk"], missing);
        Assert.AreEqual(0xcd00, symbols.Gone);
        Assert.AreEqual(0x3f0, symbols.Curlin);
        Assert.AreEqual(BasicRomSymbols.Default.Newstt, symbols.Newstt);
        Assert.IsTrue(missing.Contains("newstt"));
    }
}
