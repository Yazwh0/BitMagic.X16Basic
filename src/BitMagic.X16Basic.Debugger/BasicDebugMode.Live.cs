using Newtonsoft.Json.Linq;

namespace BitMagic.X16Basic.Debugger;

// The x16Mode request, for the extension's live view: what BASIC is doing, read straight from memory
// while the program runs, so values can be a statement out of step with each other.
public sealed partial class BasicDebugMode
{
    public JToken? CustomRequest(string command, JObject? arguments) => command switch
    {
        "live" => Live(arguments?.Value<int?>("maxVariables") ?? 200),
        "program" => ProgramInMemory(),
        _ => null,
    };

    /// <summary>
    /// The program in memory, eg after it's been changed at READY: as a .PRG (what SAVE would write, base64) and as text.
    /// </summary>
    private JObject ProgramInMemory()
    {
        var start = _memory.ReadWord(_symbols.Txttab);
        var end = _memory.ReadWord(_symbols.Vartab);
        if (end <= start + 2 || end - start > 0x9f00)
            return new JObject { ["error"] = "There's no BASIC program in memory." };

        var prg = BasicPrg.FromMemory(_memory.ReadBytes(start, end - start), start);
        try
        {
            var lines = BasicPrg.Read(prg, _keywords, _settings.PetsciiCase);
            return new JObject
            {
                ["prg"] = Convert.ToBase64String(prg),
                ["text"] = BasicPrg.ToText(lines),
                ["lines"] = lines.Count,
                ["charset"] = _settings.PetsciiCase == PetsciiCase.Mixed ? "mixed" : "upper",
            };
        }
        catch (BasicPrgException e)
        {
            return new JObject { ["error"] = "The program in memory is damaged: " + e.Message };
        }
    }

    private JObject Live(int maxVariables)
    {
        var curlin = Curlin;
        var inProgram = _programRunning && !IsDirect(curlin);

        var result = new JObject
        {
            ["running"] = inProgram,
            ["line"] = inProgram ? curlin : null,
        };

        if (inProgram && _map != null && _sourcePath != null)
        {
            // only from the source map if it's still the program in memory, which is cheap to tell
            // from the line's own address: the line number should be there
            var line = _map.FindLine(curlin);
            var address = _map.FindAddress(curlin);
            if (line != null && address != null && _memory.ReadWord(address.Value + 2) == curlin)
            {
                result["source"] = _sourcePath;
                result["sourceLine"] = line.SourceLine;
            }
        }

        var variables = new JArray();
        foreach (var v in BasicVariables.ReadVariables(_memory, _symbols).Take(maxVariables))
            variables.Add(new JObject { ["name"] = v.Name, ["value"] = v.Value.Display(_settings.PetsciiCase), ["type"] = TypeName(v.Type) });
        result["variables"] = variables;

        var arrays = new JArray();
        foreach (var a in BasicVariables.ReadArrays(_memory, _symbols).Take(maxVariables))
        {
            var preview = string.Join(", ", Enumerable.Range(0, Math.Min(a.Count, 8))
                .Select(i => BasicVariables.ReadElement(_memory, a.Type, a.DataAddress + i * a.ElementSize).Display(_settings.PetsciiCase)));
            arrays.Add(new JObject
            {
                ["name"] = $"{a.Name}({string.Join(",", a.Dimensions.Select(d => d - 1))})",
                ["type"] = TypeName(a.Type),
                ["value"] = a.Count > 8 ? preview + ", ..." : preview,
            });
        }
        result["arrays"] = arrays;

        var strend = _memory.ReadWord(_symbols.Strend);
        var fretop = _memory.ReadWord(_symbols.Fretop);
        result["freeMemory"] = fretop - strend;

        return result;
    }
}
