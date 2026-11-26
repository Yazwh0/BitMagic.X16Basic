using System.Globalization;

namespace BitMagic.X16Basic;

/// <summary>
/// Addresses in the BASIC ROM and its variables. These move between ROM releases, so load them
/// from the ROM's <c>basic.sym</c> with <see cref="Load"/>; <see cref="Default"/> matches the
/// x16-rom build these were taken from.
/// </summary>
public sealed class BasicRomSymbols
{
    /// <summary>The ROM bank BASIC runs from.</summary>
    public int BasicBank { get; init; } = 4;

    // routines, in the BASIC bank
    /// <summary>Start of each statement. <c>txtptr</c> is at the end of the previous one.</summary>
    public int Newstt { get; init; } = 0xcc00;
    /// <summary>About to run a statement: <c>curlin</c> is its line and it starts at <c>txtptr</c>+1.</summary>
    public int Gone { get; init; } = 0xcc36;
    /// <summary>Where <c>jsr gone3</c> is; a GOSUB frame sits on top of its return address.</summary>
    public int Ngone1 { get; init; } = 0xcc3c;
    /// <summary>BASIC error, with the error number in X.</summary>
    public int Error { get; init; } = 0xc6c8;
    /// <summary>Prints READY and goes back to direct mode.</summary>
    public int Ready { get; init; } = 0xe2dc;
    public int Main { get; init; } = 0xc713;
    public int Nmain { get; init; } = 0xc719;
    public int Crunch { get; init; } = 0xc8bd;
    public int Reslst { get; init; } = 0xc13e;
    public int Reslst2 { get; init; } = 0xc23e;
    public int Reslst3 { get; init; } = 0xc328;
    public int Stmdsp2 { get; init; } = 0xc0a0;
    public int Ptrfunc { get; init; } = 0xc122;
    public int Errtab { get; init; } = 0xc562;

    // variables, in main RAM
    public int Txttab { get; init; } = 0x00df;
    public int Txtptr { get; init; } = 0x00ee;
    public int Forpnt { get; init; } = 0x00e5;
    public int Vartab { get; init; } = 0x03e1;
    public int Arytab { get; init; } = 0x03e3;
    public int Strend { get; init; } = 0x03e5;
    public int Fretop { get; init; } = 0x03e7;
    public int Memsiz { get; init; } = 0x03e9;
    public int Curlin { get; init; } = 0x03eb;
    public int Oldlin { get; init; } = 0x03ed;
    public int Oldtxt { get; init; } = 0x03ef;
    public int Datlin { get; init; } = 0x03f1;
    public int Datptr { get; init; } = 0x03f3;
    public int Crambank { get; init; } = 0x03ff;

    // EXEC's state, in RAM bank 0
    public int ExecFlag { get; init; } = 0xad0d;
    public int ExecAddr { get; init; } = 0xad0e;
    public int ExecBank { get; init; } = 0xad10;

    /// <summary>The return address <c>jsr gone3</c> leaves on the stack.</summary>
    public int GoneReturnAddress => Ngone1 + 2;

    public static BasicRomSymbols Default { get; } = new();

