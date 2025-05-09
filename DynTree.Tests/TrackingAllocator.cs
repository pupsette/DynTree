namespace DynTree.Tests;

public class TrackingAllocator : IAllocator
{
    public unsafe void* Allocate(long length)
    {
        Interlocked.Increment(ref AllocatedChunks);
        return DefaultAllocator.INSTANCE.Allocate(length);
    }

    public unsafe void Free(void* ptr)
    {
        DefaultAllocator.INSTANCE.Free(ptr);
        Interlocked.Decrement(ref AllocatedChunks);
    }

    public int AllocatedChunks;
}