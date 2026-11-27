namespace BitMagic.X16Basic;

/// <summary>
/// .PRG files holding a BASIC program, as the X16's SAVE writes them: the load address (normally $0801),
/// then the program as it is in memory. Each line is a link to the next line, the line number and the
/// tokenised body ending in 0; a link of 0 ends the program.
/// </summary>
public static class BasicPrg
{
    /// <summary>
    /// Reads the program in a .PRG, checking it really is BASIC: the line links have to lead from one line to the
    /// next, each body ending just before the next line, up to a link of 0. Anything after that (eg the machine
    /// code after a <c>SYS</c> line) is ignored.
    /// </summary>
    /// <exception cref="BasicPrgException">It isn't a BASIC program.</exception>
    public static IReadOnlyList<ListedLine> Read(ReadOnlySpan<byte> prg, KeywordTable? keywords = null, PetsciiCase charset = PetsciiCase.Upper) =>
        Read(prg, out _, keywords, charset);

    /// <inheritdoc cref="Read(ReadOnlySpan{byte}, KeywordTable?, PetsciiCase)"/>
    public static IReadOnlyList<ListedLine> Read(ReadOnlySpan<byte> prg, out int loadAddress, KeywordTable? keywords = null, PetsciiCase charset = PetsciiCase.Upper)
    {
        if (prg.Length < 4)
            throw new BasicPrgException("It's too short to be a BASIC program.");

        loadAddress = prg[0] | (prg[1] << 8);
        var image = prg[2..];
        if (loadAddress + image.Length > 0x10000)
            throw new BasicPrgException($"It doesn't fit in memory from ${loadAddress:X4}, so it isn't a BASIC program.");

        Check(loadAddress, image);

        var memory = new ArrayMemory();
        image.CopyTo(memory.Data.AsSpan(loadAddress));
        return Detokeniser.List(memory, loadAddress, keywords ?? KeywordTable.Default, charset).ToList();
    }

    /// <summary>The program as text, a line each, as LIST shows it and the tokeniser reads it back.</summary>
    public static string ToText(IEnumerable<ListedLine> lines) => string.Concat(lines.Select(l => l + "\n"));

    /// <summary>A .PRG of a program as it is in memory (<c>txttab</c> to <c>vartab</c>), as SAVE would write it.</summary>
    public static byte[] FromMemory(ReadOnlySpan<byte> image, int loadAddress = BasicProgram.DefaultLoadAddress)
    {
        var result = new byte[image.Length + 2];
        result[0] = (byte)loadAddress;
        result[1] = (byte)(loadAddress >> 8);
        image.CopyTo(result.AsSpan(2));
        return result;
    }

    private static void Check(int loadAddress, ReadOnlySpan<byte> image)
    {
        const string NotBasic = "It isn't a BASIC program (perhaps it's machine code)";
        var offset = 0;
        var previous = -1;

        while (true)
        {
            if (offset + 2 > image.Length)
                throw new BasicPrgException($"{NotBasic}: it ends part way through a line.");

            var link = image[offset] | (image[offset + 1] << 8);
            if (link == 0)
                return;

            var next = link - loadAddress;
            if (next <= offset + 4 || next > image.Length)
                throw new BasicPrgException($"{NotBasic}: the line at ${loadAddress + offset:X4} links to ${link:X4}.");

            var number = image[offset + 2] | (image[offset + 3] << 8);
            if (number <= previous || number > 63999)
                throw new BasicPrgException($"{NotBasic}: line {number} comes after line {previous}.");

            // the body runs to a 0 just before the next line, with no 0 inside it
            var body = image[(offset + 4)..(next - 1)];
            if (image[next - 1] != 0 || body.Contains((byte)0))
                throw new BasicPrgException($"{NotBasic}: line {number} doesn't end where the next line starts.");

            previous = number;
            offset = next;
        }
    }
}

public sealed class BasicPrgException(string message) : Exception(message);
