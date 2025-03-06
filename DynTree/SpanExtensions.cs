using System.Numerics;
using System.Runtime.Intrinsics;

namespace DynTree
{
    internal static class SpanExtensions
    {
        public static void SubtractScalar(this Span<uint> data, uint toSubtract)
        {
            if (data.Length == 0 || toSubtract == 0)
                return;

            int i = 0;
            int length = data.Length;
            if (Vector256<uint>.IsSupported)
            {
                Vector256<uint> scalar = Vector256.Create(toSubtract);
                int vectorSize = Vector256<uint>.Count;
                for (; i <= length - vectorSize; i += vectorSize)
                {
                    Vector256<uint> result = Vector256.LoadUnsafe(ref data[i]) - scalar;
                    result.StoreUnsafe(ref data[i]);
                }
            }

            for (; i < length; i++)
                data[i] -= toSubtract;
        }

        public static void AddScalar(this Span<uint> data, uint toAdd)
        {
            if (data.Length == 0 || toAdd == 0)
                return;

            int i = 0;
            int length = data.Length;
            if (Vector256<uint>.IsSupported)
            {
                Vector256<uint> scalar = Vector256.Create(toAdd);
                int vectorSize = Vector256<uint>.Count;
                for (; i <= length - vectorSize; i += vectorSize)
                {
                    Vector256<uint> result = Vector256.LoadUnsafe(ref data[i]) + scalar;
                    result.StoreUnsafe(ref data[i]);
                }
            }

            for (; i < length; i++)
                data[i] += toAdd;
        }
        
        public static void CopyUintToUshortAndSubtract(this ReadOnlySpan<uint> data, Span<ushort> target, uint toSubtract)
        {
            if (data.Length == 0)
                return;

            int length = data.Length;
            for (int i = 0; i < length; i++)
                target[i] = (ushort)(data[i] - toSubtract);
        }

        public static void CopyUshortToUintAndSubtract(this Span<ushort> data, Span<uint> target, ushort toSubtract)
        {
            if (data.Length == 0)
                return;

            int i = 0;
            int length = data.Length;
            if (Vector128<ushort>.IsSupported)
            {
                Vector128<ushort> scalar = Vector128.Create(toSubtract);
                int vectorSize = Vector128<ushort>.Count;
                for (; i <= length - vectorSize; i += vectorSize)
                {
                    Vector128<ushort> result = Vector128.LoadUnsafe(ref data[i]) - scalar;
                    (Vector128<uint> Lower, Vector128<uint> Upper) = Vector128.Widen(result);
                    Lower.StoreUnsafe(ref target[i]);
                    Upper.StoreUnsafe(ref target[i + Vector128<uint>.Count]);
                }
            }

            for (; i < length; i++)
                target[i] = (uint)data[i] - toSubtract;
        }
    }
}
