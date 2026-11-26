using BitMagic.X16Emulator;

namespace BitMagic.X16Basic.Debugger;

/// <summary>
/// The CPU's view of the emulator's memory as BASIC uses it: main RAM, RAM bank 0 at $A000, and
/// whichever ROM bank is selected at $C000.
/// </summary>
internal sealed class EmulatorMemory(Emulator emulator) : IMemoryWriter
{
    public byte ReadByte(int address)
    {
        address &= 0xffff;
        return address switch
        {
            < 0xa000 => emulator.Memory[address],
            < 0xc000 => emulator.RamBank[address - 0xa000],
            _ => emulator.RomBank[(int)emulator.RomBankAct * 0x4000 + address - 0xc000]
        };
    }

    public void WriteByte(int address, byte value)
    {
        address &= 0xffff;
        if (address < 0xa000)
            emulator.Memory[address] = value;
        else if (address < 0xc000)
            emulator.RamBank[address - 0xa000] = value;
    }

    public void WriteBank(int bank, int address, ReadOnlySpan<byte> data)
    {
        var offset = bank * 0x2000 + address - 0xa000;
        data.CopyTo(emulator.RamBank[offset..]);
    }
}
