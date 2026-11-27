using BitMagic.X16Basic;

namespace BitMagic.X16Basic.Tests;

[TestClass]
public sealed class PrgTests
{
    private static byte[] Prg(string source, PetsciiCase charset = PetsciiCase.Upper)
    {
        var program = BasicTokeniser.Tokenise(source, new BasicTokeniserOptions { Charset = charset });
        Assert.IsFalse(program.HasErrors, string.Join(Environment.NewLine, program.Diagnostics));
        return program.ToPrg();
    }

    /// <summary>To a .PRG, back to text, and to a .PRG again: it has to be the same bytes.</summary>
    private static string AssertRoundTrip(string source, PetsciiCase charset = PetsciiCase.Upper)
    {
        var prg = Prg(source, charset);
        var text = BasicPrg.ToText(BasicPrg.Read(prg, charset: charset));
        var again = Prg(text, charset);
        CollectionAssert.AreEqual(prg, again, $"Text was:{Environment.NewLine}{text}");
        return text;
    }

    [TestMethod]
    public void ReadsAProgram()
    {
        var lines = BasicPrg.Read(Prg("10 PRINT \"HI\"\n20 GOTO 10\n"), out var loadAddress);

        Assert.AreEqual(0x0801, loadAddress);
        Assert.HasCount(2, lines);
        Assert.AreEqual("10 PRINT \"HI\"", lines[0].ToString());
        Assert.AreEqual("20 GOTO 10", lines[1].ToString());
    }

    [TestMethod]
    public void RoundTrips() => AssertRoundTrip("""
        10 REM A PROGRAM
        20 PRINT "{CLR}{RED}HELLO{WHT}":A=1:B$="X"
        30 FOR I=1 TO 10 STEP 2:PRINT I;:NEXT
        40 IF A=1 THEN GOSUB 100:ELSE GOTO 20
        50 VPOKE 1,$B000,65:SCREEN 128:COLOR 1,6
        60 DIM D(5),N$(2):D(3)=PEEK($9F20)
        70 DEF FN SQ(X)=X*X:PRINT FN SQ(3)
        80 DATA 1,"TWO",THREE
        90 END
        100 PRINT "{RVS ON}{PI}{$01}{RVS OFF}":RETURN
        """);

    [TestMethod]
    public void RoundTripsMixedCase() =>
        Assert.Contains("\"Hello World\"", AssertRoundTrip("10 PRINT \"Hello World\"\n", PetsciiCase.Mixed));

    [TestMethod]
    public void RoundTripsTheSamples()
    {
        var samples = Path.Combine(RepoRoot(), "samples", "hello");
        foreach (var file in Directory.GetFiles(samples, "*.bas"))
            AssertRoundTrip(File.ReadAllText(file));
    }

    [TestMethod]
    public void IgnoresMachineCodeAfterTheProgram()
    {
        // a SYS line, as most machine code programs start with, and some code after it
        var prg = Prg("10 SYS 2061\n").Concat(new byte[] { 0xa9, 0x00, 0x8d, 0x20, 0x9f, 0x60 }).ToArray();

        var lines = BasicPrg.Read(prg);

        Assert.HasCount(1, lines);
        Assert.AreEqual("10 SYS 2061", lines[0].ToString());
    }

    [TestMethod]
    public void FromMemoryIsWhatSaveWrites()
    {
        var program = BasicTokeniser.Tokenise("10 PRINT \"HI\"\n20 GOTO 10\n");
        CollectionAssert.AreEqual(program.ToPrg(), BasicPrg.FromMemory(program.ToMemoryImage()));
    }

    [TestMethod]
    [DataRow(new byte[] { 0x01, 0x08 }, "too short")]
    [DataRow(new byte[] { 0xff, 0xff, 0x01, 0x02, 0x03 }, "doesn't fit")]
    [DataRow(new byte[] { 0x01, 0x08, 0xa9, 0x00, 0x8d, 0x20, 0x9f, 0x60 }, "isn't a BASIC program")] // lda #0, sta $9f20, rts
    [DataRow(new byte[] { 0x01, 0x08, 0x07, 0x08, 0x0a, 0x00, 0x80, 0x00 }, "ends part way")] // 10 END, with no 00 00 after it
    public void RejectsWhatIsntBasic(byte[] prg, string because)
    {
        var e = Assert.ThrowsExactly<BasicPrgException>(() => BasicPrg.Read(prg));
        Assert.Contains(because, e.Message);
    }

    [TestMethod]
    public void RejectsLinesOutOfOrder()
    {
        // 20 then 10, joined by hand
        var prg = new byte[] { 0x01, 0x08, 0x07, 0x08, 0x14, 0x00, 0x80, 0x00, 0x0d, 0x08, 0x0a, 0x00, 0x80, 0x00, 0x00, 0x00 };
        var e = Assert.ThrowsExactly<BasicPrgException>(() => BasicPrg.Read(prg));
        Assert.Contains("line 10 comes after line 20", e.Message);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BitMagic.X16Basic.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Can't find the repo from " + AppContext.BaseDirectory);
    }
}