    /// <summary>
    /// Finds the addresses by looking for the code that uses them, for a ROM there are no symbols
    /// for. The interpreter's core has hardly changed in years, so this finds what the debugger
    /// needs; the rest keep their defaults. Returns null, with the reason, if the ROM doesn't look
    /// like X16 BASIC.
    /// </summary>
    public static BasicRomSymbols? Detect(ReadOnlySpan<byte> basicBank, out string? problem, int basicBankNumber = 4)
    {
        const int any = -1;
        int Find(ReadOnlySpan<byte> bank, int[] pattern, int from = 0)
        {
            for (var i = from; i + pattern.Length <= bank.Length; i++)
            {
                var j = 0;
                while (j < pattern.Length && (pattern[j] == any || bank[i + j] == pattern[j]))
                    j++;
                if (j == pattern.Length)
                    return i;
            }
            return -1;
        }

        int Word(ReadOnlySpan<byte> bank, int offset) => bank[offset] | (bank[offset + 1] << 8);
        int[] Text(string s) => s.Select((c, i) => i == s.Length - 1 ? c | 0x80 : c).ToArray();

        // newstt, after its jsr iscntc, through to gone's jmp (igone)
        int[] newstt =
        [
            0xa5, any, 0xa4, any, 0xc0, 0x02, 0xf0, any, 0x8d, any, any, 0x8c, any, any, 0xa0, 0x00,
            0xb1, any, 0xd0, any, 0xa0, 0x02, 0xb1, any, 0x18, 0xd0, 0x03, 0x4c, any, any, 0xc8, 0xb1,
            any, 0x8d, any, any, 0xc8, 0xb1, any, 0x8d, any, any, 0x98, 0x65, any, 0x85, any, 0x90,
            0x02, 0xe6, any, 0x6c, any, any
        ];
        var m = Find(basicBank, newstt);
        if (m < 3 || basicBank[m - 3] != 0x20)
        {
            problem = "Couldn't find the statement loop (newstt).";
            return null;
        }

        var txtptr = basicBank[m + 1];
        var oldtxt = Word(basicBank, m + 9);
        var curlin = Word(basicBank, m + 34);
        var igone = Word(basicBank, m + 52);
        var ierror = igone - 8; // the vectors are ierror, imain, icrnch, iqplop, igone

        if (oldtxt != curlin + 4)
        {
            problem = "BASIC's variables aren't laid out as expected.";
            return null;
        }

        // error is jmp (ierror); ready is ldx #$80 then the same jump
        int[] jumpError = [0x6c, ierror & 0xff, ierror >> 8];
        int error = -1, ready = -1;
        for (var p = Find(basicBank, jumpError); p != -1; p = Find(basicBank, jumpError, p + 1))
        {
            if (p >= 2 && basicBank[p - 2] == 0xa2 && basicBank[p - 1] == 0x80)
                ready = p - 2;
            else if (error == -1)
                error = p;
        }

        // nmain: stz ram_bank, lda exec_flag, beq, lda exec_addr, sta, lda exec_addr+1, sta, lda exec_bank, sta ram_bank
        var nmain = Find(basicBank, [0x64, 0x00, 0xad, any, any, 0xf0, any, 0xad, any, any, 0x85, any, 0xad, any, any, 0x85, any, 0xad, any, any, 0x85, 0x00]);

        // stxtpt: clc, lda txttab, adc #$ff, sta txtptr, lda txttab+1, adc #$ff, sta txtptr+1, rts
        var stxtpt = Find(basicBank, [0x18, 0xa5, any, 0x69, 0xff, 0x85, txtptr, 0xa5, any, 0x69, 0xff, 0x85, txtptr + 1, 0x60]);

        var reslst = Find(basicBank, [.. Text("END"), .. Text("FOR")]);
        var reslst2 = Find(basicBank, [.. Text("MON"), .. Text("DOS")]);
        var reslst3 = Find(basicBank, Text("REBOOT"));

        var notFound = new (string Name, int Offset)[] { ("error", error), ("ready", ready), ("nmain", nmain), ("stxtpt", stxtpt), ("reslst", reslst), ("reslst2", reslst2), ("reslst3", reslst3) }
            .Where(i => i.Offset == -1).Select(i => i.Name).ToList();
        if (notFound.Count > 0)
        {
            problem = $"Couldn't find {string.Join(", ", notFound)}.";
            return null;
        }

        // the extended statements are the keywords before the first function, VPEEK
        var keywordsFrom = new KeywordTable(basicBank[reslst..].ToArray(), ReadList(basicBank, reslst2), ReadList(basicBank, reslst3), 0);
        var statements = keywordsFrom.ExtendedKeywords.ToList().IndexOf("VPEEK");
        if (statements < 0)
        {
            problem = "Couldn't find where the extended functions start.";
            return null;
        }

        problem = null;
        var execFlag = Word(basicBank, nmain + 3);
        return new BasicRomSymbols
        {
            BasicBank = basicBankNumber,
            Newstt = 0xc000 + m - 3,
            Gone = 0xc000 + m + 51,
            Ngone1 = 0xc000 + m + 57,
            Error = 0xc000 + error,
            Ready = 0xc000 + ready,
            Nmain = 0xc000 + nmain,
            Main = Default.Main,
            Crunch = Default.Crunch,
            Reslst = 0xc000 + reslst,
            Reslst2 = 0xc000 + reslst2,
            Reslst3 = 0xc000 + reslst3,
            // only used for the number of extended statements, (Ptrfunc - Stmdsp2) / 2
            Stmdsp2 = 0,
            Ptrfunc = statements * 2,
            Errtab = Default.Errtab,
            Txttab = basicBank[stxtpt + 2],
            Txtptr = txtptr,
            Forpnt = Default.Forpnt,
            Vartab = curlin - 10,
            Arytab = curlin - 8,
            Strend = curlin - 6,
            Fretop = curlin - 4,
            Memsiz = curlin - 2,
            Curlin = curlin,
            Oldlin = curlin + 2,
            Oldtxt = curlin + 4,
            Datlin = curlin + 6,
            Datptr = curlin + 8,
            Crambank = Default.Crambank,
            ExecFlag = execFlag,
            ExecAddr = Word(basicBank, nmain + 8),
            ExecBank = Word(basicBank, nmain + 18),
        };

        static byte[] ReadList(ReadOnlySpan<byte> bank, int offset)
        {
            var end = offset;
            while (end < bank.Length && bank[end] != 0)
            {
                while (end < bank.Length && (bank[end] & 0x80) == 0)
                    end++;
                end++;
            }
            return bank[offset..Math.Min(end + 1, bank.Length)].ToArray();
        }
    }

