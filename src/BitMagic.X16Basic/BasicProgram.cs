namespace BitMagic.X16Basic;

/// <summary>
/// One tokenised line.
/// </summary>
/// <param name="LineNumber">BASIC's line number.</param>
/// <param name="Body">The tokenised bytes after the line number, without the closing 0.</param>
/// <param name="SourceLine">1 based line in the source file.</param>
/// <param name="SourceColumns">For each byte of <paramref name="Body"/>, the 0 based source column it came from.</param>
/// <param name="StatementStarts">Offsets into <paramref name="Body"/> where each statement starts.</param>
/// <param name="SourceLength">Length of the source line, for the end of the last statement.</param>
public sealed record BasicLine(int LineNumber, byte[] Body, int SourceLine, int[] SourceColumns, int[] StatementStarts, int SourceLength)
{
    /// <summary>Bytes the line takes in memory: link, number, body and the closing 0.</summary>
    public int Size => Body.Length + 5;

    /// <summary>
    /// Which statement a body offset is in.
    /// </summary>
    public int StatementAt(int bodyOffset)
    {
        var index = 0;
        for (var i = 1; i < StatementStarts.Length; i++)
        {
            if (StatementStarts[i] > bodyOffset)
                break;
            index = i;
        }
        return index;
    }

    /// <summary>
    /// The source columns (0 based, end exclusive) a statement covers, without the spaces
    /// around it or the <c>:</c> after it.
    /// </summary>
    public (int Start, int End) StatementColumns(int statement)
    {
        var start = StatementStarts[statement];
        var end = statement + 1 < StatementStarts.Length ? StatementStarts[statement + 1] - 1 : Body.Length; // -1 drops the ':'

        while (start < end && Body[start] == ' ')
            start++;
        while (end > start && Body[end - 1] == ' ')
            end--;

        // a keyword's byte points at where the keyword starts, so the end is where the next byte came from
        var endColumn = end < Body.Length ? SourceColumns[end] : SourceLength;
        var startColumn = start < Body.Length ? SourceColumns[start] : endColumn;

        return (startColumn, Math.Max(startColumn, endColumn));
    }
}

/// <summary>
/// A tokenised program, in line number order.
/// </summary>
public sealed class BasicProgram
{
    public const int DefaultLoadAddress = 0x0801;

    public IReadOnlyList<BasicLine> Lines { get; }
    public IReadOnlyList<SourceDiagnostic> Diagnostics { get; }
    public KeywordTable Keywords { get; }
    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    public BasicProgram(IReadOnlyList<BasicLine> lines, IReadOnlyList<SourceDiagnostic> diagnostics, KeywordTable keywords)
    {
        Lines = lines;
        Diagnostics = diagnostics;
        Keywords = keywords;
    }

    /// <summary>
    /// The program as it sits in memory at <paramref name="loadAddress"/>: each line's link,
    /// number, body and 0, then a 0 link to end. <c>vartab</c> goes straight after it.
    /// </summary>
    public byte[] ToMemoryImage(int loadAddress = DefaultLoadAddress)
    {
        var result = new List<byte>();
        var address = loadAddress;
        foreach (var line in Lines)
        {
            address += line.Size;
            result.Add((byte)address);
            result.Add((byte)(address >> 8));
            result.Add((byte)line.LineNumber);
            result.Add((byte)(line.LineNumber >> 8));
            result.AddRange(line.Body);
            result.Add(0);
        }
        result.Add(0);
        result.Add(0);

        if (loadAddress + result.Count > 0x9f00)
            throw new InvalidOperationException($"The program is {result.Count} bytes, too big to fit below $9F00 from ${loadAddress:X4}.");

        return result.ToArray();
    }

    /// <summary>
    /// A .PRG file: the load address, then <see cref="ToMemoryImage"/>.
    /// </summary>
    public byte[] ToPrg(int loadAddress = DefaultLoadAddress)
    {
        var image = ToMemoryImage(loadAddress);
        var result = new byte[image.Length + 2];
        result[0] = (byte)loadAddress;
        result[1] = (byte)(loadAddress >> 8);
        image.CopyTo(result, 2);
        return result;
    }

    public SourceMap CreateSourceMap(int loadAddress = DefaultLoadAddress) => new(this, loadAddress);
}
