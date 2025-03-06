using System.Diagnostics;

namespace DynTree.Benchmark
{
    internal class Program
    {
        static void Main(string[] args)
        {
            IAllocator allocator = new DefaultAllocator();
            Console.WriteLine("Hello, World!");

            const int runs = 100;
            Stopwatch write = new Stopwatch();
            Stopwatch read = new Stopwatch();
            for (int run = 0; run < runs; run++)
            {
                var random = new Random(10000);

                const uint maxId = 18900000;
                const uint size  = 500000;
                //HashSet<uint> items = new HashSet<uint>();
                write.Start();
                DynTree tree = DynTree.Empty;
                while (tree.GetCount() < size)
                {
                    uint id = (uint)random.NextInt64(maxId + 1);
                    //items.Add(id);
                    //Console.WriteLine($"Count: {tree.GetCount()}, Adding {id}");
                    DynTree newTree = tree.Add(allocator, id);
                    //int consumption = tree.EstimateMemoryConsumption();
                    //int percent = (consumption + 9) * 100 / ((int)tree.GetCount() * 4 + 9);
                    //Console.WriteLine($"Memory: {consumption} --> {percent}%");
                    tree.Release(allocator);
                    tree = newTree;
                }
                write.Stop();
                //Console.WriteLine(tree);

                read.Start();
                long present = 0;
                for (uint i = 0; i <= maxId; i++)
                {
                    //bool isPresent = items.Contains(i);
                    if (tree.Contains(i))
                        present++;
                }
                read.Stop();

                tree.Release(allocator);

                if (present != tree.GetCount())
                    Console.WriteLine($"Contains() failed.");
            }

            Console.WriteLine($"Avg. write time: {write.Elapsed.TotalMilliseconds / runs:F2}ms");
            Console.WriteLine($"Avg. read time: {read.Elapsed.TotalMilliseconds / runs:F2}ms");
        }
    }
}
