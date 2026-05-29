using System.Diagnostics;

namespace DynTree.Tests
{
    [TestFixture]
    public class DynTreeTests
    {
        private static readonly TrackingAllocator allocator = new TrackingAllocator();
        private const int SEED = 8879123;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
        }

        [SetUp]
        public void SetUp()
        {
            allocator.AllocatedChunks = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Assert.That(allocator.AllocatedChunks, Is.EqualTo(0));
        }
        
        [TestCase(32, 32, SEED)]
        [TestCase(10, 2500, SEED)]
        public void Test_complete_ranges(int maxCount, int maxId, int? seed = null)
        {
            for (int size = 0; size <= maxCount; size++)
            {
                Test_complete_range(size, maxId, seed);
            }
        }

        [TestCase(3597, 6000, SEED)]
        [TestCase(4096, 6000, SEED)]
        [TestCase(4097, 6000, SEED)]
        public void Test_complete_range(int size, int maxId, int? seed = null)
        {
            var random = new Random((seed + size + maxId) ?? Random.Shared.Next());

            uint[] items = PickRandom(size, maxId, random);
            DynTree tree = DynTree.Create(allocator, items);
            Console.WriteLine(tree);
            Console.WriteLine($"Size {size}: {tree.TreeType()}");

            for (uint i = 0; i < maxId; i++)
            {
                bool isPresent = items.AsSpan().BinarySearch(i) >= 0;
                Assert.That(tree.Contains(i) == isPresent, $"Contains({i}) should be {isPresent}");
            }

            Assert.That(tree.GetCount(), Is.EqualTo(size));
            tree.Release(allocator);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(4806)]
        public void Subtract_scalar(int size)
        {
            uint offset = 629812341;
            var random = new Random(size);
            uint[] array = new uint[size];
            for (int i = 0; i < size; i++)
                array[i] = (uint)random.Next();

            uint[] subtracted = new uint[array.Length];
            array.CopyTo(subtracted.AsSpan());
            subtracted.AsSpan().SubtractScalar(offset);

            for (int i = 0; i < size; i++)
                Assert.That(subtracted[i], Is.EqualTo(array[i] - offset));
        }

        [Test]
        public void IdStreamReaderInline_supports_partial_reads()
        {
            // Inline2 holds 2 IDs. Reading with a 1-slot buffer must work incrementally.
            // Bug: Math.Min(source.Length, count) always equals count, ignoring target.Length,
            // so CopyTo throws ArgumentException when target is smaller than count.
            DynTree tree = DynTree.Create(10u, 20u);
            Assert.That(tree.TreeType(), Is.EqualTo(DynTreeType.Inline2), "Precondition");

            IIdStreamReader reader = tree.GetStreamReader();
            Span<uint> buf = stackalloc uint[1];

            Assert.That(reader.Read(buf), Is.EqualTo(1));
            Assert.That(buf[0], Is.EqualTo(10u));

            Assert.That(reader.Read(buf), Is.EqualTo(1));
            Assert.That(buf[0], Is.EqualTo(20u));

            Assert.That(reader.Read(buf), Is.EqualTo(0), "Reader should be exhausted");
        }

        [Test]
        public void Add_to_Array16_with_id_above_ushort_max_upgrades_to_Array32()
        {
            // Array16 requires 5+ items all <= 65535
            uint[] initialIds = [0u, 1u, 2u, 3u, 4u];
            DynTree tree = DynTree.Create(allocator, initialIds);
            Assert.That(tree.TreeType(), Is.EqualTo(DynTreeType.Array16), "Precondition: tree must be Array16");

            uint newId = 65536u; // first value that exceeds ushort.MaxValue
            DynTree newTree = tree.Add(allocator, newId);
            tree.Release(allocator);

            Assert.That(newTree.TreeType(), Is.EqualTo(DynTreeType.Array32));
            Assert.That(newTree.Contains(newId), Is.True, $"Tree should contain {newId} after upgrade to Array32");
            Assert.That(newTree.GetCount(), Is.EqualTo((uint)initialIds.Length + 1));

            newTree.Release(allocator);
        }

