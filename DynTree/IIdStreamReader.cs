namespace DynTree
{
    public interface IIdStreamReader
    {
        int Read(Span<uint> target);
    }
}
