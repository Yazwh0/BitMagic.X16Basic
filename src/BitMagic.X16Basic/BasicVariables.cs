namespace BitMagic.X16Basic;

public enum BasicVariableType
{
    Float,
    Integer,
    String,
    Function
}

/// <summary>
/// A value as BASIC holds it: a number, a string (its PETSCII bytes and where they are), or a
/// DEF FN (where its definition and parameter are).
/// </summary>
public abstract record BasicValue
{
    /// <summary>The value written as BASIC would show it.</summary>
    public abstract string Display(PetsciiCase charset = PetsciiCase.Upper);
}

public sealed record BasicNumber(double Value) : BasicValue
{
    public override string Display(PetsciiCase charset = PetsciiCase.Upper) => BasicFloat.Format(Value);
}

public sealed record BasicInteger(short Value) : BasicValue
{
    public override string Display(PetsciiCase charset = PetsciiCase.Upper) => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record BasicString(byte[] Bytes, int Address) : BasicValue
{
    public override string Display(PetsciiCase charset = PetsciiCase.Upper)
    {
        var sb = new System.Text.StringBuilder(Bytes.Length + 2);
        sb.Append('"');
        foreach (var b in Bytes)
            Petscii.Decode(b, charset, true, sb);
        sb.Append('"');
        return sb.ToString();
    }
}

public sealed record BasicFunction(int DefinitionAddress, int ParameterAddress) : BasicValue
{
    public override string Display(PetsciiCase charset = PetsciiCase.Upper) => $"FN at ${DefinitionAddress:X4}";
}

/// <summary>
/// A simple (not array) variable from the table between <c>vartab</c> and <c>arytab</c>.
/// <see cref="Address"/> is the start of its 7 byte entry, its value is at <see cref="ValueAddress"/>.
/// </summary>
public sealed record BasicVariable(string Name, BasicVariableType Type, int Address, BasicValue Value)
{
    public int ValueAddress => Address + 2;
}

/// <summary>
/// An array from the table between <c>arytab</c> and <c>strend</c>.
/// <see cref="Dimensions"/> are the sizes of each subscript in the order they're written
/// (so <c>DIM A(2,3)</c> gives 3, 4).
/// </summary>
public sealed record BasicArray(string Name, BasicVariableType Type, int Address, int Size, int[] Dimensions, int DataAddress)
{
    public int ElementSize => BasicVariables.ElementSize(Type);
    public int Count => Dimensions.Aggregate(1, (a, b) => a * b);

    /// <summary>
    /// Where an element is. The first subscript changes fastest.
    /// </summary>
    public int ElementAddress(params int[] subscripts)
    {
        if (subscripts.Length != Dimensions.Length)
            throw new ArgumentException($"{Name} has {Dimensions.Length} dimension(s).");

        var index = 0;
        for (var i = Dimensions.Length - 1; i >= 0; i--)
        {
            if (subscripts[i] < 0 || subscripts[i] >= Dimensions[i])
                throw new ArgumentOutOfRangeException(nameof(subscripts), $"Subscript {i + 1} of {Name} must be 0 to {Dimensions[i] - 1}.");
            index = index * Dimensions[i] + subscripts[i];
        }
        return DataAddress + index * ElementSize;
    }

    public BasicValue ReadElement(IMemoryReader memory, params int[] subscripts) =>
        BasicVariables.ReadElement(memory, Type, ElementAddress(subscripts));

    /// <summary>
    /// The subscripts of the n'th element in memory order.
    /// </summary>
    public int[] SubscriptsOf(int index)
    {
        var result = new int[Dimensions.Length];
        for (var i = 0; i < Dimensions.Length; i++)
        {
            result[i] = index % Dimensions[i];
            index /= Dimensions[i];
        }
        return result;
    }
}

/// <summary>
/// Reads BASIC's variable and array tables.
/// </summary>
public static class BasicVariables
{
    public const int EntrySize = 7;