        [Test]
        public void Array16_upgrades_to_BitSet_when_count_reaches_256()
        {
            // 255 items in [0..254]: count=255 < 256, maxId=254 < 4096 → Array16
            uint[] initialIds = Enumerable.Range(0, 255).Select(i => (uint)i).ToArray();
            DynTree tree = DynTree.Create(allocator, initialIds);
            Assert.That(tree.TreeType(), Is.EqualTo(DynTreeType.Array16), "Precondition: 255 items must be Array16");

            // Adding item 255: count=256, maxId=255 < 4096 → BitSet
            DynTree newTree = tree.Add(allocator, 255u);
            tree.Release(allocator);

            Assert.That(newTree.TreeType(), Is.EqualTo(DynTreeType.BitSet));
            Assert.That(newTree.GetCount(), Is.EqualTo(256u));
            Assert.That(newTree.Contains(255u), Is.True);

            newTree.Release(allocator);
        }

        [Test]
        public void BitSet_downgrades_to_Array16_when_count_drops_below_256()
        {
            // 256 items in [0..255]: count=256, maxId=255 < 4096 → BitSet
            uint[] initialIds = Enumerable.Range(0, 256).Select(i => (uint)i).ToArray();
            DynTree tree = DynTree.Create(allocator, initialIds);
            Assert.That(tree.TreeType(), Is.EqualTo(DynTreeType.BitSet), "Precondition: 256 items must be BitSet");

            // Removing one item: count drops to 255 < 256 → should become Array16
            DynTree newTree = tree.Remove(allocator, 0u);
            tree.Release(allocator);

            Assert.That(newTree.TreeType(), Is.EqualTo(DynTreeType.Array16));
            Assert.That(newTree.GetCount(), Is.EqualTo(255u));
            Assert.That(newTree.Contains(0u), Is.False);
            Assert.That(newTree.Contains(1u), Is.True);

            newTree.Release(allocator);
        }

        [Test]
        public void Add_to_Node_outside_current_range_does_not_double_count()
        {
            // Build a level-0 Node (Width=4096):
            //   needs count > 1024 (else Array) AND maxId >= 4096 (else BitSet)
            //   1024 items [0..1023] + one item at 4096 = 1025 items, maxId=4096
            uint[] initialIds = Enumerable.Range(0, 1024).Select(i => (uint)i).Append(4096u).ToArray();
            DynTree tree = DynTree.Create(allocator, initialIds);
            Assert.That(tree.TreeType(), Is.EqualTo(DynTreeType.Node), "Precondition: tree must be a Node");
            Assert.That(tree.GetCount(), Is.EqualTo(1025u));

            // id=65536 gives childIndex = 65536/4096 = 16 >= CHILDREN(16),
            // which triggers CreateParentAndAdd. this should produce count 1026
            uint newId = 65536u;
            DynTree newTree = tree.Add(allocator, newId);
            tree.Release(allocator);

            Assert.That(newTree.Contains(newId), Is.True);
            Assert.That(newTree.GetCount(), Is.EqualTo(1026u));

            newTree.Release(allocator);
        }

        [TestCase(3597, 6000, SEED)]
        [TestCase(4097, 500000, SEED)]
        [TestCase(16000, 160000, SEED)]
        [TestCase(1000000, 1600000, SEED)]
        public void Add_random_items(int size, int maxId, int? seed = null)
        {
            var random = new Random((seed + size + maxId) ?? Random.Shared.Next());

            HashSet<uint> items = new HashSet<uint>();
            DynTree tree = DynTree.Empty;
            while (tree.GetCount() < size)
            {
                uint id = (uint)random.NextInt64(maxId + 1);
                bool isNew = items.Add(id);
                tree = tree.MakeImmutable();
                bool wasPresent = tree.Contains(id);
                if (isNew && wasPresent)
                    throw new Exception($"{id} should not be in there!");
                DynTree newTree = tree.Add(allocator, id);
                if (tree.Contains(id) != wasPresent)
                    throw new Exception("Immutable!!!");
                int consumption = tree.EstimateMemoryConsumption();
                int percent = (consumption + 9) * 100 / ((int)tree.GetCount() * 4 + 9);
                tree.Release(allocator);
                tree = newTree;
                if (items.Count != tree.GetCount())
                    break;
            }
            Console.WriteLine($"Size {size}: {tree.TreeType()}");

            for (uint i = 0; i < maxId; i++)
            {
                bool isPresent = items.Contains(i);
                Assert.That(tree.Contains(i), Is.EqualTo(isPresent), $"Contains({i}) should be {isPresent}");
            }

            uint[] array = items.ToArray();
            Array.Sort(array);
            Assert.That(tree, Is.EqualTo(array));

            tree.Release(allocator);
        }
        
