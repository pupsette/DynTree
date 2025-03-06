using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DynTree
{
    public readonly partial struct DynTree
    {
        private Array32 AsArray32
        {
            get
            {
                AssertType(DynTreeType.Array32);
                return new Array32(Payload);
            }
        }

        private readonly unsafe struct Array32
        {
            private readonly uint* data;

            public Array32(ulong payload)
            {
                data = (uint*)payload;
            }

            public uint Count
            {
                get => data[1];
                set => data[1] = value;
            }

            public uint RefCount
            {
                get => data[0];
                set => data[0] = value;
            }

            public uint* Items
            {
                get => data + 2;
            }

            public Span<uint> ItemsAsSpan
            {
                get => new Span<uint>(data + 2, (int)Count);
            }

            [Conditional("DEBUG")]
            private void AssertIndex(uint index)
            {
                if (index >= Count)
                    throw new ArgumentOutOfRangeException("index");
            }

            public DynTree ToDynTree()
            {
                return new DynTree(DynTreeType.Array32, (ulong)data);
            }

            public static unsafe Array32 Create(IAllocator allocator, int count)
            {
                Array32 result = new Array32((ulong)allocator.Allocate(count * sizeof(uint) + sizeof(uint) + sizeof(uint)));
                result.RefCount = 1;
                result.Count = (uint)count;
                return result;
            }

            public static unsafe Array32 Create(IAllocator allocator, ReadOnlySpan<uint> data, uint offset)
            {
                Array32 array = Create(allocator, data.Length);
                data.CopyTo(array.ItemsAsSpan);
                array.ItemsAsSpan.SubtractScalar(offset);
                return array;
            }

            public static bool TryAdd(DynTree original, IAllocator allocator, uint id, out DynTree result)
            {
                Array32 array = original.AsArray32;
                Span<uint> existing = array.ItemsAsSpan;
                int index = existing.BinarySearch(id);

                // The given ID is already present.
                if (index >= 0)
                {
                    result = default;
                    return false;
                }

                index = ~index;
                DynTreeType newType = ChooseType(existing.Length + 1, Math.Max(id, existing[^1]));
                if (newType == DynTreeType.BitSet)
                {
                    BitSet bitSet = BitSet.Create(allocator, existing, 0);
                    bitSet.Set(id);
                    bitSet.Count++;
                    result = bitSet.ToDynTree();
                }
                else if (newType == DynTreeType.Array16)
                {
                    // Create a new array with the new id
                    Array16 newArray = Array16.Create(allocator, existing.Length + 1);
                    Span<ushort> target = newArray.ItemsAsSpan;
                    for (int i = index; i < existing.Length; i++)
                        target[i + 1] = (ushort)existing[i];
                    target[index] = (ushort)id;
                    for (int i = 0; i < index; i++)
                        target[i] = (ushort)existing[i];
                    result = newArray.ToDynTree();
                }
                else if (newType == DynTreeType.Array32)
                {
                    // Create a new array with the new id
                    Array32 newArray = Create(allocator, existing.Length + 1);
                    Span<uint> target = newArray.ItemsAsSpan;
                    InsertIntoSpan(existing, index, target, id);
                    result = newArray.ToDynTree();
                }
                else
                {
                    // Use the generic method for creating a new DynTree by using an ID stream reader, which
                    // includes the new id.
                    result = DynTree.Create(allocator, new IdStreamReaderSequence(array.GetStreamReader(0, index), id, array.GetStreamReader(index)));
                }
                return true;
            }
            
            public static bool TryRemove(DynTree original, IAllocator allocator, uint id, out DynTree result)
            {
                Array32 array = original.AsArray32;
                Span<uint> existing = array.ItemsAsSpan;
                int index = existing.BinarySearch(id);

                // The given ID is not present.
                if (index < 0)
                {
                    result = default;
                    return false;
                }
                if (index >= existing.Length)
                    throw new Exception("Invalid index.");

                if (existing.Length <= 5)
                {
                    Span<uint> tmp = stackalloc uint[existing.Length - 1];
                    int resultIndex = 0;
                    for (int i = 0; i < existing.Length; i++)
                    {
                        if (index == i)
                            continue;
                        tmp[resultIndex++] = existing[i];
                    }
                    if (!TryCreateLeaf(allocator, tmp, 0, out result))
                        throw new Exception("Leaf creation was not expected to fail.");
                }
                else
                {
                    Array32 newArray = Create(allocator, existing.Length - 1);
                    existing[..index].CopyTo(newArray.ItemsAsSpan);
                    existing[(index + 1)..].CopyTo(newArray.ItemsAsSpan[index..]);
                    result = newArray.ToDynTree();
                }
                return true;
            }

            public bool Contains(uint id)
            {
                return ItemsAsSpan.BinarySearch(id) >= 0;
            }

            public void Release(IAllocator allocator)
            {
                if (Interlocked.Decrement(ref data[0]) != 0)
                    return;

                allocator.Free(data);
            }

            public IIdStreamReader GetStreamReader()
            {
                return new IdStreamReader(Items, Count);
            }

            public IIdStreamReader GetStreamReader(int start)
            {
#if DEBUG
                if (start < 0)
                    throw new ArgumentOutOfRangeException(nameof(start));
                if (start > Count)
                    throw new ArgumentOutOfRangeException("start exceeds the array size.");
#endif
                return new IdStreamReader(Items + start, Count - (uint)start);
            }

            public IIdStreamReader GetStreamReader(int start, int count)
            {
#if DEBUG
                if (start < 0)
                    throw new ArgumentOutOfRangeException(nameof(start));
                if (count < 0)
                    throw new ArgumentOutOfRangeException(nameof(count));
                if (count + start > Count)
                    throw new ArgumentOutOfRangeException("start + count exceed the array size.");
#endif
                return new IdStreamReader(Items + start, (uint)count);
            }

            private class IdStreamReader : IIdStreamReader
            {
                private uint* data;
                private uint remaining;

                public IdStreamReader(uint* data, uint length)
                {
                    this.data = data;
                    this.remaining = length;
                }

                public int Read(Span<uint> target)
                {
                    int count = (int)Math.Min(target.Length, remaining);
                    new ReadOnlySpan<uint>(data, count).CopyTo(target);
                    remaining = (uint)(remaining - count);
                    data += count;
                    return count;
                }
            }
        }
    }
}
