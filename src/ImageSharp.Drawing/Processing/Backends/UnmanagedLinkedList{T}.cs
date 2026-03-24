// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Represents an append-oriented linked list backed by native memory.
/// </summary>
/// <typeparam name="T">The unmanaged value type stored by the list.</typeparam>
/// <remarks>
/// <para>
/// Nodes are allocated from native-memory blocks and linked together in insertion order.
/// </para>
/// <para>
/// The list is intended for short-lived backend build paths where append-only insertion,
/// deterministic disposal, and zero GC pressure for node storage matter more than random access.
/// </para>
/// </remarks>
internal sealed unsafe class UnmanagedLinkedList<T> : IDisposable
    where T : unmanaged
{
    private const int DefaultBlockCapacity = 64;

    // Report only larger retained native growth to the GC so we avoid adding
    // pressure bookkeeping to every small block allocation on this hot path.
    private const long MemoryPressureThresholdBytes = 16 * 1024;

    private readonly int initialBlockCapacity;
    private Block* firstBlock;
    private Block* currentBlock;
    private Node* head;
    private Node* tail;
    private long allocatedBytes;
    private long memoryPressureBytes;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnmanagedLinkedList{T}"/> class.
    /// </summary>
    /// <param name="initialBlockCapacity">The initial native node-block capacity.</param>
    public UnmanagedLinkedList(int initialBlockCapacity = DefaultBlockCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialBlockCapacity);

        this.initialBlockCapacity = initialBlockCapacity;
        this.firstBlock = null;
        this.currentBlock = null;
        this.head = null;
        this.tail = null;
        this.allocatedBytes = 0;
        this.memoryPressureBytes = 0;
        this.Count = 0;
        this.disposed = false;
    }

    /// <summary>
    /// Gets the number of nodes stored in the list.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the list contains no nodes.
    /// </summary>
    public bool IsEmpty => this.Count == 0;

    /// <summary>
    /// Appends one value to the end of the list.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void AddLast(T value)
    {
        this.ThrowIfDisposed();

        Node* node = this.AllocateNode();
        node->Value = value;
        node->Next = null;

        if (this.head is null)
        {
            this.head = node;
        }
        else
        {
            this.tail->Next = node;
        }

        this.tail = node;
        this.Count++;
    }

    /// <summary>
    /// Removes all nodes from the list while retaining the currently allocated native blocks.
    /// </summary>
    public void Clear()
    {
        this.ThrowIfDisposed();

        for (Block* block = this.firstBlock; block is not null; block = block->Next)
        {
            block->Count = 0;
        }

        this.currentBlock = this.firstBlock;
        this.head = null;
        this.tail = null;
        this.Count = 0;
    }

    /// <summary>
    /// Gets an enumerator over the list nodes in insertion order.
    /// </summary>
    /// <returns>An enumerator over the stored values.</returns>
    public Enumerator GetEnumerator()
    {
        this.ThrowIfDisposed();
        return new Enumerator(this.head);
    }

    /// <summary>
    /// Releases all native memory owned by the list.
    /// </summary>
    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        Block* block = this.firstBlock;
        while (block is not null)
        {
            Block* next = block->Next;
            NativeMemory.Free(block->Nodes);
            NativeMemory.Free(block);
            block = next;
        }

        this.firstBlock = null;
        this.currentBlock = null;
        this.head = null;
        this.tail = null;
        this.Count = 0;

        if (this.memoryPressureBytes != 0)
        {
            // Remove only the pressure we actually reported rather than the full
            // native byte count, because small allocations below the threshold
            // are intentionally left unreported.
            GC.RemoveMemoryPressure(this.memoryPressureBytes);
            this.memoryPressureBytes = 0;
        }

        this.allocatedBytes = 0;
        this.disposed = true;
    }

    /// <summary>
    /// Allocates one node from the current native block, growing the block chain when needed.
    /// </summary>
    /// <returns>A pointer to writable node storage.</returns>
    private Node* AllocateNode()
    {
        if (this.currentBlock is null || this.currentBlock->Count >= this.currentBlock->Capacity)
        {
            int nextCapacity = this.currentBlock is null
                ? this.initialBlockCapacity
                : this.currentBlock->Capacity * 2;

            this.AllocateBlock(nextCapacity);
        }

        Node* node = this.currentBlock->Nodes + this.currentBlock->Count;
        this.currentBlock->Count++;
        return node;
    }

    /// <summary>
    /// Allocates and appends a new native node block.
    /// </summary>
    /// <param name="capacity">The number of nodes the new block can hold.</param>
    private void AllocateBlock(int capacity)
    {
        Block* block = (Block*)NativeMemory.Alloc((nuint)sizeof(Block));
        block->Capacity = capacity;
        block->Count = 0;
        block->Next = null;
        block->Nodes = (Node*)NativeMemory.Alloc((nuint)capacity, GetNodeSize());
        this.TrackAllocatedBytes(sizeof(Block) + (capacity * (long)GetNodeSize()));

        if (this.firstBlock is null)
        {
            this.firstBlock = block;
        }
        else
        {
            this.currentBlock->Next = block;
        }

        this.currentBlock = block;
    }

    /// <summary>
    /// Tracks cumulative native memory and reports larger retained growth to the GC.
    /// </summary>
    /// <param name="bytes">The newly allocated native byte count.</param>
    private void TrackAllocatedBytes(long bytes)
    {
        this.allocatedBytes += bytes;
        long delta = this.allocatedBytes - this.memoryPressureBytes;

        if (delta >= MemoryPressureThresholdBytes)
        {
            // The list can retain native blocks across Clear(), so once the
            // retained unmanaged footprint grows beyond the threshold we report
            // the newly accumulated delta to help the GC account for it.
            GC.AddMemoryPressure(delta);
            this.memoryPressureBytes += delta;
        }
    }

    /// <summary>
    /// Throws when the list has already been disposed.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ThrowIfDisposed()
    {
        if (this.disposed)
        {
            ThrowObjectDisposed();
        }
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/>.
    /// </summary>
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowObjectDisposed() => throw new ObjectDisposedException(nameof(UnmanagedLinkedList<T>));

    /// <summary>
    /// Gets the unmanaged size of one linked-list node.
    /// </summary>
    /// <returns>The unmanaged size of <see cref="Node"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint GetNodeSize() => (nuint)sizeof(Node);

    /// <summary>
    /// Enumerates native linked-list values in insertion order.
    /// </summary>
    public ref struct Enumerator
    {
        private Node* current;
        private Node* next;

        /// <summary>
        /// Initializes a new instance of the <see cref="Enumerator"/> struct.
        /// </summary>
        /// <param name="head">The first node in the list.</param>
        internal Enumerator(Node* head)
        {
            this.current = null;
            this.next = head;
        }

        /// <summary>
        /// Gets the current value.
        /// </summary>
        public T Current => this.current->Value;

        /// <summary>
        /// Advances to the next value in the list.
        /// </summary>
        /// <returns><see langword="true"/> if a value was produced; otherwise <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            if (this.next is null)
            {
                return false;
            }

            this.current = this.next;
            this.next = this.next->Next;
            return true;
        }
    }

    /// <summary>
    /// Represents one native node block in the linked-list allocator.
    /// </summary>
    private struct Block
    {
        /// <summary>
        /// Gets or sets the node capacity of this block.
        /// </summary>
        public int Capacity;

        /// <summary>
        /// Gets or sets the number of allocated nodes in this block.
        /// </summary>
        public int Count;

        /// <summary>
        /// Gets or sets the pointer to the next block.
        /// </summary>
        public Block* Next;

        /// <summary>
        /// Gets or sets the contiguous native node storage for this block.
        /// </summary>
        public Node* Nodes;
    }

    /// <summary>
    /// Represents one native linked-list node.
    /// </summary>
    internal struct Node
    {
        /// <summary>
        /// Gets or sets the stored value.
        /// </summary>
        public T Value;

        /// <summary>
        /// Gets or sets the next node pointer.
        /// </summary>
        public Node* Next;
    }
}
