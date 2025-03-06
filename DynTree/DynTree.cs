using System;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace DynTree
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 9)]
    public readonly partial struct DynTree
    {
        private delegate bool TryGetNextIdDelegate(ulong payload, uint currentPosition);

        private const int MAX_ARRAY_ITEM_COUNT = 1024;
        private const int MIN_WIDTH = 4096;

        public readonly byte Type;
        public readonly ulong Payload;

        public static readonly DynTree Empty = new DynTree();
        private const uint MAX_VALUE_INLINE3 = 0x1FFFFF;

        internal DynTree(DynTreeType type, ulong payload, bool isImmutable)
        {
            Type = (byte)((int)type | (isImmutable ? IMMUTABLE_FLAG : 0));
            Payload = payload;
        }

        internal DynTree(DynTreeType type, ulong payload)
        {
            Type = (byte)type;
            Payload = payload;
        }

        internal DynTree(byte type, ulong payload)
        {
            Type = type;
            Payload = payload;
        }

        private const byte IMMUTABLE_FLAG = 0x80;

        public bool IsImmutable { get => (Type & IMMUTABLE_FLAG) != 0; }

        public bool IsEmpty { get => Type == (int)DynTreeType.Empty; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DynTreeType TreeType()
        {
            return (DynTreeType)(Type & 0x7F);
        }

        public static DynTree Create(IAllocator allocator, uint[] ids)
        {
            return Create(allocator, new IdStreamReaderArray(ids));
        }

        public static DynTree Create(IAllocator allocator, IIdStreamReader ids)
        {
            // Create a single temporary buffer on the stack to hold up to 4096 IDs.
            Span<uint> d = stackalloc uint[MIN_WIDTH];

            // Create an IdBuffer struct which manages this buffer
            IdBuffer buffer = new(d, ids);

            return Create(allocator, ref buffer, 0, uint.MaxValue, 3);
        }

        private static DynTree Create(IAllocator allocator, ReadOnlySpan<uint> ids, uint offset)
        {
            if (TryCreateLeaf(allocator, ids, offset, out DynTree result))
                return result;

            return Node.Create(allocator, ids, offset).ToDynTree();
        }

        private static bool TryCreateLeaf(IAllocator allocator, ReadOnlySpan<uint> ids, uint offset, out DynTree dynTree)
        {
            if (ids.Length == 0)
            {
                dynTree = Empty;
                return true;
            }

#if DEBUG
            if (ids[0] < offset)
                throw new ArgumentOutOfRangeException(nameof(ids), "Smallest ID must not be smaller than the given offset.");
            if (ids.Length > MIN_WIDTH)
                throw new ArgumentOutOfRangeException(nameof(ids), $"A leaf node may not contain more than {MIN_WIDTH} entries.");
#endif

            DynTreeType type = ChooseType(ids.Length, ids[^1] - offset);
            if (type == DynTreeType.Node)
            {
                dynTree = default;
                return false;
            }
            dynTree = type switch
            {
                DynTreeType.Inline1 => Create(ids[0] - offset),
                DynTreeType.Inline2 => Create(ids[0] - offset, ids[1] - offset),
                DynTreeType.Inline3 => CreateInline3(ids[0] - offset, ids[1] - offset, ids[2] - offset),
                DynTreeType.Inline4 => CreateInline4(ids[0] - offset, ids[1] - offset, ids[2] - offset, ids[3] - offset),
                DynTreeType.BitSet => BitSet.Create(allocator, ids, offset).ToDynTree(),
                DynTreeType.Array16 => Array16.Create(allocator, ids, offset).ToDynTree(),
                DynTreeType.Array32 => Array32.Create(allocator, ids, offset).ToDynTree(),
                _ => throw new InvalidOperationException()
            };
            return true;
        }

        public static DynTree Create(uint id1)
        {
            return new DynTree(DynTreeType.Inline1, id1);
        }

        public static DynTree Create(uint id1, uint id2)
        {
            if (id1 >= id2)
                return id1 == id2 ? Create(id1) : new DynTree(DynTreeType.Inline2, ((ulong)id2 << 32) | id1);
            return new DynTree(DynTreeType.Inline2, ((ulong)id1 << 32) | id2);
        }

        private static DynTree CreateInline3(uint id1, uint id2, uint id3)
        {
#if DEBUG
            if (id1 >= id2)
                throw new ArgumentOutOfRangeException();
            if (id2 >= id3)
                throw new ArgumentOutOfRangeException();
            if (id3 >= 1 << 21)
                throw new ArgumentOutOfRangeException();
#endif
            return new DynTree(DynTreeType.Inline3, ((ulong)id1 << 42) | ((ulong)id2 << 21) | id3);
        }

        private static DynTree CreateInline4(uint id1, uint id2, uint id3, uint id4)
        {
#if DEBUG
            if (id1 >= id2)
                throw new ArgumentOutOfRangeException();
            if (id2 >= id3)
                throw new ArgumentOutOfRangeException();
            if (id3 >= id4)
                throw new ArgumentOutOfRangeException();
            if (id4 > ushort.MaxValue)
                throw new ArgumentOutOfRangeException();
#endif
            Vector64<ushort> vector = Vector64.Create((ushort)id1, (ushort)id2, (ushort)id3, (ushort)id4);
            return new DynTree(DynTreeType.Inline4, vector.AsUInt64().GetElement(0));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Inline3Id0() => (uint)(Payload >> 42) & 0x1FFFFF;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Inline3Id1() => (uint)(Payload >> 21) & 0x1FFFFF;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Inline3Id2() => (uint)Payload & 0x1FFFFF;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Inline1Id0() => (uint)Payload;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Inline2Id0() => (uint)(Payload >> 32);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint Inline2Id1() => (uint)Payload;

        public DynTree Add(IAllocator allocator, uint id)
        {
            if (TryAdd(allocator, id, out DynTree result))
                return result;
            
            Acquire();
            return this;
        }

        public unsafe bool TryAdd(IAllocator allocator, uint id, out DynTree result)
        {
            DynTreeType type = TreeType();

            if (type == DynTreeType.Node)
                return Node.TryAdd(this, allocator, id, out result);

            if (type == DynTreeType.Array16)
                return Array16.TryAdd(this, allocator, id, out result);

            if (type == DynTreeType.BitSet)
                return BitSet.TryAdd(this, allocator, id, out result);

            if (type == DynTreeType.Array32)
                return Array32.TryAdd(this, allocator, id, out result);

            if (type == DynTreeType.Empty)
            {
                result = Create(id);
                return true;
            }

            if (type == DynTreeType.Inline1)
            {
                if (Inline1Id0() == id)
                {
                    result = default;
                    return false;
                }
                result = Create(Inline1Id0(), id);
                return true;
            }

            Span<uint> ids = stackalloc uint[5];
            if (type == DynTreeType.Inline2)
            {
                ids[0] = Inline2Id0();
                ids[1] = Inline2Id1();
                return TryAddToSpan(allocator, ids.Slice(0, 3), id, out result);
            }
            if (type == DynTreeType.Inline3)
            {
                ids[0] = Inline3Id0();
                ids[1] = Inline3Id1();
                ids[2] = Inline3Id2();
                return TryAddToSpan(allocator, ids.Slice(0, 4), id, out result);
            }
            if (type == DynTreeType.Inline4)
            {
                var v = Vector64.Create(Payload).AsUInt16();
                ids[0] = v[0];
                ids[1] = v[1];
                ids[2] = v[2];
                ids[3] = v[3];
                return TryAddToSpan(allocator, ids, id, out result);
            }

            throw new InvalidOperationException($"Unexpected tree type '{type}'.");
        }

        public DynTree Remove(IAllocator allocator, uint id)
        {
            if (TryRemove(allocator, id, out DynTree result))
                return result;
            
            Acquire();
            return this;
        }

        public bool TryRemove(IAllocator allocator, uint id, out DynTree result)
        {
            DynTreeType type = TreeType();

            if (type == DynTreeType.Node)
                return Node.TryRemove(this, allocator, id, out result);

            if (type == DynTreeType.Array16)
                return Array16.TryRemove(this, allocator, id, out result);

            if (type == DynTreeType.BitSet)
                return BitSet.TryRemove(this, allocator, id, out result);

            if (type == DynTreeType.Array32)
                return Array32.TryRemove(this, allocator, id, out result);

            if (type == DynTreeType.Empty)
            {
                result = default;
                return false;
            }

            if (type == DynTreeType.Inline1)
            {
                result = Empty;
                return Inline1Id0() == id;
            }

            if (type == DynTreeType.Inline2)
            {
                if (Inline2Id0() == id)
                {
                    result = Create(Inline2Id1());
                    return true;
                }
                if (Inline2Id1() == id)
                {
                    result = Create(Inline2Id0());
                    return true;
                }
                result = default;
                return false;
            }
            if (type == DynTreeType.Inline3)
            {
                if (id == Inline3Id0())
                {
                    result = Create(Inline3Id1(), Inline3Id2());
                    return true;
                }
                if (id == Inline3Id1())
                {
                    result = Create(Inline3Id0(), Inline3Id2());
                    return true;
                }
                if (id == Inline3Id2())
                {
                    result = Create(Inline3Id0(), Inline3Id1());
                    return true;
                }
                result = default;
                return false;
            }
            if (type == DynTreeType.Inline4)
            {
                var v = Vector64.Create(Payload).AsUInt16();
                if (v[0] == id)
                    result = CreateInline3(v[1], v[2], v[3]);
                else if (v[1] == id)
                    result = CreateInline3(v[0], v[2], v[3]);
                else if (v[2] == id)
                    result = CreateInline3(v[0], v[1], v[3]);
                else if (v[3] == id)
                    result = CreateInline3(v[0], v[1], v[2]);
                else
                {
                    result = default;
                    return false;
                }
                return true;
            }

            throw new InvalidOperationException($"Unexpected tree type '{type}'.");
        }

        private DynTree CreateParentAndAdd(IAllocator allocator, uint newId)
        {
            Acquire();
            Node node = Node.Create(allocator, newId);
            node[0] = this;
            node.TotalCount = GetCount() + 1;

            DynTree tree = node.ToDynTree();
            DynTree newNode = tree.Add(allocator, newId);
            tree.Release(allocator);

            // this should not allocate anything, since the new node is mutable
            if (newNode.Payload != tree.Payload)
                throw new InvalidOperationException($"Node should be updated in-place when adding single ID {newId}.");
            return tree;
        }

        private static DynTreeType ChooseType(int count, uint maxId)
        {
            if (maxId < MIN_WIDTH && count >= 256)
                return DynTreeType.BitSet;
            else if (count < 3)
                return (DynTreeType)count;
            else if (count == 3 && maxId <= MAX_VALUE_INLINE3)
                return DynTreeType.Inline3;
            else if (count == 4 && maxId <= ushort.MaxValue)
                return DynTreeType.Inline4;
            else if (count <= MAX_ARRAY_ITEM_COUNT)
                return maxId <= ushort.MaxValue ? DynTreeType.Array16 : DynTreeType.Array32;
            else
                return DynTreeType.Node;
        }

        private static void InsertIntoSpan<T>(Span<T> source, int index, Span<T> target, T newId)
        {
            source.Slice(index).CopyTo(target.Slice(index + 1));
            source.Slice(0, index).CopyTo(target);
            target[index] = newId;
        }

        private bool TryAddToSpan(IAllocator allocator, Span<uint> span, uint newId, out DynTree result)
        {
            Span<uint> source = span[..^1];
            int index = source.BinarySearch(newId);
            if (index >= 0)
            {
                result = default;
                return false;
            }

            InsertIntoSpan(source, ~index, span, newId);
            result = Create(allocator, span, 0);
            return true;
        }

        public bool Contains(uint id)
        {
            DynTreeType type = TreeType();
            return type switch
            {
                DynTreeType.Node => AsNode.Contains(id),
                DynTreeType.Array16 => AsArray16.Contains(id),
                DynTreeType.BitSet => AsBitSet.Contains(id),
                DynTreeType.Array32 => AsArray32.Contains(id),
                DynTreeType.Empty => false,
                DynTreeType.Inline1 => Inline1Id0() == id,
                DynTreeType.Inline2 => Inline2Id0() == id | Inline2Id1() == id,
                DynTreeType.Inline3 => Inline3Id0() == id || Inline3Id1() == id || Inline3Id2() == id,
                DynTreeType.Inline4 => id <= ushort.MaxValue && Vector64.EqualsAny(Vector64.Create((ushort)id), Vector64.Create(Payload).AsUInt16()),
                _ => throw new InvalidOperationException()
            };
        }

        private static unsafe DynTree Create(IAllocator allocator, ref IdBuffer buffer, uint inclusiveMin, uint inclusiveMax, int level)
        {
            if (buffer.TryGet(inclusiveMax, out ReadOnlySpan<uint> ids))
            {
                if (TryCreateLeaf(allocator, ids, inclusiveMin, out DynTree result))
                {
                    buffer.Drop(ids.Length);
                    return result;
                }
            }
            if (level < 0)
                throw new InvalidOperationException("buffer.TryGet and TryCreateLeaf were expected to succeed for the lowest level.");

            Span<DynTree> children = stackalloc DynTree[Node.CHILDREN];
            uint width = Node.WIDTHS[level];
            long totalCount = 0;
            int childCount = 0;
            while (childCount < Node.CHILDREN)
            {
                long newMin = inclusiveMin + childCount * width;
                if (newMin > uint.MaxValue)
                    break;

                DynTree child = Create(allocator, ref buffer, (uint)newMin, (uint)Math.Min(uint.MaxValue, newMin + width - 1), level - 1);
                totalCount += child.GetCount();
                children[childCount++] = child;

                if (buffer.IsEmpty)
                {
                    if (childCount == 1)
                        return child;
                    break;
                }
            }

            Node node = Node.Create(allocator);
            node.Level = (uint)level;
            node.TotalCount = (uint)totalCount;
            for (int i = 0; i < childCount; i++)
                node[i] = children[i];
            return node.ToDynTree();
        }

        public void Release(IAllocator allocator)
        {
            DynTreeType treeType = TreeType();
            switch (treeType)
            {
                case DynTreeType.Array16:
                    AsArray16.Release(allocator);
                    break;
                case DynTreeType.Array32:
                    AsArray32.Release(allocator);
                    break;
                case DynTreeType.BitSet:
                    AsBitSet.Release(allocator);
                    break;
                case DynTreeType.Node:
                    AsNode.Release(allocator);
                    break;
            }
        }

        public unsafe void Acquire()
        {
            switch (TreeType())
            {
                case DynTreeType.Array16:
                case DynTreeType.Array32:
                case DynTreeType.BitSet:
                case DynTreeType.Node:
                    Interlocked.Increment(ref *(uint*)Payload);
                    break;
            }
        }

        public uint GetCount()
        {
            DynTreeType type = TreeType();
            return type switch
            {
                DynTreeType.Array16 => AsArray16.Count,
                DynTreeType.Array32 => AsArray32.Count,
                DynTreeType.BitSet => AsBitSet.Count,
                DynTreeType.Node => AsNode.TotalCount,
                _ => (uint)type
            };
        }

        public DynTree MakeImmutable()
        {
            if (IsImmutable)
                return this;

            if ((DynTreeType)Type == DynTreeType.Node)
                AsNode.MakeImmutable();

            return new DynTree((DynTreeType)Type, Payload, true);
        }

        [Conditional("DEBUG")]
        private void AssertType(DynTreeType type)
        {
            if (TreeType() != type)
                throw new InvalidOperationException($"Tree type was expected to be {type}.");
        }

        public int EstimateMemoryConsumption()
        {
            const int heapObjectOverhead = 32;
            return TreeType() switch
            {
                DynTreeType.Empty => 0,
                DynTreeType.Inline1 => 0,
                DynTreeType.Inline2 => 0,
                DynTreeType.Inline3 => 0,
                DynTreeType.Inline4 => 0,
                DynTreeType.Array16 => heapObjectOverhead + (int)AsArray16.Count * sizeof(ushort) + 6,
                DynTreeType.Array32 => heapObjectOverhead + (int)AsArray32.Count * sizeof(uint) + 8,
                DynTreeType.BitSet => heapObjectOverhead + BitSet.SIZE,
                DynTreeType.Node => EstimateNodeSize(AsNode),
                _ => throw new InvalidOperationException()
            };

            static int EstimateNodeSize(Node node)
            {
                int total = heapObjectOverhead + Node.SIZE;
                for (int i = 0; i < Node.CHILDREN; i++)
                    total += node[i].EstimateMemoryConsumption();
                return total;
            }
        }

        public IIdStreamReader GetStreamReader()
        {
            return TreeType() switch
            {
                DynTreeType.Empty => IdStreamReaderInline.Empty,
                DynTreeType.Inline1 => new IdStreamReaderInline(Inline1Id0()),
                DynTreeType.Inline2 => new IdStreamReaderInline(Inline2Id0(), Inline2Id1()),
                DynTreeType.Inline3 => new IdStreamReaderInline(Inline3Id0(),Inline3Id1(),Inline3Id2()),
                DynTreeType.Inline4 => new IdStreamReaderInline(Vector64.Create(Payload).AsUInt16()),
                DynTreeType.Array16 => AsArray16.GetStreamReader(),
                DynTreeType.Array32 => AsArray32.GetStreamReader(),
                DynTreeType.BitSet => AsBitSet.GetStreamReader(),
                DynTreeType.Node => AsNode.GetStreamReader(),
                _ => throw new InvalidOperationException()
            };
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            ToString(sb, 0);
            return sb.ToString();
        }

        private void ToString(StringBuilder sb, int indent)
        {
            sb.Append(' ', indent);
            switch (TreeType())
            {
                case DynTreeType.Empty: sb.Append("Empty"); break;
                case DynTreeType.Inline1: sb.Append($"Inline1: {Inline1Id0()}"); break;
                case DynTreeType.Inline2: sb.Append($"Inline2: {Inline2Id0()}, {Inline2Id1()}"); break;
                case DynTreeType.Inline3: sb.Append($"Inline3: {Inline3Id0()}, {Inline3Id1()}, {Inline3Id2()}"); break;
                case DynTreeType.Inline4:
                    {
                        Vector64<ushort> v = Vector64.Create(Payload).AsUInt16();
                        sb.Append($"Inline4: {v.GetElement(0)}, {v.GetElement(1)}, {v.GetElement(2)}, {v.GetElement(3)}");
                        break;
                    }
                case DynTreeType.Array16:
                    {
                        sb.Append($"Array16: ");
                        sb.Append(AsArray16.ItemsAsSpan[0]);
                        foreach (ushort id in AsArray16.ItemsAsSpan.Slice(1))
                        {
                            sb.Append(", ");
                            sb.Append(id);
                        }
                        break;
                    }
                case DynTreeType.Array32:
                    {
                        sb.Append($"Array32: ");
                        sb.Append(AsArray32.ItemsAsSpan[0]);
                        foreach (uint id in AsArray32.ItemsAsSpan.Slice(1))
                        {
                            sb.Append(", ");
                            sb.Append(id);
                        }
                        break;
                    }
                case DynTreeType.BitSet:
                    {
                        sb.Append($"BitSet: {AsBitSet.Count} bits set");
                        break;
                    }
                case DynTreeType.Node:
                    {
                        sb.Append($"Node: {AsNode.TotalCount} total count");
                        for (int i = 0; i < Node.CHILDREN; i++)
                        {
                            sb.AppendLine();
                            sb.Append(' ', indent);
                            sb.Append($"{i:00} (offset={i * AsNode.Width})");
                            AsNode[i].ToString(sb, indent + 2);
                        }
                        break;
                    }
                default: throw new InvalidOperationException();
            }
        }
    }
}
