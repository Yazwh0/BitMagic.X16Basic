using BitMagic.X16Basic;

namespace BitMagic.X16Basic.Tool;

// x16basic tokenise game.bas [-o GAME.PRG] [--charset upper|mixed]
// x16basic detokenise GAME.PRG [-o game.bas] [--charset upper|mixed]
// -o - writes to standard output. Problems go to standard error, and the exit code is 1.
internal static class Program
{
    private const string Usage = """
        x16basic: Commander X16 BASIC to and from .PRG files.

          x16basic tokenise <file.bas> [-o <file.prg>] [--charset upper|mixed]
              Makes a .PRG the X16 can LOAD, byte for byte what SAVE writes.

          x16basic detokenise <file.prg> [-o <file.bas>] [--charset upper|mixed]
              Turns a .PRG back into BASIC, as LIST shows it.

        -o defaults to the input file with the other extension; -o - writes to standard output.
        --charset is the character set strings are written for: upper (the default) or mixed.
        """;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 2 : 0;
            }

            var command = args[0].ToLowerInvariant();
            string? input = null, output = null;
            var charset = PetsciiCase.Upper;

            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-o" or "--output":
                        output = Next(args, ref i);
                        break;
                    case "--charset":
                        charset = Next(args, ref i).ToLowerInvariant() switch
                        {
                            "upper" => PetsciiCase.Upper,
                            "mixed" => PetsciiCase.Mixed,
                            var other => throw new UsageException($"Unknown charset '{other}': use upper or mixed."),
                        };
                        break;
                    case var a when a.StartsWith('-') && a != "-":
                        throw new UsageException($"Unknown option '{a}'.");
                    default:
                        input = input == null ? args[i] : throw new UsageException("Only one input file, please.");
                        break;
                }
            }

            if (input == null)
                throw new UsageException("Which file?");

            return command switch
            {
                "tokenise" or "tokenize" => Tokenise(input, output ?? Path.ChangeExtension(input, ".prg"), charset),
                "detokenise" or "detokenize" => Detokenise(input, output ?? Path.ChangeExtension(input, ".bas"), charset),
                _ => throw new UsageException($"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 2;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BasicPrgException or InvalidOperationException)
        {
            Console.Error.WriteLine($"x16basic: {e.Message}");
            return 1;
        }
    }

    private static int Tokenise(string input, string output, PetsciiCase charset)
    {
        var program = BasicTokeniser.Tokenise(File.ReadAllText(input), new BasicTokeniserOptions { Charset = charset });

        // the way compilers report, so editors can find the line
        foreach (var d in program.Diagnostics)
            Console.Error.WriteLine($"{input}({d.SourceLine},{d.Column + 1}): {(d.IsError ? "error" : "warning")}: {d.Message}");

        if (program.HasErrors)
            return 1;

        var prg = program.ToPrg();
        if (output == "-")
        {
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(prg);
        }
        else
        {
            File.WriteAllBytes(output, prg);
            Console.Error.WriteLine($"{output}: {Lines(program.Lines.Count)}, {prg.Length} bytes.");
        }

        return 0;
    }

    private static int Detokenise(string input, string output, PetsciiCase charset)
    {
        var lines = BasicPrg.Read(File.ReadAllBytes(input), out var loadAddress, charset: charset);
        if (loadAddress != BasicProgram.DefaultLoadAddress)
            Console.Error.WriteLine($"{input}: loads at ${loadAddress:X4}, not ${BasicProgram.DefaultLoadAddress:X4} like a BASIC program saved on the X16.");

        var text = BasicPrg.ToText(lines);
        if (output == "-")
            Console.Out.Write(text);
        else
        {
            File.WriteAllText(output, text);
            Console.Error.WriteLine($"{output}: {Lines(lines.Count)}.");
        }

        return 0;
    }

    private static string Lines(int count) => count == 1 ? "1 line" : $"{count} lines";

    private static string Next(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new UsageException($"{args[i - 1]} needs a value.");

    private sealed class UsageException(string message) : Exception(message);
}
