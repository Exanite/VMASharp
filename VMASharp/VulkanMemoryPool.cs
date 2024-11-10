using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Silk.NET.Vulkan;

namespace VMASharp;

[PublicApi]
public sealed class VulkanMemoryPool : IDisposable
{
    [PublicApi]
    public VulkanMemoryAllocator Allocator { get; }

    private Vk VkApi => Allocator.VkApi;

    [PublicApi]
    public string? Name { get; set; }

    internal uint Id { get; }

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

        Id = (uint)Interlocked.Increment(ref tmpRef);

        if (Id == 0)
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

    [PublicApi]
    public void Dispose()
    {
        Allocator.DestroyPool(this);
    }

    [PublicApi]
    public int MakeAllocationsLost()
    {
        return Allocator.MakePoolAllocationsLost(this);
    }

    [PublicApi]
    public Result CheckForCorruption()
    {
        return Allocator.CheckPoolCorruption(this);
    }

    [PublicApi]
    public void GetPoolStats(out PoolStats stats)
    {
        Allocator.GetPoolStats(this, out stats);
    }
}
