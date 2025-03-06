namespace DynTree
{
    internal class IdStreamReaderArray(uint[] data) : IIdStreamReader
    {
        private int offset;

        public int Read(Span<uint> target)
        {
            int count = Math.Min(target.Length, data.Length - offset);
            new ReadOnlySpan<uint>(data, offset, count).CopyTo(target);
            offset += count;
            return count;
        }
    }
}
