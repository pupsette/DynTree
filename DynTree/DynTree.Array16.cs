using System.Diagnostics;
using System.Runtime.Intrinsics;

namespace DynTree
{
    public readonly partial struct DynTree
    {
        private Array16 AsArray16
        {
            get
            {
                AssertType(DynTreeType.Array16);
                return new Array16(Payload);
            }
        }

        private unsafe readonly struct Array16
        {
            private readonly uint* data;

            public Array16(ulong payload)
            {
                data = (uint*)payload;
            }

            public ushort Count
            {
                get => *(ushort*)(data + 1);
                set => *(ushort*)(data + 1) = value;
            }

            public uint RefCount
            {
                get => data[0];
                set => data[0] = value;
            }

            public ushort* Items
            {
                get => (ushort*)data + 3;
            }

            public Span<ushort> ItemsAsSpan
            {
                get => new Span<ushort>(Items, Count);
            }

            [Conditional("DEBUG")]
            private void AssertIndex(uint index)
            {
                if (index >= Count)
                    throw new ArgumentOutOfRangeException("index");
            }

            public DynTree ToDynTree()
            {
                return new DynTree(DynTreeType.Array16, (ulong)data);
            }

            public static Array16 Create(IAllocator allocator, int count)
            {
                if (count > ushort.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(count));
                Array16 result = new Array16((ulong)allocator.Allocate(count * sizeof(ushort) + sizeof(uint) + sizeof(ushort)));
                result.RefCount = 1;
                result.Count = (ushort)count;
                return result;
            }

            public static Array16 Create(IAllocator allocator, ReadOnlySpan<uint> data, uint offset)
            {
                Array16 array = Create(allocator, data.Length);
                data.CopyUintToUshortAndSubtract(array.ItemsAsSpan, offset);
                return array;
            }

            public static bool TryAdd(DynTree original, IAllocator allocator, uint id, out DynTree result)
            {
                Array16 array = original.AsArray16;
                Span<ushort> existing = array.ItemsAsSpan;
                int index = id <= ushort.MaxValue ? existing.BinarySearch((ushort)id) : ~array.Count;

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
                    return true;
                }
                if (newType == DynTreeType.Array16)
                {
                    // Create a new array with the new id
                    Array16 newArray = Create(allocator, existing.Length + 1);
                    Span<ushort> target = newArray.ItemsAsSpan;
                    InsertIntoSpan(existing, index, target, (ushort)id);
                    result = newArray.ToDynTree();
                    return true;
                }
                if (newType == DynTreeType.Array32)
                {
                    // Create a new array with the new id
                    Array32 newArray = Array32.Create(allocator, existing.Length + 1);
                    Span<uint> target = newArray.ItemsAsSpan;
                    for (int i = index; i < existing.Length; i++)
                        target[i + 1] = existing[i];
                    target[index] = (ushort)id;
                    for (int i = 0; i < index; i++)
                        target[i] = existing[i];
                    result = newArray.ToDynTree();
                    return true;
                }
                else
                {
                    // Use the generic method for creating a new DynTree by using an ID stream reader, which
                    // includes the new id.
                    result = DynTree.Create(allocator, new IdStreamReaderSequence(array.GetStreamReader(0, index), id, array.GetStreamReader(index)));
                    return true;
                }
            }
            
            public static bool TryRemove(DynTree original, IAllocator allocator, uint id, out DynTree result)
            {
                Array16 array = original.AsArray16;
                Span<ushort> existing = array.ItemsAsSpan;
                int index = id <= ushort.MaxValue ? existing.BinarySearch((ushort)id) : -1;

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
                    Array16 newArray = Create(allocator, existing.Length - 1);
                    existing[..index].CopyTo(newArray.ItemsAsSpan);
                    existing[(index + 1)..].CopyTo(newArray.ItemsAsSpan[index..]);
                    result = newArray.ToDynTree();
                }
                return true;
            }

            public bool Contains(uint id)
            {
                return id <= ushort.MaxValue && ItemsAsSpan.BinarySearch((ushort)id) >= 0;
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

            internal unsafe class IdStreamReader : IIdStreamReader
            {
                private ushort* data;
                private uint remaining;

                public IdStreamReader(ushort* data, uint length)
                {
                    this.data = data;
                    this.remaining = length;
                }

                public int Read(Span<uint> target)
                {
                    int count = (int)Math.Min(target.Length, remaining);
                    for (int i = 0; i < count; i++)
                        target[i] = data[i];
                    remaining = (uint)(remaining - count);
                    data += count;
                    return count;
                }
            }
        }
    }
}
