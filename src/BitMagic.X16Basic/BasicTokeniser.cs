namespace BitMagic.X16Basic;

public sealed record BasicTokeniserOptions
{
    public KeywordTable Keywords { get; init; } = KeywordTable.Default;
    public PetsciiCase Charset { get; init; } = PetsciiCase.Upper;

    /// <summary>
    /// The longest line BASIC's input buffer takes (<c>buflen</c> - 1). Longer lines still
    /// tokenise and run, but can't be typed in or EXEC'd, so they get a warning.
    /// </summary>
    public int MaxInputLength { get; init; } = 88;
}

/// <summary>
/// Turns source text with line numbers into a <see cref="BasicProgram"/>, giving the same
/// bytes as typing each line in at the READY prompt.
/// </summary>
public static class BasicTokeniser
{
    public const int MaxLineNumber = 63999;

    public static BasicProgram Tokenise(string source, BasicTokeniserOptions? options = null)
    {
        options ??= new BasicTokeniserOptions();
        var diagnostics = new List<SourceDiagnostic>();
        var lines = new SortedDictionary<int, BasicLine>();

        var sourceLines = source.Split('\n');
        for (var i = 0; i < sourceLines.Length; i++)
        {
            var text = sourceLines[i].TrimEnd('\r');
            var sourceLine = i + 1;

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var line = TokeniseLine(text, sourceLine, options, diagnostics);
            if (line == null)
                continue;

            if (lines.TryGetValue(line.LineNumber, out var existing))
                diagnostics.Add(new SourceDiagnostic(sourceLine, 0, $"Line {line.LineNumber} is also on source line {existing.SourceLine}; this one replaces it.", false));

            if (line.Body.Length == 0)
            {
                // typing just a number deletes that line
                lines.Remove(line.LineNumber);
                diagnostics.Add(new SourceDiagnostic(sourceLine, 0, $"Line {line.LineNumber} is empty, so it isn't stored.", false));
                continue;
            }

            lines[line.LineNumber] = line;
        }

        var ordered = lines.Values.ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].SourceLine < ordered[i - 1].SourceLine)
                diagnostics.Add(new SourceDiagnostic(ordered[i].SourceLine, 0,
                    $"Line {ordered[i].LineNumber} is after line {ordered[i - 1].LineNumber} in the source, but BASIC stores it before.", false));
        }

        return new BasicProgram(ordered, diagnostics, options.Keywords);
    }

    /// <summary>
    /// Tokenises one numbered line. Returns null, and adds to <paramref name="diagnostics"/>,
    /// if it has no valid line number.
    /// </summary>
    public static BasicLine? TokeniseLine(string text, int sourceLine, BasicTokeniserOptions? options = null, List<SourceDiagnostic>? diagnostics = null)
    {
        options ??= new BasicTokeniserOptions();

        var (encoded, columns) = Petscii.EncodeLine(text, options.Charset, sourceLine, diagnostics);

        if (encoded.Length > options.MaxInputLength)
            diagnostics?.Add(new SourceDiagnostic(sourceLine, 0,
                $"Line is {encoded.Length} characters; BASIC can only take {options.MaxInputLength} when typed in or EXEC'd.", false));

        // linget: digits, with spaces skipped by chrget, before and between them
        var p = 0;
        while (p < encoded.Length && encoded[p] == ' ')
            p++;

        if (p == encoded.Length || encoded[p] < '0' || encoded[p] > '9')
        {
            diagnostics?.Add(new SourceDiagnostic(sourceLine, p < columns.Length ? columns[p] : 0, "Line has no line number."));
            return null;
        }

        var number = 0;
        var numberColumn = columns[p];
        while (p < encoded.Length && (encoded[p] == ' ' || (encoded[p] >= '0' && encoded[p] <= '9')))
        {
            if (encoded[p] != ' ')
            {
                number = number * 10 + encoded[p] - '0';
                if (number > MaxLineNumber)
                {
                    diagnostics?.Add(new SourceDiagnostic(sourceLine, numberColumn, $"Line number is over {MaxLineNumber}."));
                    return null;
                }
            }
            p++;
        }

        var (body, sources) = Cruncher.Crunch(encoded.AsSpan(p), options.Keywords);

        var bodyColumns = new int[body.Length];
        for (var i = 0; i < body.Length; i++)
            bodyColumns[i] = columns[p + sources[i]];

        return new BasicLine(number, body, sourceLine, bodyColumns, FindStatements(body), text.Length);
    }

    /// <summary>
    /// Where each statement in a line body starts: the start, and after each <c>:</c> outside
    /// quotes, up to a REM.
    /// </summary>
    public static int[] FindStatements(ReadOnlySpan<byte> body)
    {
        var starts = new List<int> { 0 };
        var inQuotes = false;
        for (var i = 0; i < body.Length; i++)
        {
            var b = body[i];
            if (b == '"')
                inQuotes = !inQuotes;
            else if (!inQuotes && b == KeywordTable.ExtendedToken)
                i++; // the byte after $CE can look like REM ($CE $8F is RESET)
            else if (!inQuotes && b == KeywordTable.RemToken)
                break;
            else if (!inQuotes && b == ':')
                starts.Add(i + 1);
        }
        return starts.ToArray();
    }
}
