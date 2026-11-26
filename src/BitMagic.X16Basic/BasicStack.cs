namespace BitMagic.X16Basic;

/// <summary>
/// A frame BASIC keeps on the 6502 stack for a GOSUB or FOR. <see cref="LineNumber"/> and
/// <see cref="TextPointer"/> are where execution goes back to: after the GOSUB for RETURN, and
/// after the FOR statement for NEXT.
/// </summary>
public abstract record BasicStackFrame(int StackAddress, int LineNumber, int TextPointer);

/// <summary>A GOSUB: token $8D, <c>curlin</c>, <c>txtptr</c> (5 bytes).</summary>
public sealed record GosubFrame(int StackAddress, int LineNumber, int TextPointer) : BasicStackFrame(StackAddress, LineNumber, TextPointer);

/// <summary>
/// A FOR loop (18 bytes): token $81, the loop variable's address, STEP (unpacked, then its sign),
/// the TO limit, then <c>curlin</c> and <c>txtptr</c>.
/// </summary>
public sealed record ForFrame(int StackAddress, int LineNumber, int TextPointer, int VariableAddress, double Step, double Limit) : BasicStackFrame(StackAddress, LineNumber, TextPointer);

public static class BasicStack
{
    public const int GosubFrameSize = 5;
    public const int ForFrameSize = 18;

    /// <summary>
    /// Reads BASIC's frames, newest first, as they are when BASIC is between statements (at
    /// <c>newstt</c> or <c>gone</c>). <paramref name="stackPointer"/> is the CPU's S register.
    ///
    /// FOR pops the return address it was called with, so its frames sit straight on top of the
    /// next; GOSUB keeps it (RETURN goes back through it), so each GOSUB frame is followed by the
    /// return address <c>jsr gone3</c> pushed. Reading stops at anything else.
    /// </summary>
    public static IReadOnlyList<BasicStackFrame> Read(IMemoryReader memory, int stackPointer, BasicRomSymbols symbols)
    {
        var frames = new List<BasicStackFrame>();
        var p = 0x100 + ((stackPointer + 1) & 0xff);
        if (stackPointer >= 0xff)
            return frames;

        var goneReturn = symbols.GoneReturnAddress;

        while (p < 0x200)
        {
            var token = memory.ReadByte(p);
            if (token == KeywordTable.GosubToken && p + GosubFrameSize + 2 <= 0x200)
            {
                frames.Add(new GosubFrame(p, memory.ReadWord(p + 1), memory.ReadWord(p + 3)));
                p += GosubFrameSize;

                if (memory.ReadWord(p) != goneReturn)
                    break;
                p += 2;
            }
            else if (token == KeywordTable.ForToken && p + ForFrameSize <= 0x200)
            {
                var step = BasicFloat.DecodeUnpacked(memory.ReadBytes(p + 3, 5), memory.ReadByte(p + 8));
                var limit = BasicFloat.Decode(memory.ReadBytes(p + 9, 5));
                frames.Add(new ForFrame(p, memory.ReadWord(p + 14), memory.ReadWord(p + 16), memory.ReadWord(p + 1), step, limit));
                p += ForFrameSize;
            }
            else
            {
                break;
            }
        }

        return frames;
    }
}
