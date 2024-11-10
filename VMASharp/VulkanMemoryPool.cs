using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Silk.NET.Vulkan;

namespace VMASharp;

[PublicAPI]
public sealed class VulkanMemoryPool : IDisposable
{
    [PublicAPI]
    public VulkanMemoryAllocator Allocator { get; }

    private Vk VkApi => Allocator.VkApi;

    [PublicAPI]
    public string? Name { get; set; }

    internal uint ID { get; }

    internal readonly BlockList BlockList;

    internal VulkanMemoryPool(
        VulkanMemoryAllocator allocator,
        in AllocationPoolCreateInfo poolInfo,
        long preferredBlockSize)
    {
        if (allocator is null)
        {
            throw new ArgumentNullException(nameof(allocator));
        }

        Allocator = allocator;

        ref var tmpRef = ref Unsafe.As<uint, int>(ref allocator.NextPoolId);

        ID = (uint)Interlocked.Increment(ref tmpRef);

        if (ID == 0)
        {
            throw new OverflowException();
        }

        BlockList = new BlockList(
            allocator,
            this,
            poolInfo.MemoryTypeIndex,
            poolInfo.BlockSize != 0 ? poolInfo.BlockSize : preferredBlockSize,
            poolInfo.MinBlockCount,
            poolInfo.MaxBlockCount,
            (poolInfo.Flags & PoolCreateFlags.IgnoreBufferImageGranularity) != 0
                ? 1
                : allocator.BufferImageGranularity,
            poolInfo.FrameInUseCount,
            poolInfo.BlockSize != 0,
            poolInfo.AllocationAlgorithmCreate ?? Helpers.DefaultMetaObjectCreate);

        BlockList.CreateMinBlocks();
    }

    [PublicAPI]
    public void Dispose()
    {
        Allocator.DestroyPool(this);
    }

    [PublicAPI]
    public int MakeAllocationsLost()
    {
        return Allocator.MakePoolAllocationsLost(this);
    }

    [PublicAPI]
    public Result CheckForCorruption()
    {
        return Allocator.CheckPoolCorruption(this);
    }

    [PublicAPI]
    public void GetPoolStats(out PoolStats stats)
    {
        Allocator.GetPoolStats(this, out stats);
    }
}
