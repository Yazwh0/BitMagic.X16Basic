namespace BitMagic.X16Basic;

/// <summary>
/// BASIC's error messages, by the number <c>error</c> is called with in X (basic/token2.s).
/// </summary>
public static class BasicErrors
{
    private static readonly string[] _messages =
    [
        "TOO MANY FILES", "FILE OPEN", "FILE NOT OPEN", "FILE NOT FOUND", "DEVICE NOT PRESENT",
        "NOT INPUT FILE", "NOT OUTPUT FILE", "MISSING FILE NAME", "ILLEGAL DEVICE NUMBER", "NEXT WITHOUT FOR",
        "SYNTAX", "RETURN WITHOUT GOSUB", "OUT OF DATA", "ILLEGAL QUANTITY", "OVERFLOW",
        "OUT OF MEMORY", "UNDEF'D STATEMENT", "BAD SUBSCRIPT", "REDIM'D ARRAY", "DIVISION BY ZERO",
        "ILLEGAL DIRECT", "TYPE MISMATCH", "STRING TOO LONG", "FILE DATA", "FORMULA TOO COMPLEX",
        "CAN'T CONTINUE", "UNDEF'D FUNCTION", "VERIFY", "LOAD", "BREAK",
        "NOT GRAPHICS MODE", "CHANNEL", "INSTRUMENT", "OCTAVE", "VOLUME",
        "DRUM", "DEPTH", "FREQUENCY", "PAN",
    ];

    public const int Break = 30;

    /// <summary>
    /// The message for an error number, as BASIC prints it before " ERROR".
    /// </summary>
    public static string GetMessage(int error) =>
        error >= 1 && error <= _messages.Length ? _messages[error - 1] : $"ERROR {error}";

    /// <summary>
    /// Reads the messages from the ROM's <c>errtab</c>, to match a particular ROM.
    /// </summary>
    public static string[] ReadFromRom(ReadOnlySpan<byte> basicBank, BasicRomSymbols symbols, int count = 39)
    {
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            var p = symbols.Errtab - 0xc000 + i * 2;
            var address = (basicBank[p] | (basicBank[p + 1] << 8)) - 0xc000;
            var sb = new System.Text.StringBuilder();
            while (address < basicBank.Length)
            {
                var b = basicBank[address++];
                if (b == 0)
                    break;
                Petscii.Decode((byte)(b & 0x7f), PetsciiCase.Upper, false, sb);
                if ((b & 0x80) != 0)
                    break;
            }
            result[i] = sb.ToString();
        }
        return result;
    }
}
