using System.Reflection;

namespace BitMagic.X16Basic.RomTests;

/// <summary>
/// Finding BASIC's addresses in a ROM with no symbols.
/// </summary>
[TestClass]
public sealed class DetectTests
{
    private static byte[] BasicBank(string rom)
    {
        if (!File.Exists(rom))
            Assert.Inconclusive($"No ROM at '{rom}'.");
        return File.ReadAllBytes(rom).AsSpan(4 * 0x4000, 0x4000).ToArray();
    }

    [TestMethod]
    public void MatchesTheSymbols()
    {
        var bank = BasicBank(Path.Combine(RomMachine.RomDirectory, "rom.bin"));
        var expected = BasicRomSymbols.Load(Path.Combine(RomMachine.RomDirectory, "basic.sym"));

        var detected = BasicRomSymbols.Detect(bank, out var problem);
        Assert.IsNotNull(detected, problem);

        foreach (var property in typeof(BasicRomSymbols).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name is nameof(BasicRomSymbols.Stmdsp2) or nameof(BasicRomSymbols.Ptrfunc))
                continue; // only their difference is found
            Assert.AreEqual(property.GetValue(expected), property.GetValue(detected), property.Name);
        }

        Assert.AreEqual((expected.Ptrfunc - expected.Stmdsp2) / 2, (detected.Ptrfunc - detected.Stmdsp2) / 2);
    }

    /// <summary>The rom.bin X16D comes with, which has no symbols and is a different release.</summary>
    [TestMethod]
    public void FindsBasicInX16DsRom()
    {
        var rom = Path.Combine(Path.GetDirectoryName(typeof(DetectTests).Assembly.Location)!, "..", "..", "..", "..", "..", "..",
            "..", "BitMagic", "BitMagic.X16Debugger", "X16D", "bin", "x64", "Debug", "net10.0", "rom.bin");
        var bank = BasicBank(Path.GetFullPath(rom));

        var detected = BasicRomSymbols.Detect(bank, out var problem);
        Assert.IsNotNull(detected, problem);

        var keywords = KeywordTable.FromRom(bank, detected);
        Assert.AreEqual("END", keywords.GetKeyword(0x80));
        Assert.AreEqual("VPEEK", keywords.GetExtendedKeyword(0xd0));
        Console.WriteLine($"gone ${detected.Gone:X4}, curlin ${detected.Curlin:X4}, txtptr ${detected.Txtptr:X2}, txttab ${detected.Txttab:X2}, nmain ${detected.Nmain:X4}, error ${detected.Error:X4}, ready ${detected.Ready:X4}");
    }

    [TestMethod]
    public void NotBasic()
    {
        Assert.IsNull(BasicRomSymbols.Detect(new byte[0x4000], out var problem));
        Assert.IsNotNull(problem);
    }
}
