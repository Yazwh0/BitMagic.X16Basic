using System.Diagnostics;
using System.Reflection;
using System.Text;
using BitMagic.X16Emulator;

namespace BitMagic.X16Basic.RomTests;

/// <summary>
/// The BitMagic emulator booted into the real ROM, with helpers to run to a ROM routine and to
/// feed text to BASIC through EXEC, so lines go through the ROM's own <c>crunch</c> and line
/// insertion exactly as if typed.
/// </summary>
internal sealed class RomMachine : IMemoryWriter
{
    public Emulator Emulator { get; }
    public BasicRomSymbols Symbols { get; }
    public KeywordTable Keywords { get; }
    public static string RomDirectory { get; } = FindRomDirectory();

    private RomMachine(Emulator emulator, BasicRomSymbols symbols, KeywordTable keywords)
    {
        Emulator = emulator;
        Symbols = symbols;
        Keywords = keywords;
    }

    private static string FindRomDirectory()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("X16_ROM_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        return typeof(RomMachine).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "X16RomDirectory")?.Value ?? "";
    }

    /// <summary>
    /// Boots the ROM and runs until BASIC is waiting at the READY prompt. Marks the test
    /// inconclusive if there's no ROM to use.
    /// </summary>
    public static RomMachine Boot()
    {
        var rom = Path.Combine(RomDirectory, "rom.bin");
        var sym = Path.Combine(RomDirectory, "basic.sym");
        if (!File.Exists(rom) || !File.Exists(sym))
            Assert.Inconclusive($"No ROM at '{RomDirectory}'. Set X16_ROM_DIR to a folder with rom.bin and basic.sym.");

        var missing = new List<string>();
        var symbols = BasicRomSymbols.Load(sym, missing);
        Assert.IsEmpty(missing, $"basic.sym is missing: {string.Join(", ", missing)}");

        var emulator = new Emulator();
        emulator.Headless = true;
        emulator.FrameControl = FrameControl.Run;

        var romData = File.ReadAllBytes(rom);
        romData.CopyTo(emulator.RomBank);
        emulator.RomBankAct = 0;
        emulator.Pc = (ushort)(emulator.RomBank[0x3ffc] | (emulator.RomBank[0x3ffd] << 8));

        var basicBank = emulator.RomBank.Slice(symbols.BasicBank * 0x4000, 0x4000);
        var machine = new RomMachine(emulator, symbols, KeywordTable.FromRom(basicBank, symbols));

        machine.RunTo(symbols.Nmain);
        return machine;
    }

    /// <summary>
    /// Runs until the CPU is about to execute <paramref name="address"/> in the BASIC bank and
    /// <paramref name="until"/> (if given) is true there.
    /// </summary>
    public void RunTo(int address, Func<bool>? until = null, int? bank = null, TimeSpan? timeout = null)
    {
        bank ??= Symbols.BasicBank;
        var limit = Stopwatch.StartNew();
        timeout ??= TimeSpan.FromSeconds(20);

        // ROM addresses have an entry per bank, which the core copies into the active area when the
        // bank changes; set both, as BitMagic's GetMemoryLocations does
        var bankEntry = address >= 0xc000 ? 0x210000 + bank.Value * 0x4000 + address - 0xc000 : address;
        var previous = Emulator.Breakpoints[address];
        var previousBank = Emulator.Breakpoints[bankEntry];
        Emulator.Breakpoints[bankEntry] = previousBank | DebugBreakpoint;
        if (address < 0xc000 || Emulator.RomBankAct == bank)
            Emulator.Breakpoints[address] = previous | DebugBreakpoint;
        try
        {
            while (true)
            {
                if (limit.Elapsed > timeout)
                    Assert.Fail($"Didn't reach ${address:X4} in bank {bank} within {timeout.Value.TotalSeconds}s. PC is ${Emulator.Pc:X4} in bank {Emulator.RomBankAct}.");

                using (var cancel = new CancellationTokenSource(timeout.Value - limit.Elapsed))
                using (cancel.Token.Register(() => Emulator.Control = Control.Stop))
                {
                    var result = Emulator.Emulate();
                    if (cancel.IsCancellationRequested)
                        continue; // reported above

                    if (result != Emulator.EmulatorResult.Breakpoint)
                        Assert.Fail($"Emulation stopped with {result} at PC ${Emulator.Pc:X4}.");
                }

                if (Emulator.Pc == address && Emulator.RomBankAct == bank && (until == null || until()))
                    return;
            }
        }
        finally
        {
            Emulator.Breakpoints[bankEntry] = previousBank;
            if (address < 0xc000 || Emulator.RomBankAct == bank)
                Emulator.Breakpoints[address] = previous;
        }
    }

