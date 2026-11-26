namespace BitMagic.X16Basic;

/// <summary>
/// The CPU's view of memory as BASIC sees it: main RAM, with RAM bank 0 at $A000.
/// Implemented over the emulator by the debugger, and over arrays in tests.
/// </summary>
public interface IMemoryReader
{
    byte ReadByte(int address);
}

public interface IMemoryWriter : IMemoryReader
{
    void WriteByte(int address, byte value);
}

public static class MemoryReaderExtensions
{
    public static int ReadWord(this IMemoryReader memory, int address) =>
        memory.ReadByte(address) | (memory.ReadByte(address + 1) << 8);

    public static byte[] ReadBytes(this IMemoryReader memory, int address, int count)
    {
        var result = new byte[count];
        for (var i = 0; i < count; i++)
            result[i] = memory.ReadByte(address + i);
        return result;
    }

    public static void WriteWord(this IMemoryWriter memory, int address, int value)
    {
        memory.WriteByte(address, (byte)value);
        memory.WriteByte(address + 1, (byte)(value >> 8));
    }

    public static void WriteBytes(this IMemoryWriter memory, int address, ReadOnlySpan<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
            memory.WriteByte(address + i, data[i]);
    }
}

/// <summary>
/// A flat 64KB address space.
/// </summary>
public sealed class ArrayMemory(byte[]? data = null) : IMemoryWriter
{
    public byte[] Data { get; } = data ?? new byte[0x10000];

    public byte ReadByte(int address) => Data[address & 0xffff];
    public void WriteByte(int address, byte value) => Data[address & 0xffff] = value;
}