    /// <summary>
    /// Reads a VICE label file as the x16-rom build writes them (<c>al 00CC36 .gone</c>).
    /// Anything missing keeps its default, and is listed in <paramref name="missing"/>.
    /// </summary>
    public static BasicRomSymbols Load(string path, List<string>? missing = null) =>
        Parse(File.ReadLines(path), missing);

    public static BasicRomSymbols Parse(IEnumerable<string> lines, List<string>? missing = null)
    {
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || parts[0] != "al")
                continue;
            if (!int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                continue;
            labels.TryAdd(parts[2].TrimStart('.'), value & 0xffff);
        }

        var d = Default;
        int Get(string name, int fallback)
        {
            if (labels.TryGetValue(name, out var v))
                return v;
            missing?.Add(name);
            return fallback;
        }

        return new BasicRomSymbols
        {
            Newstt = Get("newstt", d.Newstt),
            Gone = Get("gone", d.Gone),
            Ngone1 = Get("ngone1", d.Ngone1),
            Error = Get("error", d.Error),
            Ready = Get("ready", d.Ready),
            Main = Get("main", d.Main),
            Nmain = Get("nmain", d.Nmain),
            Crunch = Get("crunch", d.Crunch),
            Reslst = Get("reslst", d.Reslst),
            Reslst2 = Get("reslst2", d.Reslst2),
            Reslst3 = Get("reslst3", d.Reslst3),
            Stmdsp2 = Get("stmdsp2", d.Stmdsp2),
            Ptrfunc = Get("ptrfunc", d.Ptrfunc),
            Errtab = Get("errtab", d.Errtab),
            Txttab = Get("txttab", d.Txttab),
            Txtptr = Get("txtptr", d.Txtptr),
            Forpnt = Get("forpnt", d.Forpnt),
            Vartab = Get("vartab", d.Vartab),
            Arytab = Get("arytab", d.Arytab),
            Strend = Get("strend", d.Strend),
            Fretop = Get("fretop", d.Fretop),
            Memsiz = Get("memsiz", d.Memsiz),
            Curlin = Get("curlin", d.Curlin),
            Oldlin = Get("oldlin", d.Oldlin),
            Oldtxt = Get("oldtxt", d.Oldtxt),
            Datlin = Get("datlin", d.Datlin),
            Datptr = Get("datptr", d.Datptr),
            Crambank = Get("crambank", d.Crambank),
            ExecFlag = Get("exec_flag", d.ExecFlag),
            ExecAddr = Get("exec_addr", d.ExecAddr),
            ExecBank = Get("exec_bank", d.ExecBank),
        };
    }
}
