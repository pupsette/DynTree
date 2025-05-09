using System.Diagnostics;

namespace DynTree
{
    public readonly partial struct DynTree
    {
        private Node AsNode
        {
            get
            {
                AssertType(DynTreeType.Node);
                return new Node(Payload);
            }
        }

        private readonly unsafe struct Node
        {
            public static Node Create(IAllocator allocator)
            {
                Node node = new((ulong)allocator.Allocate(SIZE));
                new Span<byte>((byte*)node.data, SIZE).Clear();
                node.RefCount = 1;
                return node;
            }

            public static Node Create(IAllocator allocator, ReadOnlySpan<uint> ids, uint offset)
            {
                uint maxId = ids[^1] - offset;
                Node target = Create(allocator, maxId);
                uint width = target.Width;
                for (int i = 0; i < CHILDREN; i++)
                {
                    long childRangeMin = i * width + offset;
                    long childRangeExclusiveMax = childRangeMin + width;
                    if (childRangeMin > maxId || ids.Length == 0)
                        break;

                    if (ids[0] >= childRangeExclusiveMax)
                        continue;
                    if (childRangeExclusiveMax > uint.MaxValue)
                    {
                        target[i] = DynTree.Create(allocator, ids, (uint)childRangeMin);
                        break;
                    }

                    int index = ids.IndexOfAnyInRange((uint)childRangeExclusiveMax, uint.MaxValue);
                    target[i] = DynTree.Create(allocator, index < 0 ? ids : ids.Slice(0, index), (uint)childRangeMin);
                    ids = index < 0 ? default : ids.Slice(index);
                }
                return target;
            }

            public Node(ulong payload)
            {
                data = (uint*)payload;
            }

            public static bool TryAdd(DynTree original, IAllocator allocator, uint id, out DynTree result)
            {
                Node node = original.AsNode;
                uint nodeWidth = node.Width;
                int childIndex = (int)(id / node.Width);
                if (childIndex >= CHILDREN)
                {
                    result = original.CreateParentAndAdd(allocator, id);
                    return true;
                }

                uint offset = (uint)childIndex * nodeWidth;
                // The new ID must be inserted into the existing node
                DynTree previousChild = node[childIndex];
                if (!previousChild.TryAdd(allocator, id - offset, out DynTree newChild))
                {
                    result = default;
                    return false;
                }

                Node targetNode = node;
                if (original.IsImmutable)
                    targetNode = Create(allocator, node);
                else
                    original.Acquire();

                previousChild.Release(allocator);
                targetNode[childIndex] = newChild;
                targetNode.TotalCount++;
                result = targetNode.ToDynTree();
                return true;
            }

            public void MakeImmutable()
            {
                const byte mutableNodeType = (byte)DynTreeType.Node;
                for (int i = 0; i < CHILDREN; i++)
                {
                    ref byte type = ref GetType(i);
                    if (type == mutableNodeType)
                        new Node(GetPayload(i)).MakeImmutable();
                    type |= IMMUTABLE_FLAG;
                }
            }

            public static Node Create(IAllocator allocator, uint maxId)
            {
                Node node = new((ulong)allocator.Allocate(SIZE));
                new Span<byte>((byte*)node.data, SIZE).Clear();
                node.RefCount = 1;
                node.Level = FindNodeLevel(maxId);
                return node;
            }

            public static Node Create(IAllocator allocator, Node copyFrom)
            {
                Node node = new((ulong)allocator.Allocate(SIZE));
                System.Buffer.MemoryCopy(copyFrom.data, node.data, SIZE, SIZE);
                node.RefCount = 1;
                for (int i = 0; i < CHILDREN; i++)
                    node[i].Acquire();
                return node;
            }

            public DynTree ToDynTree()
            {
                return new DynTree(DynTreeType.Node, (ulong)data);
            }

            private const int HEADER_SIZE = 8;
            private const int PAYLOAD_OFFSET = CHILDREN + HEADER_SIZE;
            internal const int SIZE = CHILDREN * 9 + HEADER_SIZE;
            internal const int CHILDREN = 16;
            internal static readonly uint[] WIDTHS;

            static Node()
            {
                List<uint> widths = new List<uint>();
                long width = MIN_WIDTH;
                while (width < uint.MaxValue)
                {
                    widths.Add((uint)width);
                    width *= CHILDREN;
                }
                WIDTHS = widths.ToArray();
            }

            private const int LEVEL_SHIFT = 24;
            private const uint REF_COUNT_MASK = 0x00FFFFFF;

            private readonly uint* data;

            public ref uint TotalCount
            {
                get => ref data[1];
            }

            public uint Width
            {
                get => WIDTHS[Level];
            }
            /// <summary>
            /// Level 0: width of 4096 (lowest level for nodes)
            /// Level 1: WIDTH_1
            /// ...
            /// </summary>
            public uint Level
            {
                get => data[0] >> LEVEL_SHIFT;
                set => data[0] = (value << LEVEL_SHIFT) | (data[0] & REF_COUNT_MASK);
            }
            public uint RefCount
            {
                get => data[0] & REF_COUNT_MASK;
                set => data[0] = (data[0] & ~REF_COUNT_MASK) | value;
            }

            [Conditional("DEBUG")]
            private void AssertIndex(int index)
            {
                if (index < 0 || index >= CHILDREN)
                    throw new ArgumentOutOfRangeException("index");
            }

            private ref byte GetType(int index)
            {
                return ref ((byte*)(data + 2))[index];
            }

            private ref ulong GetPayload(int index)
            {
                return ref ((ulong*)((byte*)data + PAYLOAD_OFFSET))[index];
            }

            public DynTree this[int index]
            {
                get
                {
                    AssertIndex(index);
                    return new DynTree(GetType(index), GetPayload(index));
                }
                set
                {
                    AssertIndex(index);
                    GetType(index) = value.Type;
                    GetPayload(index) = value.Payload;
                }
            }

            public bool Contains(uint id)
            {
                uint width = Width;
                if (id >= (long)width * CHILDREN)
                    return false;
                uint index = id / width;
                return this[(int)index].Contains(id - index * width);
            }

            public void Release(IAllocator allocator)
            {
                if ((Interlocked.Decrement(ref data[0]) & REF_COUNT_MASK) != 0)
                    return;

                for (int i = 0; i < CHILDREN; i++)
                    this[i].Release(allocator);

                allocator.Free(data);
            }

            private static uint FindNodeLevel(uint maxId)
            {
                if (maxId < WIDTHS[0])
                    throw new InvalidOperationException($"A node should not be created when the maximum ID is {maxId}.");

                for (uint i = 1; i < WIDTHS.Length; i++)
                {
                    if (maxId < WIDTHS[i])
                        return i - 1;
                }
                return (uint)WIDTHS.Length - 1;
            }

            public static bool TryRemove(DynTree original, IAllocator allocator, uint id, out DynTree result)
            {
                Node node = original.AsNode;
                uint nodeWidth = node.Width;
                int childIndex = (int)(id / node.Width);
                if (childIndex >= CHILDREN)
                {
                    result = default;
                    return false;
                }

                uint offset = (uint)childIndex * nodeWidth;
                // The new ID must be removed from the existing node
                DynTree previousChild = node[childIndex];
                if (!previousChild.TryRemove(allocator, id - offset, out DynTree newChild))
                {
                    result = default;
                    return false;
                }

                Node targetNode = node;
                if (original.IsImmutable)
                    targetNode = Create(allocator, node);
                else
                    original.Acquire();

                previousChild.Release(allocator);
                targetNode[childIndex] = newChild;
                targetNode.TotalCount--;

                // Convert the node to a leaf, if it is too small now
                if (targetNode.TotalCount <= MAX_ARRAY_ITEM_COUNT)
                {
                    var leaf = DynTree.Create(allocator, targetNode.GetStreamReader());
                    targetNode.Release(allocator);
                    result = leaf;
                }
                else
                    result = targetNode.ToDynTree();

                return true;
            }

            public IIdStreamReader GetStreamReader()
            {
                return new IdStreamReader(this);
            }
            
            private class IdStreamReader(Node node) : IIdStreamReader
            {
                private Node node = node;
                private readonly uint width = node.Width;
                private int currentChildIndex = -1;
                private IIdStreamReader? currentChild;

                public int Read(Span<uint> target)
                {
                    int read = 0;
                    while (read < target.Length)
                    {
                        if (currentChild == null)
                        {
                            currentChildIndex++;
                            if (currentChildIndex >= CHILDREN)
                                return read;
                            currentChild = node[currentChildIndex].GetStreamReader();
                        }

                        Span<uint> remainingTarget = target.Slice(read);
                        int childRead = currentChild.Read(remainingTarget);
                        if (currentChildIndex > 0)
                            remainingTarget.AddScalar((uint)currentChildIndex * width);
                        read += childRead;
                        if (childRead < remainingTarget.Length)
                            currentChild = null;
                    }
                    return read;
                }
            }
        }
    }
}
