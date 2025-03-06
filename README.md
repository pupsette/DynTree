# What is DynTree
It's a data structure for maintaining an ordered set of unsigned 32 bit integers. The key features:

 * Unmanaged memory -- The DynTree operates on unmanaged memory. Many and/or large trees will not have any impact on GC.
 * Immutability -- You may mark a DynTree as immutable. Subsequent modifications (inserts/removals) will copy affected parts of the tree, unmodified parts will just be referenced.
 * Ref-counting --  Users need to take care of releasing trees properly
 * Efficiency -- The tree structure adapts to the actual density and size, supporting different representations of individual tree leaves (e.g. a bitset)
