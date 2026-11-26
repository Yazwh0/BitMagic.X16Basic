using System.Text;

namespace BitMagic.X16Basic;

/// <summary>
/// Turns tokenised lines back into text the way LIST does (basic/code3.s): keywords are
/// expanded everywhere outside quotes, including after REM.
/// </summary>
public static class Detokeniser
{
    /// <summary>
    /// The text of one line's body (the bytes after its number, without the closing 0).
    /// <paramref name="starts"/>, if given, receives the column each input byte starts at.
    /// </summary>
    public static string DetokeniseLine(ReadOnlySpan<byte> body, KeywordTable keywords, PetsciiCase charset = PetsciiCase.Upper, List<int>? starts = null)
    {
        var sb = new StringBuilder(body.Length * 2);
        var inQuotes = false;

        for (var i = 0; i < body.Length; i++)
        {
            var b = body[i];
            starts?.Add(sb.Length);

            if (b == '"')
            {
                inQuotes = !inQuotes;
                sb.Append('"');
                continue;
            }

            if (b < 0x80 || b == Petscii.Pi || inQuotes)
            {
                Petscii.Decode(b, charset, inQuotes, sb);
                continue;
            }

            if (b == KeywordTable.ExtendedToken && i + 1 < body.Length)
            {
                var ext = keywords.GetExtendedKeyword(body[i + 1]);
                if (ext != null)
                {
                    starts?.Add(sb.Length);
                    sb.Append(ext);
                    i++;
                    continue;
                }
            }

            var keyword = keywords.GetKeyword(b);
            if (keyword != null)
                sb.Append(keyword);
            else
                Petscii.Decode(b, charset, inQuotes, sb);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Lists a program held in memory from <paramref name="start"/> (normally <c>txttab</c>),
    /// following the line links until a link of 0. Stops early if a link doesn't move forward,
    /// which means the program in memory is damaged.
    /// </summary>
    public static IEnumerable<ListedLine> List(IMemoryReader memory, int start, KeywordTable keywords, PetsciiCase charset = PetsciiCase.Upper, int maxLines = 0x10000)
    {
        var address = start;
        for (var count = 0; count < maxLines; count++)
        {
            var link = memory.ReadWord(address);
            if (link == 0)
                yield break;

            var number = memory.ReadWord(address + 2);
            var body = new List<byte>();
            var p = address + 4;
            while (p < 0x10000 && memory.ReadByte(p) != 0)
                body.Add(memory.ReadByte(p++));

            var bytes = body.ToArray();
            yield return new ListedLine(number, address, bytes, DetokeniseLine(bytes, keywords, charset));

            if (link <= address)
                yield break;

            address = link;
        }
    }
}

/// <summary>
/// One line of a program in memory: its number, where it starts (the link) and its body.
/// </summary>
public sealed record ListedLine(int LineNumber, int Address, byte[] Body, string Text)
{
    public override string ToString() => $"{LineNumber} {Text}";
}