        [TestCase(3597, 6000, SEED)]
        [TestCase(4097, 500000, SEED)]
        [TestCase(16000, 160000, SEED)]
        [TestCase(1000000, 1600000, SEED)]
        public void Add_and_remove_random_items(int size, int maxId, int? seed = null)
        {
            var random = new Random((seed + size + maxId) ?? Random.Shared.Next());

            HashSet<uint> items = new HashSet<uint>();
            DynTree tree = DynTree.Empty;
            while (tree.GetCount() < size)
            {
                uint id = (uint)random.NextInt64(maxId + 1);
                bool isNew = items.Add(id);
                tree = tree.MakeImmutable();
                bool wasPresent = tree.Contains(id);
                if (isNew && wasPresent)
                    throw new Exception($"{id} should not be in there!");
                DynTree newTree = tree.Add(allocator, id);
                if (tree.Contains(id) != wasPresent)
                    throw new Exception("Immutable!!!");
                int consumption = tree.EstimateMemoryConsumption();
                int percent = (consumption + 9) * 100 / ((int)tree.GetCount() * 4 + 9);
                tree.Release(allocator);
                tree = newTree;
                if (items.Count != tree.GetCount())
                    break;
            }

            uint[] array = items.ToArray();
            Array.Sort(array);
            Assert.That(tree, Is.EqualTo(array));

            int removedCount = 0;
            foreach (uint id in items)
            {
                tree = tree.MakeImmutable();
                bool wasPresent = tree.Contains(id);
                if (!wasPresent)
                    throw new Exception($"{id} was expected to be present!");
                bool removed = tree.TryRemove(allocator, id, out DynTree newTree);
                if (!removed)
                {
                    Console.WriteLine(tree.ToString());
                    throw new Exception("Should have been removed.");
                }

                removedCount++;
                if (!tree.Contains(id))
                    throw new Exception("Should have been immutable (delete).");
                tree.Release(allocator);
                if (newTree.GetCount() != items.Count - removedCount)
                {
                    Console.WriteLine(tree.ToString());
                    Console.WriteLine(newTree.ToString());
                    throw new Exception("Item count mismatch.");
                }
                tree = newTree;
            }

            Assert.That(tree.GetEnumerator().MoveNext(), Is.False);

            Console.WriteLine(tree);
        }

        [TestCase(6000000, 12600000, SEED)]
        public void Create_from_random_items(int size, int maxId, int? seed = null)
        {
            var random = new Random((seed + size + maxId) ?? Random.Shared.Next());
            uint[] items = PickRandom(size, maxId, random);

            Stopwatch w =  Stopwatch.StartNew();
            DynTree tree = DynTree.Create(allocator, items);
            w.Stop();
            Console.WriteLine($"Size {size} created in {w.Elapsed.Milliseconds:0.0}ms.");

            var check = new HashSet<uint>(items);
            w.Restart();
            for (uint i = 0; i < maxId; i++)
            {
                if (tree.Contains(i) != check.Contains(i))
                    throw new Exception($"tree.Contains({i}) failed.");
            }
            w.Stop();
            Console.WriteLine($"Check took {w.Elapsed.Milliseconds:0.0}ms.");
            
            tree.Release(allocator);
        }

        private static uint[] PickRandom(int count, int maxId, Random random)
        {
            int[] collection = Enumerable.Range(0, maxId).ToArray();
            uint[] result = new uint[Math.Min(count, maxId)];
            int remainingItems = collection.Length;
            for (int i = 0; i < result.Length; i++)
            {
                int sourceIndex = random.Next(remainingItems);
                result[i] = (uint)collection[sourceIndex];
                collection[sourceIndex] = collection[--remainingItems];
            }
            Array.Sort(result);
            return result;
        }
    }
}