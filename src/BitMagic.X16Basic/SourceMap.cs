namespace BitMagic.X16Basic;

/// <summary>
/// Maps between source lines, BASIC line numbers and where each line sits in memory.
/// </summary>
public sealed class SourceMap
{
    private readonly Dictionary<int, int> _byNumber = new();
    private readonly SortedList<int, int> _bySourceLine = new();
    private readonly SortedList<int, int> _byAddress = new();

    public BasicProgram Program { get; }
    public int LoadAddress { get; }
    public IReadOnlyList<int> LineAddresses { get; }

    /// <summary>The first byte after the program, where <c>vartab</c> starts.</summary>
    public int EndAddress { get; }

    internal SourceMap(BasicProgram program, int loadAddress)
    {
        Program = program;
        LoadAddress = loadAddress;

        var addresses = new int[program.Lines.Count];
        var address = loadAddress;
        for (var i = 0; i < program.Lines.Count; i++)
        {
            var line = program.Lines[i];
            addresses[i] = address;
            _byNumber[line.LineNumber] = i;
            _bySourceLine[line.SourceLine] = i;
            _byAddress[address] = i;
            address += line.Size;
        }
        LineAddresses = addresses;
        EndAddress = address + 2;
    }

    public BasicLine? FindLine(int lineNumber) =>
        _byNumber.TryGetValue(lineNumber, out var i) ? Program.Lines[i] : null;

    public int? FindAddress(int lineNumber) =>
        _byNumber.TryGetValue(lineNumber, out var i) ? LineAddresses[i] : null;

    /// <summary>
    /// The BASIC line on a source line, or failing that the next one after it, which is where a
    /// breakpoint on a blank or replaced line ends up.
    /// </summary>
    public BasicLine? FindBySourceLine(int sourceLine, bool orNext = true)
    {
        if (_bySourceLine.TryGetValue(sourceLine, out var exact))
            return Program.Lines[exact];

        if (!orNext)
            return null;

        BasicLine? best = null;
        foreach (var line in Program.Lines)
        {
            if (line.SourceLine > sourceLine && (best == null || line.SourceLine < best.SourceLine))
                best = line;
        }
        return best;
    }

    /// <summary>
    /// Where execution is from BASIC's <c>curlin</c> and <c>txtptr</c> as they are at <c>gone</c>:
    /// the statement that is about to run starts at <c>txtptr</c>+1. Null if <c>curlin</c> isn't a
    /// line in this program (eg direct mode, where its high byte is $FF).
    /// </summary>
    public BasicLocation? Locate(int curlin, int txtptr)
    {
        if (!_byNumber.TryGetValue(curlin, out var i))
            return null;

        var line = Program.Lines[i];
        var offset = txtptr + 1 - (LineAddresses[i] + 4);
        if (offset < 0 || offset > line.Body.Length)
            offset = 0;

        return new BasicLocation(line, line.StatementAt(offset), offset);
    }
}

/// <summary>
/// A statement in a program: the line, which statement on it, and the offset into the line's body.
/// </summary>
public sealed record BasicLocation(BasicLine Line, int Statement, int BodyOffset)
{
    public (int Start, int End) Columns => Line.StatementColumns(Statement);
}