    public static int ElementSize(BasicVariableType type) => type switch
    {
        BasicVariableType.Float => 5,
        BasicVariableType.Integer => 2,
        BasicVariableType.String => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>
    /// The type is held in the top bits of the two name bytes.
    /// </summary>
    public static BasicVariableType TypeOf(byte name0, byte name1) => ((name0 & 0x80) != 0, (name1 & 0x80) != 0) switch
    {
        (false, false) => BasicVariableType.Float,
        (true, true) => BasicVariableType.Integer,
        (false, true) => BasicVariableType.String,
        (true, false) => BasicVariableType.Function,
    };

    public static string NameOf(byte name0, byte name1)
    {
        var sb = new System.Text.StringBuilder(5);
        var type = TypeOf(name0, name1);
        if (type == BasicVariableType.Function)
            sb.Append("FN");

        Petscii.Decode((byte)(name0 & 0x7f), PetsciiCase.Upper, false, sb);
        if ((name1 & 0x7f) != 0)
            Petscii.Decode((byte)(name1 & 0x7f), PetsciiCase.Upper, false, sb);

        sb.Append(type switch
        {
            BasicVariableType.Integer => "%",
            BasicVariableType.String => "$",
            _ => ""
        });
        return sb.ToString();
    }

    /// <summary>
    /// Encodes a name as the two bytes BASIC stores, including the type bits. Only the first two
    /// characters count, as in BASIC.
    /// </summary>
    public static (byte Name0, byte Name1) EncodeName(string name)
    {
        var type = BasicVariableType.Float;
        name = name.Trim().ToUpperInvariant();
        if (name.StartsWith("FN"))
        {
            type = BasicVariableType.Function;
            name = name[2..];
        }
        if (name.EndsWith('%'))
        {
            type = BasicVariableType.Integer;
            name = name[..^1];
        }
        else if (name.EndsWith('$'))
        {
            type = BasicVariableType.String;
            name = name[..^1];
        }

        if (name.Length == 0 || name[0] < 'A' || name[0] > 'Z' || name.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException($"'{name}' isn't a valid variable name.");

        var n0 = (byte)name[0];
        var n1 = name.Length > 1 ? (byte)name[1] : (byte)0;
        return type switch
        {
            BasicVariableType.Integer => ((byte)(n0 | 0x80), (byte)(n1 | 0x80)),
            BasicVariableType.String => (n0, (byte)(n1 | 0x80)),
            BasicVariableType.Function => ((byte)(n0 | 0x80), n1),
            _ => (n0, n1),
        };
    }

    public static BasicValue ReadElement(IMemoryReader memory, BasicVariableType type, int address) => type switch
    {
        BasicVariableType.Float => new BasicNumber(BasicFloat.Decode(memory, address)),
        // integers are high byte first
        BasicVariableType.Integer => new BasicInteger((short)((memory.ReadByte(address) << 8) | memory.ReadByte(address + 1))),
        BasicVariableType.String => ReadString(memory, address),
        BasicVariableType.Function => new BasicFunction(memory.ReadWord(address), memory.ReadWord(address + 2)),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>
    /// Reads a string descriptor (length, then address) and the string it points at.
    /// </summary>
    public static BasicString ReadString(IMemoryReader memory, int descriptor)
    {
        var length = memory.ReadByte(descriptor);
        var address = memory.ReadWord(descriptor + 1);
        return new BasicString(memory.ReadBytes(address, length), address);
    }

    /// <summary>
    /// The simple variables, from <c>vartab</c> to <c>arytab</c>, in the order BASIC created them.
    /// </summary>
    public static IEnumerable<BasicVariable> ReadVariables(IMemoryReader memory, BasicRomSymbols symbols)
    {
        var start = memory.ReadWord(symbols.Vartab);
        var end = memory.ReadWord(symbols.Arytab);
        return ReadVariables(memory, start, end);
    }

    public static IEnumerable<BasicVariable> ReadVariables(IMemoryReader memory, int start, int end)
    {
        if (end < start || end - start > 0x10000)
            yield break;

        for (var address = start; address + EntrySize <= end; address += EntrySize)
        {
            var n0 = memory.ReadByte(address);
            var n1 = memory.ReadByte(address + 1);
            var type = TypeOf(n0, n1);
            yield return new BasicVariable(NameOf(n0, n1), type, address, ReadElement(memory, type, address + 2));
        }
    }

    /// <summary>
    /// The arrays, from <c>arytab</c> to <c>strend</c>. Stops if an array's size doesn't make sense.
    /// </summary>
    public static IEnumerable<BasicArray> ReadArrays(IMemoryReader memory, BasicRomSymbols symbols)
    {
        var start = memory.ReadWord(symbols.Arytab);
        var end = memory.ReadWord(symbols.Strend);
        return ReadArrays(memory, start, end);
    }

    public static IEnumerable<BasicArray> ReadArrays(IMemoryReader memory, int start, int end)
    {
        var address = start;
        while (address + 5 <= end)
        {
            var n0 = memory.ReadByte(address);
            var n1 = memory.ReadByte(address + 1);
            var size = memory.ReadWord(address + 2);
            var count = memory.ReadByte(address + 4);
            var type = TypeOf(n0, n1);

            if (size < 5 + count * 2 || address + size > end || count == 0 || type == BasicVariableType.Function)
                yield break;

            // the sizes are high byte first, and the last subscript's comes first
            var dimensions = new int[count];
            for (var i = 0; i < count; i++)
            {
                var p = address + 5 + i * 2;
                dimensions[count - 1 - i] = (memory.ReadByte(p) << 8) | memory.ReadByte(p + 1);
            }

            yield return new BasicArray(NameOf(n0, n1), type, address, size, dimensions, address + 5 + count * 2);
            address += size;
        }
    }

    /// <summary>
    /// Finds a simple variable by name (only the first two characters and the type count).
    /// </summary>
    public static BasicVariable? FindVariable(IMemoryReader memory, BasicRomSymbols symbols, string name)
    {
        var (n0, n1) = EncodeName(name);
        var start = memory.ReadWord(symbols.Vartab);
        var end = memory.ReadWord(symbols.Arytab);
        for (var address = start; address + EntrySize <= end; address += EntrySize)
        {
            if (memory.ReadByte(address) == n0 && memory.ReadByte(address + 1) == n1)
                return ReadVariables(memory, address, address + EntrySize).First();
        }
        return null;
    }

    public static BasicArray? FindArray(IMemoryReader memory, BasicRomSymbols symbols, string name)
    {
        var (n0, n1) = EncodeName(name);
        foreach (var array in ReadArrays(memory, symbols))
        {
            if (memory.ReadByte(array.Address) == n0 && memory.ReadByte(array.Address + 1) == n1)
                return array;
        }
        return null;
    }

    /// <summary>
    /// Writes a number into a float or integer value (a variable's or an array element's).
    /// </summary>
    public static void WriteNumber(IMemoryWriter memory, BasicVariableType type, int address, double value)
    {
        switch (type)
        {
            case BasicVariableType.Float:
                memory.WriteBytes(address, BasicFloat.Encode(value));
                break;
            case BasicVariableType.Integer:
                var truncated = Math.Floor(value);
                if (truncated < short.MinValue || truncated > short.MaxValue)
                    throw new OverflowException($"{value} doesn't fit in an integer variable.");
                var i = (short)truncated;
                memory.WriteByte(address, (byte)(i >> 8));
                memory.WriteByte(address + 1, (byte)i);
                break;
            default:
                throw new ArgumentException($"A {type} isn't a number.");
        }
    }
}
