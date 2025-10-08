using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace DynTree
{
    public readonly partial struct DynTree : IEnumerable<uint>
    {
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        IEnumerator<uint> IEnumerable<uint>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public unsafe struct Enumerator : IEnumerator<uint>
        {
            private BitSetEnumerator currentEnumerator;
            private FixedSizeStack stack;
            private int stackIndex;

            internal Enumerator(DynTree dynTree)
            {
                stackIndex = -1;
                PushToStack(dynTree);
            }

            public uint Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                while (stackIndex >= 0)
                {
                    ref StackEntry e = ref stack[stackIndex];
                    DynTreeType type = e.Tree.TreeType();

                    if (type == DynTreeType.Empty)
                    {
                        FinishNode();
                        continue;
                    }

                    if (type == DynTreeType.Inline1)
                    {
                        Current = e.Tree.Inline1Id0() + e.Offset;
                        FinishNode();
                        return true;
                    }

                    if (type == DynTreeType.Inline2)
                    {
                        if (e.Index == 0)
                        {
                            Current = e.Tree.Inline2Id0() + e.Offset;
                            e.Index++;
                            return true;
                        }
                        Current = e.Tree.Inline2Id1() + e.Offset;
                        FinishNode();
                        return true;
                    }

                    if (type == DynTreeType.Inline3)
                    {
                        if (e.Index == 0)
                        {
                            Current = e.Tree.Inline3Id0() + e.Offset;
                            e.Index++;
                            return true;
                        }
                        if (e.Index == 1)
                        {
                            Current = e.Tree.Inline3Id1() + e.Offset;
                            e.Index++;
                            return true;
                        }

                        Current = e.Tree.Inline3Id2() + e.Offset;
                        FinishNode();
                        return true;
                    }

                    if (type == DynTreeType.Inline4)
                    {
                        var v = Vector64.Create(e.Tree.Payload).AsUInt16();
                        Current = v[(int)e.Index] + e.Offset;
                        if (e.Index < 3)
                            e.Index++;
                        else
                            FinishNode();
                        return true;
                    }

                    if (type == DynTreeType.Array16)
                    {
                        if (e.Index < e.Tree.AsArray16.Count)
                        {
                            Current = e.Tree.AsArray16.Items[e.Index] + e.Offset;
                            e.Index++;
                        }
                        if (e.Index >= e.Tree.AsArray16.Count)
                            FinishNode();
                        return true;
                    }

                    if (type == DynTreeType.Array32)
                    {
                        if (e.Index < e.Tree.AsArray32.Count)
                        {
                            Current = e.Tree.AsArray32.Items[e.Index] + e.Offset;
                            e.Index++;
                        }
                        if (e.Index >= e.Tree.AsArray32.Count)
                            FinishNode();
                        return true;
                    }

                    if (type == DynTreeType.BitSet)
                    {
                        if (currentEnumerator.TryGetNextBit(out uint id))
                        {
                            Current = id + e.Offset;
                            return true;
                        }

                        FinishNode();
                        continue;
                    }

                    throw new Exception($"Unexpected node type {type}.");
                }
                return false;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            private void FinishNode()
            {
            popNode:
                stackIndex--;
                if (stackIndex < 0)
                    return;

                // If there is still something on the stack it has to be a Node.
                ref StackEntry e = ref stack[stackIndex];
                e.Index++;
                if (e.Index < Node.CHILDREN)
                    PushToStack(e.Tree.AsNode[(int)e.Index]);
                else
                    goto popNode;
            }

            private void PushToStack(DynTree dynTree)
            {
            pushNode:
                uint offset = 0;
                if (stackIndex >= 0)
                    offset = stack[stackIndex].Offset + stack[stackIndex].Tree.AsNode.Width * stack[stackIndex].Index;

                stack[++stackIndex] = new StackEntry(dynTree, 0, offset);
                if (dynTree.TreeType() == DynTreeType.Node)
                {
                    dynTree = dynTree.AsNode[0];
                    goto pushNode;
                }
                if (dynTree.TreeType() == DynTreeType.BitSet)
                    currentEnumerator = new BitSetEnumerator(dynTree.AsBitSet.Bits, dynTree.AsBitSet.BitsAsSpan.Length);
            }

            private record struct StackEntry(DynTree Tree, uint Index, uint Offset);

            [InlineArray(10)]
            private struct FixedSizeStack
            {
                private StackEntry item;
            }
        }
    }
}