namespace DynTree
{
    internal class IdStreamReaderSequence : IIdStreamReader
    {
        private IIdStreamReader? before;
        private uint? id;
        private IIdStreamReader? after;

        public IdStreamReaderSequence(IIdStreamReader? before, uint? id, IIdStreamReader? after)
        {
            this.before = before;
            this.id = id;
            this.after = after;
        }

        public int Read(Span<uint> target)
        {
            int read = Advance(ref before, ref target);
            if (target.Length == 0)
                return read;
            if (id.HasValue)
            {
                target[0] = id.Value;
                id = null;
                target = target.Slice(1);
                read++;
            }
            if (target.Length == 0)
                return read;

            return read + Advance(ref after, ref target);
        }

        private static int Advance(ref IIdStreamReader? reader, ref Span<uint> target)
        {
            if (reader == null)
                return 0;

            int read = reader.Read(target);
            if (read < target.Length)
                reader = null;
            target = target.Slice(read);
            return read;
        }
    }
}
