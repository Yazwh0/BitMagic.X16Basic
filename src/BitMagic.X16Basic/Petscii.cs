using System.Globalization;
using System.Text;

namespace BitMagic.X16Basic;

/// <summary>
/// Which character set the source text is written for. This only changes how letters
/// inside string literals are encoded; outside quotes letters always become unshifted
/// PETSCII ($41-$5A), as that is the only form BASIC recognises keywords and variable names in.
/// </summary>
public enum PetsciiCase
{
    /// <summary>
    /// Upper case / graphics set (the X16's default). Letters of either case become $41-$5A,
    /// so <c>"Hello"</c> prints as <c>HELLO</c>.
    /// </summary>
    Upper,

    /// <summary>
    /// Lower / upper case set. Inside quotes, lower case letters become $41-$5A and upper case
    /// $C1-$DA, so <c>"Hello"</c> prints as written once the machine is in that set.
    /// </summary>
    Mixed
}

/// <summary>
/// A problem found while reading source text. <see cref="Column"/> is 0 based.
/// </summary>
public sealed record SourceDiagnostic(int SourceLine, int Column, string Message, bool IsError = true)
{
    public override string ToString() => $"{SourceLine}:{Column + 1}: {(IsError ? "error" : "warning")}: {Message}";
}

/// <summary>
/// Converts between source text and PETSCII. Control codes and characters with no ASCII
/// form are written in braces, petcat style: <c>{CLR}</c>, <c>{RED}</c>, <c>{$93}</c> or <c>{147}</c>.
/// </summary>
public static class Petscii
{
    public const byte Pi = 0xff;

    private static readonly (string Name, byte Value)[] _names =
    [
        ("CLR", 0x93), ("CLEAR", 0x93), ("HOME", 0x13),
        ("DOWN", 0x11), ("UP", 0x91), ("LEFT", 0x9d), ("RIGHT", 0x1d),
        ("RVS ON", 0x12), ("RVSON", 0x12), ("RVS OFF", 0x92), ("RVSOFF", 0x92),
        ("DEL", 0x14), ("INST", 0x94), ("INS", 0x94),
        ("SWLC", 0x0e), ("LOWER", 0x0e), ("SWUC", 0x8e), ("UPPER", 0x8e),
        ("BLK", 0x90), ("BLACK", 0x90), ("WHT", 0x05), ("WHITE", 0x05),
        ("RED", 0x1c), ("CYN", 0x9f), ("CYAN", 0x9f), ("PUR", 0x9c), ("PURPLE", 0x9c),
        ("GRN", 0x1e), ("GREEN", 0x1e), ("BLU", 0x1f), ("BLUE", 0x1f), ("YEL", 0x9e), ("YELLOW", 0x9e),
        ("ORNG", 0x81), ("ORANGE", 0x81), ("BRN", 0x95), ("BROWN", 0x95),
        ("LRED", 0x96), ("PINK", 0x96), ("GRY1", 0x97), ("DGRY", 0x97),
        ("GRY2", 0x98), ("GRY", 0x98), ("LGRN", 0x99), ("LBLU", 0x9a), ("GRY3", 0x9b), ("LGRY", 0x9b),
        ("F1", 0x85), ("F2", 0x89), ("F3", 0x86), ("F4", 0x8a), ("F5", 0x87), ("F6", 0x8b), ("F7", 0x88), ("F8", 0x8c),
        ("PI", Pi), ("POUND", 0x5c), ("UP ARROW", 0x5e), ("LEFT ARROW", 0x5f),
    ];

    private static readonly Dictionary<string, byte> _byName = BuildByName();
    private static readonly Dictionary<byte, string> _byValue = BuildByValue();

