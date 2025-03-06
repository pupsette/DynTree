using System.Runtime.InteropServices;

namespace DynTree
{
    public unsafe class DefaultAllocator : IAllocator
    {
        public static readonly DefaultAllocator INSTANCE = new();

        public void* Allocate(long length)
        {
            if (length > int.MaxValue)
                throw new OutOfMemoryException();
            return Marshal.AllocHGlobal((int)length).ToPointer();
        }

        public void Free(void* ptr)
        {
            Marshal.FreeHGlobal(new IntPtr(ptr));
        }
    }
}
