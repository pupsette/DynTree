namespace DynTree.Tests
{
    [TestFixture]
    internal class BitSetEnumeratorTests
    {
        [TestCase(1, 64)]
        [TestCase(0, 11, 16, 63)]
        [TestCase(1023, 1024)]
        public unsafe void Enumerate_bitset(params int[] ids)
        {
            int length = ids.Max() / 64 + 1;
            ulong[] bitSet = new ulong[length];
            foreach (int id in ids)
                bitSet[id >> 6] ^= 1UL << (id & 63);

            List<uint> target = new();
            fixed (ulong* bitSetPtr = bitSet)
            {
                var enumerator = new BitSetEnumerator(bitSetPtr, bitSet.Length);
                while (enumerator.TryGetNextBit(out uint tmpId))
                    target.Add(tmpId);
            }

            Assert.That(target, Is.EquivalentTo(ids));
        }

        [Test]
        public unsafe void Enumerate_empty_bitset()
        {
            ulong[] bitSet = new ulong[10];

            fixed (ulong* bitSetPtr = bitSet)
            {
                var enumerator = new BitSetEnumerator(bitSetPtr, bitSet.Length);
                Assert.That(enumerator.TryGetNextBit(out uint tmpId), Is.False);
            }
        }

        [Test]
        public unsafe void Enumerate_zero_length_bitset()
        {
            var enumerator = new BitSetEnumerator(null, 0);
            Assert.That(enumerator.TryGetNextBit(out uint tmpId), Is.False);
        }
    }
}