    private static Dictionary<string, byte> BuildByName()
    {
        var d = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in _names)
            d.TryAdd(name, value);
        return d;
    }

    private static Dictionary<byte, string> BuildByValue()
    {
        var d = new Dictionary<byte, string>();
        foreach (var (name, value) in _names)
            d.TryAdd(value, name); // first name listed is the one written back out
        return d;
    }

    /// <summary>
    /// Encodes one line of source text. Returns the bytes and, for each byte, the index of the
    /// source character it came from. Quotes are tracked here only to pick the letter mapping;
    /// tokenising is left to <see cref="Cruncher"/>.
    /// </summary>
    public static (byte[] Bytes, int[] Columns) EncodeLine(string text, PetsciiCase charset, int sourceLine, List<SourceDiagnostic>? diagnostics)
    {
        var bytes = new List<byte>(text.Length);
        var columns = new List<int>(text.Length);
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '{')
            {
                var end = text.IndexOf('}', i + 1);
                if (end == -1)
                {
                    diagnostics?.Add(new SourceDiagnostic(sourceLine, i, "'{' without a closing '}'."));
                    bytes.Add((byte)'{');  // not valid PETSCII, but keeps the columns lined up
                    columns.Add(i);
                    continue;
                }

                var name = text[(i + 1)..end];
                if (TryParseEscape(name, out var escaped))
                {
                    bytes.Add(escaped);
                    columns.Add(i);
                }
                else
                {
                    diagnostics?.Add(new SourceDiagnostic(sourceLine, i, $"Unknown character name '{{{name}}}'."));
                }
                i = end;
                continue;
            }

            if (c == '"')
                inQuotes = !inQuotes;

            if (!TryEncodeChar(c, inQuotes ? charset : PetsciiCase.Upper, out var b))
            {
                diagnostics?.Add(new SourceDiagnostic(sourceLine, i, $"'{c}' has no PETSCII equivalent, use {{$xx}}."));
                continue;
            }

            bytes.Add(b);
            columns.Add(i);
        }

        return (bytes.ToArray(), columns.ToArray());
    }

    public static bool TryParseEscape(string name, out byte value)
    {
        name = name.Trim();
        if (_byName.TryGetValue(name, out value))
            return true;

        if (name.StartsWith('$') && byte.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            return true;

        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && byte.TryParse(name.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            return true;

        return byte.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryEncodeChar(char c, PetsciiCase charset, out byte value)
    {
        switch (c)
        {
            case >= 'a' and <= 'z':
                value = (byte)(c - 'a' + 0x41);
                return true;
            case >= 'A' and <= 'Z':
                value = (byte)(charset == PetsciiCase.Mixed ? c - 'A' + 0xc1 : c - 'A' + 0x41);
                return true;
            case >= ' ' and <= '@':
            case '[' or ']':
                value = (byte)c;
                return true;
            case '\\' or '£':
                value = 0x5c;
                return true;
            case '^' or '↑':
                value = 0x5e;
                return true;
            case '_' or '←':
                value = 0x5f;
                return true;
            case 'π':
                value = Pi;
                return true;
            default:
                value = 0;
                return false;
        }
    }

    /// <summary>
    /// Decodes PETSCII to source text, the inverse of <see cref="EncodeLine"/>.
    /// </summary>
    public static void Decode(byte b, PetsciiCase charset, bool inQuotes, StringBuilder output)
    {
        var mixed = charset == PetsciiCase.Mixed && inQuotes;

        switch (b)
        {
            case >= 0x41 and <= 0x5a:
                output.Append(mixed ? (char)(b - 0x41 + 'a') : (char)b);
                return;
            case >= 0xc1 and <= 0xda when mixed:
                output.Append((char)(b - 0xc1 + 'A'));
                return;
            case >= 0x20 and <= 0x40:
            case 0x5b or 0x5d:
                output.Append((char)b);
                return;
            case 0x5c:
                output.Append('\\');
                return;
            case 0x5e:
                output.Append('^');
                return;
            case 0x5f:
                output.Append('_');
                return;
            case Pi:
                output.Append("{PI}");
                return;
        }

        output.Append('{');
        output.Append(_byValue.TryGetValue(b, out var name) ? name : $"${b:X2}");
        output.Append('}');
    }

    public static string Decode(ReadOnlySpan<byte> bytes, PetsciiCase charset = PetsciiCase.Upper)
    {
        var sb = new StringBuilder(bytes.Length);
        var inQuotes = false;
        foreach (var b in bytes)
        {
            if (b == '"')
                inQuotes = !inQuotes;
            Decode(b, charset, inQuotes, sb);
        }
        return sb.ToString();
    }
}
