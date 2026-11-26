namespace BitMagic.X16Basic.Tests;

[TestClass]
public sealed class ExpressionTests
{
    private static readonly BasicRomSymbols S = BasicRomSymbols.Default;

    // A=1, B=3, A$="HI" (in the program text), N%=-5, and DIM E(2) with E(1)=7
    private static ArrayMemory Memory()
    {
        var memory = new ArrayMemory();
        memory.WriteBytes(0x82d, "HI"u8);

        var vars = new List<byte>();
        vars.AddRange([0x41, 0x00, .. BasicFloat.Encode(1)]);
        vars.AddRange([0x42, 0x00, .. BasicFloat.Encode(3)]);
        vars.AddRange([0x41, 0x80, 0x02, 0x2d, 0x08, 0, 0]);
        vars.AddRange([0xce, 0x80, 0xff, 0xfb, 0, 0, 0]);
        memory.WriteBytes(0x900, vars.ToArray());
        var arytab = 0x900 + vars.Count;

        var array = new List<byte> { 0x45, 0x00, 0, 0, 1, 0x00, 0x03 };
        array.AddRange(BasicFloat.Encode(0));
        array.AddRange(BasicFloat.Encode(7));
        array.AddRange(BasicFloat.Encode(0));
        array[2] = (byte)array.Count;
        memory.WriteBytes(arytab, array.ToArray());

        memory.WriteWord(S.Vartab, 0x900);
        memory.WriteWord(S.Arytab, arytab);
        memory.WriteWord(S.Strend, arytab + array.Count);
        memory.WriteWord(S.Fretop, 0x9f00);
        memory.WriteWord(S.Memsiz, 0x9f00);
        return memory;
    }

    private static string Eval(string expression, ArrayMemory? memory = null) =>
        new BasicExpression(memory ?? Memory(), S).Evaluate(expression).Display();

    [TestMethod]
    [DataRow("A", "1")]
    [DataRow("A+B*2", "7")]
    [DataRow("(A+B)*2", "8")]
    [DataRow("-B^2", "-9")]
    [DataRow("2^3^2", "64")] // left to right, as in BASIC
    [DataRow("N%", "-5")]
    [DataRow("N%*2", "-10")]
    [DataRow("E(1)", "7")]
    [DataRow("E(A)+1", "8")]
    [DataRow("A$", "\"HI\"")]
    [DataRow("A$+\"!\"", "\"HI!\"")]
    [DataRow("LEN(A$)", "2")]
    [DataRow("LEFT$(A$,1)", "\"H\"")]
    [DataRow("MID$(\"HELLO\",2,3)", "\"ELL\"")]
    [DataRow("ASC(A$)", "72")]
    [DataRow("$FF+%101", "260")]
    [DataRow("B>A", "-1")]
    [DataRow("B<A", "0")]
    [DataRow("A=1 AND B=3", "-1")]
    [DataRow("A=2 OR B=3", "-1")]
    [DataRow("NOT 0", "-1")]
    [DataRow("A$=\"HI\"", "-1")]
    [DataRow("INT(-1.5)", "-2")]
    [DataRow("1E3/4", "250")]
    [DataRow("ZZ+1", "1")] // unset variables are 0, as in BASIC
    public void Evaluates(string expression, string expected) => Assert.AreEqual(expected, Eval(expression));

    [TestMethod]
    public void BareUnknownNameThrows()
    {
        Assert.ThrowsExactly<BasicExpressionException>(() => Eval("PRINT"));
        Assert.AreEqual("0", new BasicExpression(Memory(), S).Evaluate("ZZ", false).Display());
    }

    [TestMethod]
    public void Errors()
    {
        Assert.ThrowsExactly<BasicExpressionException>(() => Eval("A+"));
        Assert.ThrowsExactly<BasicExpressionException>(() => Eval("A$+1"));
        Assert.ThrowsExactly<BasicExpressionException>(() => Eval("E(5)"));
        Assert.ThrowsExactly<BasicExpressionException>(() => Eval("1/0"));
    }

    [TestMethod]
    public void Conditions()
    {
        var e = new BasicExpression(Memory(), S);
        Assert.IsTrue(e.IsTrue("B=3"));
        Assert.IsFalse(e.IsTrue("B=4"));
        Assert.IsFalse(e.IsTrue("UNSET"));
    }

    [TestMethod]
    public void WriteTargets()
    {
        var memory = Memory();
        var e = new BasicExpression(memory, S);

        e.ResolveTarget("B")!.Write(memory, new BasicNumber(2.5));
        Assert.AreEqual("2.5", Eval("B", memory));

        e.ResolveTarget("N%")!.Write(memory, new BasicNumber(300));
        Assert.AreEqual("300", Eval("N%", memory));

        e.ResolveTarget("E(2)")!.Write(memory, new BasicNumber(-1));
        Assert.AreEqual("-1", Eval("E(2)", memory));

        // strings go in the string space under FRETOP
        e.ResolveTarget("A$")!.Write(memory, e.Evaluate("\"HELLO\""));
        Assert.AreEqual("\"HELLO\"", Eval("A$", memory));
        Assert.AreEqual(0x9f00 - 5, memory.ReadWord(S.Fretop));

        Assert.ThrowsExactly<BasicExpressionException>(() => e.ResolveTarget("B")!.Write(memory, e.Evaluate("\"X\"")));
        Assert.ThrowsExactly<BasicExpressionException>(() => e.ResolveTarget("Q"));
    }
}
