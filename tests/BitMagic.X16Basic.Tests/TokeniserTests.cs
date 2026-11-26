using BitMagic.X16Basic;

namespace BitMagic.X16Basic.Tests;

[TestClass]
public sealed class TokeniserTests
{
    private static byte[] Body(string line, PetsciiCase charset = PetsciiCase.Upper)
    {
        var diagnostics = new List<SourceDiagnostic>();
        var result = BasicTokeniser.TokeniseLine(line, 1, new BasicTokeniserOptions { Charset = charset }, diagnostics);
        Assert.IsNotNull(result, string.Join(Environment.NewLine, diagnostics));
        return result.Body;
    }

    private static void AssertBody(string line, params byte[] expected) =>
        CollectionAssert.AreEqual(expected, Body(line), $"Got {Convert.ToHexString(Body(line))}");

    [TestMethod]
    public void PrintString() =>
        AssertBody("10 PRINT \"HELLO\"", 0x99, 0x20, 0x22, 0x48, 0x45, 0x4c, 0x4c, 0x4f, 0x22);

    [TestMethod]
    public void Assignments() =>
        AssertBody("10 A=1:B=A+2", 0x41, 0xb2, 0x31, 0x3a, 0x42, 0xb2, 0x41, 0xaa, 0x32);

    [TestMethod]
    public void LowerCaseKeywords() =>
        AssertBody("10 print a", 0x99, 0x20, 0x41);

    [TestMethod]
    public void KeywordsInsideNames() =>
        AssertBody("10 FORM=1TO2", 0x81, 0x4d, 0xb2, 0x31, 0xa4, 0x32);

    [TestMethod]
    public void QuestionMarkIsPrint() =>
        AssertBody("10 ?A", 0x99, 0x41);

    [TestMethod]
    public void PrintHashBeforePrint() =>
        AssertBody("10 PRINT#1", 0x98, 0x31);

    [TestMethod]
    public void GoToWithSpace() =>
        AssertBody("10 GO TO 10", 0xcb, 0x20, 0xa4, 0x20, 0x31, 0x30);

    [TestMethod]
    public void RemIsLiteral() =>
        AssertBody("10 REM PRINT \"X", 0x8f, 0x20, 0x50, 0x52, 0x49, 0x4e, 0x54, 0x20, 0x22, 0x58);

    [TestMethod]
    public void DataIsLiteralToColon() =>
        AssertBody("10 DATA PRINT,\"A:B\":PRINT",
            0x83, 0x20, 0x50, 0x52, 0x49, 0x4e, 0x54, 0x2c, 0x22, 0x41, 0x3a, 0x42, 0x22, 0x3a, 0x99);

    [TestMethod]
    public void ExtendedStatement() =>
        AssertBody("10 VPOKE 1,2,3", 0xce, 0x84, 0x20, 0x31, 0x2c, 0x32, 0x2c, 0x33);

    [TestMethod]
    public void ExtendedStatementColor() =>
        AssertBody("10 COLOR 1", 0xce, 0x8d, 0x20, 0x31);

    [TestMethod]
    public void ExtendedFunctions()
    {
        AssertBody("10 A=VPEEK(1,2)", 0x41, 0xb2, 0xce, 0xd0, 0x28, 0x31, 0x2c, 0x32, 0x29);
        AssertBody("10 X=MX", 0x58, 0xb2, 0xce, 0xd1);
    }

    [TestMethod]
    public void ExtendedStatementFromSecondList() =>
        AssertBody("10 RING", 0xce, 0xc0);

    [TestMethod]
    public void ShiftedLetterAbbreviates() =>
        AssertBody("10 P{$CF}1", 0x97, 0x31); // P, shifted O is POKE

    [TestMethod]
    public void SpacesInLineNumber()
    {
        var line = BasicTokeniser.TokeniseLine("  1 0 PRINT", 1);
        Assert.IsNotNull(line);
        Assert.AreEqual(10, line.LineNumber);
        CollectionAssert.AreEqual(new byte[] { 0x99 }, line.Body);
    }

    [TestMethod]
    public void LineNumberTooBig()
    {
        var diagnostics = new List<SourceDiagnostic>();
        Assert.IsNull(BasicTokeniser.TokeniseLine("64000 PRINT", 1, null, diagnostics));
        Assert.AreEqual(1, diagnostics.Count);
        Assert.IsNotNull(BasicTokeniser.TokeniseLine("63999 PRINT", 1));
    }

    [TestMethod]
    public void NoLineNumber()
    {
        var program = BasicTokeniser.Tokenise("PRINT \"HI\"");
        Assert.IsTrue(program.HasErrors);
        Assert.AreEqual(0, program.Lines.Count);
    }

    [TestMethod]
    public void MixedCaseStrings() =>
        CollectionAssert.AreEqual(new byte[] { 0x99, 0x22, 0xc8, 0x49, 0x22 }, Body("10 PRINT\"Hi\"", PetsciiCase.Mixed));

    [TestMethod]
    public void UpperCaseStringsFold() =>
        CollectionAssert.AreEqual(new byte[] { 0x99, 0x22, 0x48, 0x49, 0x22 }, Body("10 PRINT\"Hi\""));

    [TestMethod]
    public void ControlCodes() =>
        AssertBody("10 PRINT\"{CLR}{RED}{$01}\"", 0x99, 0x22, 0x93, 0x1c, 0x01, 0x22);

    [TestMethod]
    public void ProgramImage()
    {
        var program = BasicTokeniser.Tokenise("""
            10 A=1:B=A+2
            20 GOSUB100
            30 PRINTB
            40 END
            100 A$="HI":RETURN
            """);

        Assert.IsFalse(program.HasErrors);

        // the same bytes the ROM stored in the phase 0 spike
        var expected = Convert.FromHexString(
            "0F080A0041B2313A42B241AA3200" +
            "180814008D31303000" +
            "1F081E00994200" +
            "250828008000" +
            "330864004124B2224849223A8E00" +
            "0000");

        CollectionAssert.AreEqual(expected, program.ToMemoryImage(), Convert.ToHexString(program.ToMemoryImage()));
    }

    [TestMethod]
    public void LinesAreSortedAndReplaced()
    {
        var program = BasicTokeniser.Tokenise("""
            20 PRINT 2
            10 PRINT 1
            20 PRINT 3
            """);

        Assert.AreEqual(2, program.Lines.Count);
        Assert.AreEqual(10, program.Lines[0].LineNumber);
        Assert.AreEqual(3, program.Lines[1].SourceLine);
        Assert.IsFalse(program.HasErrors);
        Assert.IsTrue(program.Diagnostics.Count >= 1);
    }

    [TestMethod]
    public void Statements()
    {
        var line = BasicTokeniser.TokeniseLine("10 A=1: PRINT \"A:B\" :REM X:Y", 1)!;
        Assert.AreEqual(3, line.StatementStarts.Length);
        Assert.AreEqual((3, 6), line.StatementColumns(0));
        Assert.AreEqual((8, 19), line.StatementColumns(1));
        Assert.AreEqual((21, 28), line.StatementColumns(2));
    }

    [TestMethod]
    public void ExtendedTokenThatLooksLikeRem()
    {
        // RESET is $CE $8F, and $8F on its own is REM
        var line = BasicTokeniser.TokeniseLine("10 RESET:PRINT", 1)!;
        CollectionAssert.AreEqual(new byte[] { 0xce, 0x8f, 0x3a, 0x99 }, line.Body);
        Assert.AreEqual(2, line.StatementStarts.Length);
    }
}
