using Newtonsoft.Json;

namespace BitMagic.X16Basic.Debugger;

/// <summary>
/// The <c>settings</c> of the project's <c>debugMode</c>, or what the VS Code extension sends.
/// </summary>
public sealed class BasicSettings
{
    /// <summary>The BASIC source to tokenise and run. Relative to the project's base path.</summary>
    [JsonProperty("source")]
    public string? Source { get; set; }

    /// <summary><c>upper</c> (the default) or <c>mixed</c>: how letters in strings are encoded.</summary>
    [JsonProperty("charset")]
    public string Charset { get; set; } = "upper";

    /// <summary>
    /// <c>ready</c> (the default): when the program ends, stay at READY so it can be RUN again.
    /// <c>terminate</c>: end the debug session.
    /// </summary>
    [JsonProperty("onProgramEnd")]
    public string OnProgramEnd { get; set; } = "ready";

    /// <summary>Stop on the program's first statement.</summary>
    [JsonProperty("stopOnEntry")]
    public bool StopOnEntry { get; set; }

    /// <summary>RUN the program once it's loaded. If false it's left at READY for you to RUN.</summary>
    [JsonProperty("run")]
    public bool Run { get; set; } = true;

    /// <summary>
    /// Draw the whole screen when stopping, so output (eg a PRINT) shows straight away rather than when
    /// the beam next gets to it. BASIC doesn't race the beam, so nothing is lost.
    /// </summary>
    [JsonProperty("renderOnStop")]
    public bool RenderOnStop { get; set; } = true;

    /// <summary>The BASIC ROM's symbols, if they aren't in the project's symbols for ROM bank 4.</summary>
    [JsonProperty("symbols")]
    public string? Symbols { get; set; }

    internal PetsciiCase PetsciiCase => string.Equals(Charset, "mixed", StringComparison.OrdinalIgnoreCase) ? PetsciiCase.Mixed : PetsciiCase.Upper;
    internal bool TerminateAtEnd => string.Equals(OnProgramEnd, "terminate", StringComparison.OrdinalIgnoreCase);
}
