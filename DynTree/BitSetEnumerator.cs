using System.Numerics;

namespace DynTree;

internal unsafe struct BitSetEnumerator
{
    private readonly ulong* bitSetData;
    private readonly int wordCount;
    private int currentWordIndex;
    private ulong currentWord;

    public BitSetEnumerator(ulong* bitSetData, int wordCount)
    {
        this.bitSetData = bitSetData;
        this.wordCount = wordCount;
        if (wordCount > 0)
            currentWord = bitSetData[0];
    }

    public static bool VisitSetBitIndices(ulong value, uint offset, Func<uint, bool> visitor)
    {
        while (value != 0)
        {
            // Compute its bit index using BitOperations
            int bitIndex = BitOperations.TrailingZeroCount(value);

            if (!visitor(offset + (uint)bitIndex))
                return false;

            // Clear the lowest set bit
            if (System.Runtime.Intrinsics.X86.Bmi1.X64.IsSupported)
                value = System.Runtime.Intrinsics.X86.Bmi1.X64.ResetLowestSetBit(value);
            else
                value ^= (1UL << bitIndex);
        }

        return true;
    }

    public bool TryGetNextBit(out uint id)
    {
        nextWord:

        if (currentWord != 0)
        {
            // Compute its bit index using BitOperations
            int bitIndex = BitOperations.TrailingZeroCount(currentWord);

            // Clear the lowest set bit
            if (System.Runtime.Intrinsics.X86.Bmi1.X64.IsSupported)
                currentWord = System.Runtime.Intrinsics.X86.Bmi1.X64.ResetLowestSetBit(currentWord);
            else
                currentWord ^= (1UL << bitIndex);
            id = (uint)(currentWordIndex << 6) + (uint)bitIndex;
            return true;
        }

        currentWordIndex++;
        if (currentWordIndex >= wordCount)
        {
            currentWordIndex = wordCount;
            id = default;
            return false;
        }

        currentWord = bitSetData[currentWordIndex];
        goto nextWord;
    }
}
