using System.Diagnostics;

namespace DynTree.Tests
{
    [TestFixture]
    public class DynTreeTests
    {
        private StreamWriter log = new StreamWriter("log.txt");

        private static readonly TrackingAllocator allocator = new TrackingAllocator();
        private const int SEED = 8879123;

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            log.Dispose();
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
                log.WriteLine($"Count: {tree.GetCount()}, Adding {id}");
                tree = tree.MakeImmutable();
                bool wasPresent = tree.Contains(id);
                if (isNew && wasPresent)
                    throw new Exception($"{id} should not be in there!");
                DynTree newTree = tree.Add(allocator, id);
                if (tree.Contains(id) != wasPresent)
                    throw new Exception("Immutable!!!");
                int consumption = tree.EstimateMemoryConsumption();
                int percent = (consumption + 9) * 100 / ((int)tree.GetCount() * 4 + 9);
                log.WriteLine($"Memory: {consumption} --> {percent}%");
                tree.Release(allocator);
                log.WriteLine("--------------------");
                tree = newTree;
                if (items.Count != tree.GetCount())
                {
                    log.WriteLine("Count mismatch.");
                    break;
                }
            }
            Console.WriteLine(tree);
            Console.WriteLine($"Size {size}: {tree.TreeType()}");

            for (uint i = 0; i < maxId; i++)
            {
                bool isPresent = items.Contains(i);
                Assert.That(tree.Contains(i), Is.EqualTo(isPresent), $"Contains({i}) should be {isPresent}");
            }
            
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
                log.WriteLine($"Count: {tree.GetCount()}, Adding {id}");
                tree = tree.MakeImmutable();
                bool wasPresent = tree.Contains(id);
                if (isNew && wasPresent)
                    throw new Exception($"{id} should not be in there!");
                DynTree newTree = tree.Add(allocator, id);
                if (tree.Contains(id) != wasPresent)
                    throw new Exception("Immutable!!!");
                int consumption = tree.EstimateMemoryConsumption();
                int percent = (consumption + 9) * 100 / ((int)tree.GetCount() * 4 + 9);
                log.WriteLine($"Memory: {consumption} --> {percent}%");
                tree.Release(allocator);
                log.WriteLine("--------------------");
                tree = newTree;
                if (items.Count != tree.GetCount())
                {
                    log.WriteLine("Count mismatch.");
                    break;
                }
            }

            int removedCount = 0;
            foreach (uint id in items)
            {
                Console.WriteLine($"Removing {id}");
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