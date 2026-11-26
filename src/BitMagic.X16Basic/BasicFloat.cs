namespace BitMagic.X16Basic;

/// <summary>
/// BASIC's 5 byte floating point format (MFLPT): an exponent biased by 128 (0 means the value
/// is 0), then a 32 bit mantissa, high byte first, whose top bit is replaced by the sign.
/// </summary>
public static class BasicFloat
{
    public const int Size = 5;

    /// <summary>The largest value BASIC can hold, about 1.70141183E+38.</summary>
    public static readonly double MaxValue = Decode([0xff, 0x7f, 0xff, 0xff, 0xff]);

    public static double Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes[0] == 0)
            return 0;

        var mantissa = ((uint)(bytes[1] | 0x80) << 24) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 8) | bytes[4];
        var value = Math.ScaleB(mantissa, bytes[0] - 128 - 32);
        return (bytes[1] & 0x80) != 0 ? -value : value;
    }

    /// <summary>
    /// Decodes a value held unpacked, as FAC is and as a FOR loop's STEP is on the stack:
    /// the mantissa's top bit is kept, and the sign is a separate byte (negative if bit 7 is set).
    /// </summary>
    public static double DecodeUnpacked(ReadOnlySpan<byte> bytes, byte sign)
    {
        if (bytes[0] == 0)
            return 0;

        var mantissa = ((uint)bytes[1] << 24) | ((uint)bytes[2] << 16) | ((uint)bytes[3] << 8) | bytes[4];
        var value = Math.ScaleB(mantissa, bytes[0] - 128 - 32);
        return (sign & 0x80) != 0 ? -value : value;
    }

    public static double Decode(IMemoryReader memory, int address) => Decode(memory.ReadBytes(address, Size));

    /// <summary>
    /// Encodes a value, rounding to the nearest. Values too small to hold become 0.
    /// </summary>
    /// <exception cref="OverflowException">The value is too big, or isn't a number.</exception>
    public static byte[] Encode(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new OverflowException($"{value} can't be held as a BASIC number.");

        if (value == 0)
            return new byte[Size];

        var negative = value < 0;
        var magnitude = Math.Abs(value);

        // magnitude = fraction * 2^exponent, with 0.5 <= fraction < 1
        var exponent = Math.ILogB(magnitude) + 1;
        var mantissa = Math.Round(Math.ScaleB(magnitude, 32 - exponent), MidpointRounding.AwayFromZero);
        if (mantissa >= 4294967296.0)
        {
            mantissa /= 2;
            exponent++;
        }

        var biased = exponent + 128;
        if (biased > 255)
            throw new OverflowException($"{value} is too big for a BASIC number.");
        if (biased < 1)
            return new byte[Size];

        var m = (uint)mantissa;
        return
        [
            (byte)biased,
            (byte)(((m >> 24) & 0x7f) | (negative ? 0x80u : 0)),
            (byte)(m >> 16),
            (byte)(m >> 8),
            (byte)m,
        ];
    }

    /// <summary>
    /// Formats a value roughly as PRINT does: up to 9 significant digits, and E notation for
    /// very large or very small values.
    /// </summary>
    public static string Format(double value)
    {
        if (value == 0)
            return "0";

        var abs = Math.Abs(value);
        if (abs >= 1e-2 && abs < 1e9)
        {
            // keep to 9 significant digits like BASIC
            var r = double.Parse(value.ToString("G9", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
            var s = r.ToString("0.#########", System.Globalization.CultureInfo.InvariantCulture);
            return s.StartsWith("0.") ? s[1..] : s.StartsWith("-0.") ? "-" + s[2..] : s;
        }

        return value.ToString("0.########E+00", System.Globalization.CultureInfo.InvariantCulture);
    }
}
