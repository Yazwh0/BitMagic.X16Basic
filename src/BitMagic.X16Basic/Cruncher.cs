namespace BitMagic.X16Basic;

/// <summary>
/// Tokenises the text of one line, a port of the ROM's <c>ncrnch</c> (basic/code2.s) kept close
/// to the original so it gives the same bytes, quirks included:
/// <list type="bullet">
/// <item>keywords are found anywhere outside quotes, even inside names (<c>FORM</c> is <c>FOR M</c>);</item>
/// <item>the first keyword in list order wins, so <c>INPUT#</c> must come before <c>INPUT</c>;</item>
/// <item>digits and <c>: ;</c> are never the start of a keyword;</item>
/// <item>after <c>DATA</c>, text is copied up to the next <c>:</c>; after <c>REM</c>, to the end of the line;</item>
/// <item>a shifted letter ends a keyword early, which is how abbreviations such as <c>pO</c> work;</item>
/// <item>bytes from $80 up are dropped outside quotes, apart from π.</item>
/// </list>
/// </summary>
public static class Cruncher
{
    private sealed class Output(int capacity)
    {
        public readonly List<byte> Bytes = new(capacity);
        public readonly List<int> Sources = new(capacity);

        public void Add(byte b, int source)
        {
            Bytes.Add(b);
            Sources.Add(source);
        }
    }

    /// <summary>
    /// Tokenises <paramref name="input"/>, the line's text after its number. Returns the bytes
    /// (without the 0 that ends the line) and, for each byte, the index in
    /// <paramref name="input"/> it came from.
    /// </summary>
    public static (byte[] Bytes, int[] Sources) Crunch(ReadOnlySpan<byte> input, KeywordTable keywords)
    {
        // the ROM reads up to a 0, so give it one
        var buf = new byte[input.Length + 1];
        input.CopyTo(buf);

        var output = new Output(input.Length);
        var x = 0;
        byte dores = 0;

        while (true)
        {
            // kloop
            var c = buf[x];
            byte stored;
            int storedFrom = x;

            if (c >= 0x80)
            {
                if (c != Petscii.Pi)
                {
                    x++; // dropped
                    continue;
                }
                stored = c;
                x++;
            }
            else if (c == ' ')
            {
                stored = c;
                x++;
            }
            else if (c == '"')
            {
                // strng: copy to the closing quote or the end of the line
                output.Add(c, x);
                x++;
                while (buf[x] != 0 && buf[x] != '"')
                {
                    output.Add(buf[x], x);
                    x++;
                }
                if (buf[x] == 0)
                    break;
                stored = buf[x]; // the closing quote goes through the checks below like any other byte
                x++;
            }
            else if ((dores & 0x40) != 0)
            {
                stored = c;
                x++;
            }
            else if (c == '?')
            {
                stored = KeywordTable.PrintToken;
                x++;
            }
            else if (c >= '0' && c < '<')
            {
                stored = c;
                x++;
            }
            else
            {
                // mustcr
                var (length, token) = Match(buf, x, keywords.Main);
                if (length > 0)
                {
                    stored = (byte)(0x80 | token);
                    x += length;
                }
                else
                {
                    var count = 0;
                    (length, token) = Match(buf, x, keywords.Extended);
                    if (length == 0)
                    {
                        count = token;
                        (length, token) = Match(buf, x, keywords.Extended2);
                        token += count;
                    }

                    if (length > 0)
                    {
                        // resfnd: extended tokens skip the checks below and go straight back to kloop
                        output.Add(KeywordTable.ExtendedToken, x);
                        output.Add(keywords.ExtendedTokenFor(token), x);
                        x += length;
                        continue;
                    }

                    stored = c;
                    x++;
                }
            }

            // crdone0
            if (stored == 0)
                break;

            output.Add(stored, storedFrom);

            if (stored == ':')
                dores = 0;
            else if (stored == KeywordTable.DataToken)
                dores = (byte)(KeywordTable.DataToken - ':');

            if (stored == KeywordTable.RemToken)
            {
                // copy the rest of the line as it is
                while (buf[x] != 0)
                {
                    output.Add(buf[x], x);
                    x++;
                }
                break;
            }
        }

        return (output.Bytes.ToArray(), output.Sources.ToArray());
    }

    /// <summary>
    /// Looks for a keyword at <paramref name="x"/>, walking <paramref name="list"/> as
    /// <c>rescon</c>/<c>nthis</c> do. Returns how many bytes matched (0 for none) and the
    /// keyword's index, or when nothing matched how many keywords the list holds.
    /// </summary>
    private static (int Length, int Index) Match(byte[] buf, int x, byte[] list)
    {
        var y = 0;
        var count = 0;

        while (true)
        {
            var xi = x;
            while (xi < buf.Length && y < list.Length)
            {
                var ch = buf[xi];
                if (ch < 0x80 && ch >= 0x61)
                    ch -= 0x20;

                var d = (byte)(ch - list[y]);
                if (d == 0)
                {
                    // matched this byte; like the ROM this carries on into the next entry if
                    // a shifted character matched an entry's last byte
                    xi++;
                    y++;
                    continue;
                }

                if (d == 0x80)
                    return (xi - x + 1, count);

                break;
            }

            // nthis: skip to the start of the next keyword
            count++;
            do
            {
                y++;
            } while (y < list.Length && (list[y - 1] & 0x80) == 0);

            if (y >= list.Length || list[y] == 0)
                return (0, count);
        }
    }
}
