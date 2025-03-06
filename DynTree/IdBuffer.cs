namespace DynTree
{
    internal ref struct IdBuffer
    {
        public Span<uint> buffer;
        private readonly IIdStreamReader ids;
        private int count;
        private uint nextId;
        private bool hasNext;

        public IdBuffer(Span<uint> buffer, IIdStreamReader ids)
        {
            this.buffer = buffer;
            this.ids = ids;
            count = ids.Read(buffer);
            Peek();
        }

        public bool IsEmpty { get => count == 0; }

        public bool TryGet(uint inclusiveMax, out ReadOnlySpan<uint> result)
        {
            if (count == 0 || buffer[0] > inclusiveMax)
            {
                result = default;
                return true;
            }
            Span<uint> validIds = buffer.Slice(0, count);
            if (validIds[^1] <= inclusiveMax)
            {
                result = hasNext ? default : validIds;
                return !hasNext;
            }
            int index = validIds.BinarySearch(inclusiveMax + 1);
            if (index < 0)
                index = ~index;

            result = buffer.Slice(0, index);
            return true;
        }

        public void Drop(int toDrop)
        {
            if (toDrop == 0)
                return;
            if (toDrop > count)
                throw new ArgumentException();
            if (toDrop < count)
                buffer.Slice(toDrop).CopyTo(buffer);
            count -= toDrop;
            Fill();
        }

        private void Fill()
        {
            if (!hasNext)
                return;

            if (count < buffer.Length)
            {
                buffer[count++] = nextId;
                count += ids.Read(buffer.Slice(count));
            }
            Peek();
        }

        private void Peek()
        {
            if (count == buffer.Length)
                hasNext = ids.Read(new Span<uint>(ref nextId)) > 0;
            else
                hasNext = false;
        }
    }
}
