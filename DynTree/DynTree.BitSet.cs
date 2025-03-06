using System.Diagnostics;
using System.Numerics;

namespace DynTree
{
    public readonly partial struct DynTree
    {
        private BitSet AsBitSet
        {
            get
            {
                AssertType(DynTreeType.BitSet);
                return new BitSet(Payload);
            }
        }

        private readonly unsafe struct BitSet
        {
            private readonly uint* data;

            public BitSet(ulong payload)
            {
                data = (uint*)payload;
            }

            public ref uint Count
            {
                get => ref data[1];
            }

            public uint RefCount
            {
                get => data[0];
                set => data[0] = value;
            }

            public ulong* Bits
            {
                get => (ulong*)(data + 2);
            }

            public Span<ulong> BitsAsSpan
            {
                get => new Span<ulong>(data + 2, LONGS_IN_BITSET);
            }

            [Conditional("DEBUG")]
            private void AssertIndex(uint index)
            {
                if (index >= MIN_WIDTH)
                    throw new ArgumentOutOfRangeException(nameof(index), $"Bit {index} is invalid.");
            }

            public DynTree ToDynTree()
            {
                return new DynTree(DynTreeType.BitSet, (ulong)data);
            }

            private const int LONGS_IN_BITSET = (MIN_WIDTH + 63) / 64;
            public const int SIZE = LONGS_IN_BITSET * 8 + sizeof(uint) + sizeof(uint);

            public static bool TryAdd(DynTree original, IAllocator allocator, uint id, out DynTree result)
            {
                BitSet bitSet = original.AsBitSet;
                if (id < MIN_WIDTH)
                {
                    if (bitSet.Contains(id))
                    {
                        result = default;
                        return false;
                    }

                    if (original.IsImmutable)
                    {
                        BitSet newBitSet = Create(allocator, bitSet);
                        newBitSet.Set(id);
                        newBitSet.Count++;
                        result = newBitSet.ToDynTree();
                        return true;
                    }
                    bitSet.Set(id);
                    bitSet.Count++;
                    original.Acquire();
                    result = original;
                    return true;
                }

                DynTreeType newType = ChooseType((int)bitSet.Count + 1, id);
                if (newType == DynTreeType.Node)
                {
                    result = original.CreateParentAndAdd(allocator, id);
                }
                else
                {
                    // Use the generic method for creating a new DynTree by using an ID stream reader, which
                    // appends the new id.
                    result = DynTree.Create(allocator, new IdStreamReaderSequence(bitSet.GetStreamReader(), id, null));
                }
                return true;
            }

            public static bool TryRemove(DynTree original, IAllocator allocator, uint id, out DynTree result)
            {
                BitSet bitSet = original.AsBitSet;
                if (!bitSet.Contains(id))
                {
                    result = default;
                    return false;
                }
                if (original.IsImmutable)
                {
                    BitSet newBitSet = Create(allocator, bitSet);
                    newBitSet.Clear(id);
                    newBitSet.Count--;
                    result = newBitSet.ToDynTree();
                    return true;
                }
                bitSet.Clear(id);
                bitSet.Count--;
                original.Acquire();
                result = original;
                return true;
            }

            public static BitSet Create(IAllocator allocator, ReadOnlySpan<ushort> data, uint offset)
            {
                BitSet bitSet = Create(allocator, (uint)data.Length);
                for (int i = 0; i < data.Length; i++)
                    bitSet.Set(data[i] - offset);
                return bitSet;
            }

            public static BitSet Create(IAllocator allocator, ReadOnlySpan<uint> data, uint offset)
            {
                BitSet bitSet = Create(allocator, (uint)data.Length);
                for (int i = 0; i < data.Length; i++)
                    bitSet.Set(data[i] - offset);
                return bitSet;
            }

            private static bool IsBitSet(Span<ulong> target, uint bit)
            {
                uint index = (bit >> 6);
                return index < target.Length && (target[(int)index] & (1UL << ((int)bit & 63))) != 0;
            }

            public bool Contains(uint bit)
            {
                if (bit >= MIN_WIDTH)
                    return false;

                ulong word = Bits[bit >> 6];
                return (word & (1UL << ((int)bit & 63))) != 0;
            }

            public void Set(uint bit)
            {
                AssertIndex(bit);
                ref ulong targetWord = ref Bits[(bit >> 6)];
                targetWord |= (1UL << ((int)bit & 63));
            }

            public void Clear(uint bit)
            {
                AssertIndex(bit);
                ref ulong targetWord = ref Bits[(bit >> 6)];
                targetWord &= ~(1UL << ((int)bit & 63));
            }

            public void Release(IAllocator allocator)
            {
                if (Interlocked.Decrement(ref data[0]) != 0)
                    return;

                allocator.Free(data);
            }

            public static BitSet Create(IAllocator allocator, uint count)
            {
                BitSet result = new((ulong)allocator.Allocate(SIZE));
                result.BitsAsSpan.Clear();
                result.RefCount = 1;
                result.Count = count;
                return result;
            }

            public static BitSet Create(IAllocator allocator, BitSet copyFrom)
            {
                BitSet result = new((ulong)allocator.Allocate(SIZE));
                copyFrom.BitsAsSpan.CopyTo(result.BitsAsSpan);
                result.RefCount = 1;
                result.Count = copyFrom.Count;
                return result;
            }

            public IIdStreamReader GetStreamReader()
            {
                return new IdStreamReaderBitSet(Bits, LONGS_IN_BITSET);
            }

            private class IdStreamReaderBitSet : IIdStreamReader
            {
                private readonly ulong* bitSetData;
                private readonly int length;
                private uint currentBit;

                public IdStreamReaderBitSet(ulong* bitSetData, int length)
                {
                    this.bitSetData = bitSetData;
                    this.length = length;
                }

                public int Read(Span<uint> target)
                {
                    for (int i = 0; i < target.Length; i++)
                    {
                        if (!TryGetNextBit(out target[i]))
                            return i;
                    }
                    return target.Length;
                }

                private bool TryGetNextBit(out uint id)
                {
                    if (currentBit < 0)
                    {
                        id = default;
                        return false;
                    }

                    uint i = currentBit >> 6;
                    if (i >= LONGS_IN_BITSET)
                    {
                        id = default;
                        return false;
                    }
                    int subIndex = (int)(currentBit & 0x3f); // index within the word
                    ulong word = bitSetData[i] >> subIndex; // skip all the bits to the right of index

                    if (word != 0)
                    {
                        id = (uint)((i << 6) + subIndex + BitOperations.TrailingZeroCount(word));
                        currentBit = id + 1;
                        return true;
                    }

                    while (++i < LONGS_IN_BITSET)
                    {
                        word = bitSetData[i];
                        if (word != 0)
                        {
                            id = (uint)((i << 6) + BitOperations.TrailingZeroCount(word));
                            currentBit = id + 1;
                            return true;
                        }
                    }

                    id = default;
                    return false;
                }
            }
        }
    }
}
