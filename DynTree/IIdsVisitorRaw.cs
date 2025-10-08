namespace DynTree
{
    public interface IIdsVisitorRaw
    {
        bool VisitBits(uint offset, ReadOnlySpan<ulong> bits);
        bool VisitArray(uint offset, ReadOnlySpan<uint> ids);
        bool VisitArray(uint offset, ReadOnlySpan<ushort> ids);
        bool VisitArray(uint offset, ReadOnlySpan<byte> ids);
    }
}
