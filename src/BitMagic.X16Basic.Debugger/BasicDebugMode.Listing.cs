using Microsoft.VisualStudio.Shared.VSCodeDebugProtocol.Messages;

namespace BitMagic.X16Basic.Debugger;

// When the program in memory isn't the source file (there wasn't one, it was LOADed, or it was
// changed at READY), frames point at a listing of what's really there, served as a virtual document.
public sealed partial class BasicDebugMode
{
    private byte[]? _image;         // the program as we loaded it
    private Listing? _listing;
    private int _listingReference;  // the listing's sourceReference, kept for the session so its breakpoints stay put
    private bool _toldListing;

    private sealed record Listing(byte[] Image, SourceMap Map, string[] Lines, Source Source);

    /// <summary>The program being shown: its map from line numbers to source, the source, and its lines.</summary>
    private sealed record ShownProgram(SourceMap? Map, Source? Source, string[] Lines);

    private ShownProgram CurrentProgram()
    {
        var start = _memory.ReadWord(_symbols.Txttab);
        var end = _memory.ReadWord(_symbols.Vartab);
        if (end <= start || end - start > 0x9f00)
            return new ShownProgram(null, null, []);

        var image = _memory.ReadBytes(start, end - start);
        if (_map != null && _image != null && image.AsSpan().SequenceEqual(_image))
            return new ShownProgram(_map, _source, _sourceLines);

        if (_listing == null || !_listing.Image.AsSpan().SequenceEqual(image))
            _listing = MakeListing(start, image);

        if (_listing != null && !_toldListing)
        {
            _toldListing = true;
            _host.Logger.LogLine(_program == null
                ? "Showing a listing of the BASIC program in memory."
                : $"The program in memory isn't '{Path.GetFileName(_sourcePath)}' any more, so showing a listing of it instead.");
        }

        return _listing == null ? new ShownProgram(null, null, []) : new ShownProgram(_listing.Map, _listing.Source, _listing.Lines);
    }

    private Listing? MakeListing(int start, byte[] image)
    {
        var lines = Detokeniser.List(_memory, start, _keywords, _settings.PetsciiCase).Select(l => l.ToString()).ToArray();
        if (lines.Length == 0)
            return null;

        // tokenise the listing again for its columns; for a program LIST shows properly that's the same bytes
        var program = BasicTokeniser.Tokenise(string.Join('\n', lines), new BasicTokeniserOptions { Keywords = _keywords, Charset = _settings.PetsciiCase });

        if (_listingReference == 0)
            _listingReference = _host.NewId();

        var source = new Source
        {
            Name = "BASIC program (listing)",
            SourceReference = _listingReference,
            Origin = "the program in memory",
            AdapterData = "x16basic-listing",
        };

        return new Listing(image, program.CreateSourceMap(start), lines, source);
    }

    public SourceResponse? Source(SourceArguments arguments)
    {
        var reference = arguments.Source?.SourceReference ?? arguments.SourceReference;
        if (_listingReference == 0 || reference != _listingReference)
            return null;

        CurrentProgram(); // brings the listing up to date
        return new SourceResponse(string.Join("\n", _listing?.Lines ?? []))
        {
            MimeType = "text/x-x16basic",
        };
    }
}