    private const uint DebugBreakpoint = 1; // DebugConstants.Breakpoint

    /// <summary>
    /// Types <paramref name="lines"/> in through EXEC, then runs until BASIC is back at the READY
    /// prompt waiting for input. Each line is given as the PETSCII bytes EXEC should feed.
    /// </summary>
    public void Exec(IEnumerable<byte[]> lines)
    {
        StartExec(lines);

        // nmain picks EXEC up each time round; it's done when nmain finds the flag cleared
        RunTo(Symbols.Nmain, () => ReadBankByte(0, Symbols.ExecFlag) == 0, timeout: TimeSpan.FromSeconds(60));
    }

    /// <summary>
    /// Sets EXEC up to type <paramref name="lines"/> in, without running. BASIC must be waiting
    /// at the READY prompt (stopped at <c>nmain</c>), as it is after <see cref="Boot"/> or <see cref="Exec"/>.
    /// </summary>
    public void StartExec(IEnumerable<byte[]> lines)
    {
        var text = new List<byte>();
        foreach (var line in lines)
        {
            text.AddRange(line);
            text.Add(13);
        }
        text.Add(0);

        // the text goes in RAM bank 1 at $A000, it carries on into the next banks if it's long
        Assert.IsLessThan(0x2000 * 32, text.Count);
        for (var i = 0; i < text.Count; i++)
            Emulator.RamBank[0x2000 + i] = text[i];

        WriteBankByte(0, Symbols.ExecFlag, 0x80);
        WriteBankByte(0, Symbols.ExecAddr, 0x00);
        WriteBankByte(0, Symbols.ExecAddr + 1, 0xa0);
        WriteBankByte(0, Symbols.ExecBank, 1);
    }

    public void Exec(params string[] lines) => Exec(Encode(lines));
    public void StartExec(params string[] lines) => StartExec(Encode(lines));

    private static IEnumerable<byte[]> Encode(string[] lines) =>
        lines.Select(l => Petscii.EncodeLine(l, PetsciiCase.Upper, 0, null).Bytes);

    public byte ReadBankByte(int bank, int address) => Emulator.RamBank[bank * 0x2000 + address - 0xa000];
    public void WriteBankByte(int bank, int address, byte value) => Emulator.RamBank[bank * 0x2000 + address - 0xa000] = value;

    /// <summary>Main RAM, with RAM bank 0 at $A000 (where BASIC keeps its own state).</summary>
    public byte ReadByte(int address)
    {
        address &= 0xffff;
        return address switch
        {
            < 0xa000 => Emulator.Memory[address],
            < 0xc000 => ReadBankByte(0, address),
            _ => Emulator.RomBank[(int)Emulator.RomBankAct * 0x4000 + address - 0xc000]
        };
    }

    public void WriteByte(int address, byte value)
    {
        address &= 0xffff;
        if (address < 0xa000)
            Emulator.Memory[address] = value;
        else if (address < 0xc000)
            WriteBankByte(0, address, value);
    }

    /// <summary>The program BASIC holds, from txttab to vartab.</summary>
    public byte[] ProgramBytes()
    {
        var start = this.ReadWord(Symbols.Txttab);
        var end = this.ReadWord(Symbols.Vartab);
        return this.ReadBytes(start, end - start);
    }

    /// <summary>What's on the text screen, for failure messages.</summary>
    public string ScreenText(int rows = 60, int columns = 80)
    {
        // default text mode: layer 1 map at $1B000, 128 columns of (char, colour)
        var sb = new StringBuilder();
        var vram = Emulator.Vera.Vram;
        for (var y = 0; y < rows; y++)
        {
            var line = new StringBuilder();
            for (var x = 0; x < columns; x++)
            {
                var c = vram[0x1b000 + (y * 128 + x) * 2];
                line.Append(c switch
                {
                    >= 1 and <= 26 => (char)('A' + c - 1),
                    >= 0x20 and <= 0x3f => (char)c,
                    0 => '@',
                    _ => '.'
                });
            }
            var s = line.ToString().TrimEnd();
            if (s.Length > 0)
                sb.AppendLine(s);
        }
        return sb.ToString();
    }
}
