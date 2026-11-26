using System.Text;

namespace BitMagic.X16Basic;

/// <summary>
/// BASIC's keyword lists, held the way the ROM holds them: each keyword's last byte has bit 7
/// set, and each list ends with a 0. <see cref="Cruncher"/> walks these byte for byte exactly as
/// <c>crunch</c> does, so quirks of the ROM's matching come out the same.
///
/// <list type="bullet">
/// <item><see cref="Main"/> is <c>reslst</c>: tokens $80 to $CB.</item>
/// <item><see cref="Extended"/> and <see cref="Extended2"/> are <c>reslst2</c> and <c>reslst3</c>,
/// numbered on from each other: written as $CE then $80 + n for the first
/// <see cref="ExtendedStatementCount"/> (statements), then $D0 onwards (functions).</item>
/// </list>
/// </summary>
public sealed class KeywordTable
{
    public const byte ExtendedToken = 0xce;
    public const byte ExtendedFunctionBase = 0xd0;

    public byte[] Main { get; }
    public byte[] Extended { get; }
    public byte[] Extended2 { get; }
    public int ExtendedStatementCount { get; }

    private readonly string[] _main;
    private readonly string[] _extended;

    public KeywordTable(byte[] main, byte[] extended, byte[] extended2, int extendedStatementCount)
    {
        Main = main;
        Extended = extended;
        Extended2 = extended2;
        ExtendedStatementCount = extendedStatementCount;

        _main = Split(main).ToArray();
        _extended = Split(extended).Concat(Split(extended2)).ToArray();

        if (_main.Length > 0x80)
            throw new ArgumentException("Too many keywords in the main list.", nameof(main));
    }

    public int MainCount => _main.Length;
    public int ExtendedCount => _extended.Length;
    public IReadOnlyList<string> MainKeywords => _main;
    public IReadOnlyList<string> ExtendedKeywords => _extended;

    public const byte EndToken = 0x80;
    public const byte ForToken = 0x81;
    public const byte DataToken = 0x83;
    public const byte GotoToken = 0x89;
    public const byte GosubToken = 0x8d;
    public const byte RemToken = 0x8f;
    public const byte PrintToken = 0x99;

    /// <summary>
    /// The text of a single byte token ($80 to $CB), or null if it isn't one.
    /// </summary>
    public string? GetKeyword(byte token)
    {
        var i = token - 0x80;
        return i >= 0 && i < _main.Length ? _main[i] : null;
    }

    /// <summary>
    /// The text of the byte that follows a $CE, or null if it isn't a keyword.
    /// </summary>
    public string? GetExtendedKeyword(byte token)
    {
        int i;
        if (token >= ExtendedFunctionBase)
            i = ExtendedStatementCount + token - ExtendedFunctionBase;
        else if (token >= 0x80 && token - 0x80 < ExtendedStatementCount)
            i = token - 0x80;
        else
            return null;

        return i < _extended.Length ? _extended[i] : null;
    }

    /// <summary>
    /// The byte written after $CE for the n'th extended keyword.
    /// </summary>
    public byte ExtendedTokenFor(int index) =>
        index < ExtendedStatementCount
            ? (byte)(0x80 | index)
            : (byte)(ExtendedFunctionBase + index - ExtendedStatementCount);

    private static IEnumerable<string> Split(byte[] list)
    {
        var sb = new StringBuilder();
        foreach (var b in list)
        {
            if (b == 0 && sb.Length == 0)
                yield break;

            var c = (byte)(b & 0x7f);
            Petscii.Decode(c, PetsciiCase.Upper, false, sb);

            if ((b & 0x80) != 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
    }

    /// <summary>
    /// Reads the keyword lists out of the BASIC ROM bank (16KB, mapped at $C000).
    /// </summary>
    public static KeywordTable FromRom(ReadOnlySpan<byte> basicBank, BasicRomSymbols symbols)
    {
        static byte[] ReadList(ReadOnlySpan<byte> bank, int address)
        {
            var offset = address - 0xc000;
            var end = offset;
            // each entry ends on a byte with bit 7 set, the list on a 0 where an entry would start
            while (bank[end] != 0)
            {
                while ((bank[end] & 0x80) == 0)
                    end++;
                end++;
            }
            return bank[offset..(end + 1)].ToArray();
        }

        return new KeywordTable(
            ReadList(basicBank, symbols.Reslst),
            ReadList(basicBank, symbols.Reslst2),
            ReadList(basicBank, symbols.Reslst3),
            (symbols.Ptrfunc - symbols.Stmdsp2) / 2);
    }

    private static byte[] Build(params string[] keywords)
    {
        var bytes = new List<byte>();
        foreach (var k in keywords)
        {
            for (var i = 0; i < k.Length; i++)
            {
                if (!Petscii.TryEncodeChar(k[i], PetsciiCase.Upper, out var b))
                    throw new ArgumentException($"Bad keyword '{k}'.");
                bytes.Add(i == k.Length - 1 ? (byte)(b | 0x80) : b);
            }
        }
        bytes.Add(0);
        return bytes.ToArray();
    }

    /// <summary>
    /// The lists from x16-rom's basic/tokens.s and token2.s. Use <see cref="FromRom"/> to match
    /// a particular ROM exactly.
    /// </summary>
    public static KeywordTable Default { get; } = new(
        Build(
            "END", "FOR", "NEXT", "DATA", "INPUT#", "INPUT", "DIM", "READ", "LET", "GOTO", "RUN", "IF", "RESTORE",
            "GOSUB", "RETURN", "REM", "STOP", "ON", "WAIT", "LOAD", "SAVE", "VERIFY", "DEF", "POKE", "PRINT#", "PRINT",
            "CONT", "LIST", "CLR", "CMD", "SYS", "OPEN", "CLOSE", "GET", "NEW",
            "TAB(", "TO", "FN", "SPC(", "THEN", "NOT", "STEP", "+", "-", "*", "/", "^", "AND", "OR", ">", "=", "<",
            "SGN", "INT", "ABS", "USR", "FRE", "POS", "SQR", "RND", "LOG", "EXP", "COS", "SIN", "TAN", "ATN", "PEEK",
            "LEN", "STR$", "VAL", "ASC", "CHR$", "LEFT$", "RIGHT$", "MID$", "GO"),
        Build(
            "MON", "DOS", "OLD", "GEOS", "VPOKE", "VLOAD", "SCREEN", "PSET", "LINE", "FRAME", "RECT", "CHAR", "MOUSE",
            "COLOR", "TEST", "RESET", "CLS", "CODEX", "LOCATE", "BOOT", "KEYMAP", "BLOAD", "BVLOAD", "BVERIFY", "BANK",
            "FMINIT", "FMNOTE", "FMDRUM", "FMINST", "FMVIB", "FMFREQ", "FMVOL", "FMPAN", "FMPLAY", "FMCHORD", "FMPOKE",
            "PSGINIT", "PSGNOTE", "PSGVOL", "PSGWAV", "PSGFREQ", "PSGPAN", "PSGPLAY", "PSGCHORD"),
        Build(
            "REBOOT", "POWEROFF", "I2CPOKE", "SLEEP", "BSAVE", "MENU", "REN", "LINPUT#", "LINPUT", "BINPUT#", "HELP",
            "BANNER", "EXEC", "TILE", "EDIT", "SPRITE", "SPRMEM", "MOVSPR", "BASLOAD", "OVAL", "RING",
            "VPEEK", "MX", "MY", "MB", "JOY", "HEX$", "BIN$", "I2CPEEK", "POINTER", "STRPTR", "RPT$", "MWHEEL", "TDATA", "TATTR"),
        65);
}
