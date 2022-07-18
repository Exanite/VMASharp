#pragma warning disable CA1063

using System.Diagnostics;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;
using System.Buffers;

namespace VMASharp;

/// <summary>
/// The object containing details on a suballocation of Vulkan Memory
/// </summary>
public abstract unsafe class Allocation : IDisposable
{
    internal VulkanMemoryAllocator Allocator { get; }

    protected Vk VkApi => Allocator.VkApi;

    private   long _size;
    protected int  MapCount;
    private   bool _lostOrDisposed;
    private   int  _lastUseFrameIndex;

    /// <summary>
    /// Size of this allocation, in bytes.
    /// Value never changes, unless allocation is lost.
    /// </summary>
    public long Size {
        get {
            if (_lostOrDisposed || _lastUseFrameIndex == Helpers.FrameIndexLost) {
                return 0;
            }

            return _size;
        }
        protected set => _size = value;
    }

    /// <summary>
    /// Memory type index that this allocation is from. Value does not change.
    /// </summary>
    public int MemoryTypeIndex { get; protected set; }

    /// <summary>
    /// Handle to Vulkan memory object.
    /// Same memory object can be shared by multiple allocations.
    /// It can change after call to vmaDefragment() if this allocation is passed to the function, or if allocation is lost.
    /// If the allocation is lost, it is equal to `VK_NULL_HANDLE`.
    /// </summary>
    public abstract DeviceMemory DeviceMemory { get; }

    /// <summary>
    /// Offset into deviceMemory object to the beginning of this allocation, in bytes. (deviceMemory, offset) pair is unique to this allocation.
    /// It can change after call to vmaDefragment() if this allocation is passed to the function, or if allocation is lost.
    /// </summary>
    public abstract long Offset { get; internal set; }

    internal abstract bool CanBecomeLost { get; }


    internal bool IsPersistantMapped => MapCount < 0;

    internal int LastUseFrameIndex => _lastUseFrameIndex;

    protected internal long Alignment { get; protected set; }

    public object? UserData { get; set; }

    internal Allocation(VulkanMemoryAllocator allocator, int currentFrameIndex) {
        Allocator = allocator;
        _lastUseFrameIndex = LastUseFrameIndex;
        _lastUseFrameIndex = currentFrameIndex;
    }

    /// <summary>
    /// If this allocation is mapped, returns a pointer to the mapped memory region. Returns Null otherwise.
    /// </summary>
    public abstract IntPtr MappedData { get; }

    public void Dispose() {
        GC.SuppressFinalize(this);
        if (!_lostOrDisposed) {
            Allocator.FreeMemory(this);
            _lostOrDisposed = true;
        }
    }

    public Result BindBufferMemory(Buffer buffer) {
        Debug.Assert(Offset >= 0);

        return Allocator.BindVulkanBuffer(buffer, DeviceMemory, Offset, null);
    }

    public Result BindBufferMemory(Buffer buffer, long allocationLocalOffset, IntPtr pNext) =>
        BindBufferMemory(buffer, allocationLocalOffset, (void*)pNext);

    public Result BindBufferMemory(Buffer buffer, long allocationLocalOffset, void* pNext = null) {
        if ((ulong)allocationLocalOffset >= (ulong)Size) {
            throw new ArgumentOutOfRangeException(nameof(allocationLocalOffset));
        }

        return Allocator.BindVulkanBuffer(buffer, DeviceMemory, Offset + allocationLocalOffset,
            pNext);
    }

    public Result BindImageMemory(Image image) => Allocator.BindVulkanImage(image, DeviceMemory, Offset, null);

    public Result BindImageMemory(Image image, long allocationLocalOffset, IntPtr pNext) => BindImageMemory(image, allocationLocalOffset, (void*)pNext);

    public Result BindImageMemory(Image image, long allocationLocalOffset, void* pNext = null) {
        if ((ulong)allocationLocalOffset >= (ulong)Size) {
            throw new ArgumentOutOfRangeException(nameof(allocationLocalOffset));
        }

        return Allocator.BindVulkanImage(image, DeviceMemory, Offset + allocationLocalOffset, pNext);
    }

