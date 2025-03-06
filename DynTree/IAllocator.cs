namespace DynTree
{
    public unsafe interface IAllocator
    {
        void* Allocate(long length);
        void Free(void* ptr);
    }
}
