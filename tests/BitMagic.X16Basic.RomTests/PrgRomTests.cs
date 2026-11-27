using BitMagic.X16Basic;

namespace BitMagic.X16Basic.RomTests;

/// <summary>
/// Programs typed into the real ROM, saved as SAVE would (the load address and the program in memory), read back
/// as text and tokenised again: it has to come out the same bytes.
/// </summary>
[TestClass]
public sealed class PrgRomTests
{
    private static void AssertRoundTrip(RomMachine machine, PetsciiCase charset = PetsciiCase.Upper)
    {
        var prg = BasicPrg.FromMemory(machine.ProgramBytes(), machine.ReadWord(machine.Symbols.Txttab));
        var text = BasicPrg.ToText(BasicPrg.Read(prg, machine.Keywords, charset));

        var again = BasicTokeniser.Tokenise(text, new BasicTokeniserOptions { Keywords = machine.Keywords, Charset = charset });
        Assert.IsFalse(again.HasErrors, string.Join(Environment.NewLine, again.Diagnostics));
        CollectionAssert.AreEqual(prg, again.ToPrg(), $"Text was:{Environment.NewLine}{text}");
    }

    [TestMethod]
    public void WholeCorpus()
    {
        var machine = RomMachine.Boot();
        machine.Exec(TokeniserRomTests.Corpus);
        AssertRoundTrip(machine);
    }

    [TestMethod]
    public void MixedCase()
    {
        string[] source = ["10 PRINT \"Hello World\"", "20 A$=\"MiXeD\":REM Not In Quotes"];
        var machine = RomMachine.Boot();
        machine.Exec(source.Select(l => Petscii.EncodeLine(l, PetsciiCase.Mixed, 0, null).Bytes));
        AssertRoundTrip(machine, PetsciiCase.Mixed);
    }

    [TestMethod]
    public void ControlCodes()
    {
        string[] source = ["10 PRINT \"{CLR}{RED}RED{WHT} {RVS ON}REVERSED{RVS OFF}\"", "20 PRINT \"{HOME}{DOWN}{RIGHT}{PI}\""];
        var machine = RomMachine.Boot();
        machine.Exec(source.Select(l => Petscii.EncodeLine(l, PetsciiCase.Upper, 0, null).Bytes));
        AssertRoundTrip(machine);
    }
}
