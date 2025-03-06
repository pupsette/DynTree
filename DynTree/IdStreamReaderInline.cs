using System.Runtime.Intrinsics;

namespace DynTree;

internal class IdStreamReaderInline : IIdStreamReader
{
    public static readonly IIdStreamReader Empty = new IdStreamReaderInline();
            
    private struct Id4
    {
        public unsafe fixed uint Items[4];
    }

    private Id4 ids;
    private int count;

    internal unsafe IdStreamReaderInline(uint id0, uint id1, uint id2, uint id3)
    {
        ids.Items[0] = id0;
        ids.Items[1] = id1;
        ids.Items[2] = id2;
        ids.Items[3] = id3;
        count = 4;
    }

    internal unsafe IdStreamReaderInline(Vector64<ushort> vids)
    {
        ids.Items[0] = vids.GetElement(0);
        ids.Items[1] = vids.GetElement(1);
        ids.Items[2] = vids.GetElement(2);
        ids.Items[3] = vids.GetElement(3);
        count = 4;
    }
    
    internal unsafe IdStreamReaderInline(uint id0, uint id1, uint id2)
    {
        ids.Items[0] = id0;
        ids.Items[1] = id1;
        ids.Items[2] = id2;
        count = 3;
    }

    internal unsafe IdStreamReaderInline(uint id0, uint id1)
    {
        ids.Items[0] = id0;
        ids.Items[1] = id1;
        count = 2;
    }

    internal unsafe IdStreamReaderInline(uint id0)
    {
        ids.Items[0] = id0;
        count = 1;
    }

    internal IdStreamReaderInline()
    {
        count = 0;
    }
            
    public unsafe int Read(Span<uint> target)
    {
        if (count == 0)
            return 0;

        fixed (uint* ptr = ids.Items)
        {
            Span<uint> source = new(ptr, count);
            int toCopy = Math.Min(source.Length, count);
            source[..toCopy].CopyTo(target);

            if (toCopy < count)
                source.Slice(toCopy).CopyTo(source);

            count -= toCopy;
            return toCopy;
        }
    }
}