    internal bool MakeLost(int currentFrame, int frameInUseCount) {
        if (!CanBecomeLost) {
            throw new InvalidOperationException(
                "Internal Exception, tried to make an allocation lost that cannot become lost.");
        }

        int localLastUseFrameIndex = _lastUseFrameIndex;

        while (true) {
            if (localLastUseFrameIndex == Helpers.FrameIndexLost) {
                Debug.Assert(false);
                return false;
            }

            if (localLastUseFrameIndex + frameInUseCount >= currentFrame) {
                return false;
            }
            int tmp = Interlocked.CompareExchange(ref _lastUseFrameIndex, Helpers.FrameIndexLost,
                localLastUseFrameIndex);

            if (tmp == localLastUseFrameIndex) {
                _lostOrDisposed = true;
                return true;
            }

            localLastUseFrameIndex = tmp;
        }
    }

    public bool TouchAllocation() {
        if (_lostOrDisposed) {
            return false;
        }

        int currFrameIndexLoc = Allocator.CurrentFrameIndex;
        int lastUseFrameIndexLoc = _lastUseFrameIndex;

        if (CanBecomeLost) {
            while (true) {
                if (lastUseFrameIndexLoc == Helpers.FrameIndexLost) {
                    return false;
                } else if (lastUseFrameIndexLoc == currFrameIndexLoc) {
                    return true;
                }

                lastUseFrameIndexLoc = Interlocked.CompareExchange(ref _lastUseFrameIndex, currFrameIndexLoc,
                    lastUseFrameIndexLoc);
            }
        } else {
            while (true) {
                Debug.Assert(lastUseFrameIndexLoc != Helpers.FrameIndexLost);

                if (lastUseFrameIndexLoc == currFrameIndexLoc)
                    break;

                lastUseFrameIndexLoc = Interlocked.CompareExchange(ref _lastUseFrameIndex, currFrameIndexLoc,
                    lastUseFrameIndexLoc);
            }

            return true;
        }
    }

    /// <summary>
    /// Flushes a specified region of memory
    /// </summary>
    /// <param name="offset">Offset in this allocation</param>
    /// <param name="size">Size of region to flush</param>
    /// <returns>The result of the operation</returns>
    public Result Flush(long offset, long size) => Allocator.FlushOrInvalidateAllocation(this, offset, size, CacheOperation.Flush);

    /// <summary>
    /// Invalidates a specified region of memory
    /// </summary>
    /// <param name="offset">Offset in this allocation</param>
    /// <param name="size">Size of region to Invalidate</param>
    /// <returns>The result of the operation</returns>
    public Result Invalidate(long offset, long size) => Allocator.FlushOrInvalidateAllocation(this, offset, size, CacheOperation.Invalidate);

    public abstract IntPtr Map();

    public abstract void Unmap();

    public bool TryGetMemory<T>(out Memory<T> memory)
        where T : unmanaged {
        if (MapCount != 0) {
            int size = checked((int)Size);

            if (size >= sizeof(T)) {
                memory = new UnmanagedMemoryManager<T>((byte*)MappedData, size / sizeof(T)).Memory;

                return true;
            }
        }

        memory = Memory<T>.Empty;
        return false;
    }

    public bool TryGetSpan<T>(out Span<T> span)
        where T : unmanaged {
        if (MapCount != 0) {
            int size = checked((int)Size);

            if (size >= sizeof(T)) {
                span = new Span<T>((void*)MappedData, size / sizeof(T));

                return true;
            }
        }

        span = Span<T>.Empty;
        return false;
    }

    private sealed class UnmanagedMemoryManager<T> : MemoryManager<T>
        where T : unmanaged
    {
        private readonly T*  _pointer;
        private readonly int _elementCount;

        public UnmanagedMemoryManager(void* ptr, int elemCount) {
            _pointer = (T*)ptr;
            _elementCount = elemCount;
        }

        protected override void Dispose(bool disposing) { }

        public override Span<T> GetSpan() => new(_pointer, _elementCount);

        public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);

        public override void Unpin() { }
    }
}