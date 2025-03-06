# What is DynTree
It's a data structure for maintaining an ordered set of unsigned 32 bit integers. The key features:

 * Unmanaged memory -- The DynTree operates on unmanaged memory. Many and/or large trees will not have any impact on GC.
 * Immutability -- You may mark a DynTree as immutable. Subsequent modifications (inserts/removals) will copy affected parts of the tree, unmodified parts will just be referenced.
 * Ref-counting --  Users need to take care of releasing trees properly
 * Efficiency -- The tree structure adapts to the actual density and size, supporting different representations of individual tree leaves (e.g. a bitset)
 * 9-byte struct -- The tree structure itself is a struct with a size of 9 bytes. This allows other data structures to include a large amount of DynTree instances.

# Create a DynTree
```
// The default allocator uses Marshal.AllocHGlobal() and
//  Marshal.FreeHGlobal to allocate/free unmanaged memory
IAllocator allocator = new DefaultAllocator();

// Create a tree from a pre-defined set. The integers must be in ascending order.
DynTree tree = DynTree.Create(allocator, [90, 112]);

// Do something with the tree

// Free unmanaged memory. A reference to the allocator is not part of the tree struct.
tree.Release(allocator);
```

# Modify a DynTree
```
IAllocator allocator = new DefaultAllocator();

// Start with an empty tree.
DynTree tree = DynTree.Empty;

// Add some integers
foreach (uint id in [112, 90])
{
  // Adding an integer creates a new tree, which must be released by the caller.
  DynTree newTree = tree.Add(allocator, id);
  tree.Release(allocator);
  tree = newTree;
}

// Remove some integers
foreach (uint id in [112, 90])
{
  // Removing an integer creates a new tree, too.
  DynTree newTree = tree.Remove(allocator, id);
  tree.Release(allocator);
  tree = newTree;
}

// Free unmanaged memory.
tree.Release(allocator);
```

# Query a DynTree
```
IAllocator allocator = new DefaultAllocator();

DynTree tree = DynTree.Create(allocator, [16, 18, 90, 112]);

tree.Contains(90); // true
tree.Contains(19); // false

tree.Release(allocator);
```

# Make a DynTree immutable
```
IAllocator allocator = new DefaultAllocator();

DynTree tree = DynTree.Create(allocator, [16, 18, 90, 112]);

// The returned tree is not a new instance, it's just flagged as immutable.
tree = tree.MakeImmutable();

// Add an integer.
DynTree newTree = tree.Add(allocator, 20);

newTree.Contains(20); // true
tree.Contains(20); // false

newTree.Release(allocator);
tree.Release(allocator);
```
