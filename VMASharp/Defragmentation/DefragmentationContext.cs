using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;

namespace VMASharp.Defragmentation;

public sealed class DefragmentationContext : IDisposable
{
    private readonly VulkanMemoryAllocator allocator;
    private readonly uint currentFrame;
    private readonly uint flags;
    private DefragmentationStats stats;

    private ulong maxCpuBytesToMove, maxGpuBytesToMove;
    private int maxCpuAllocationsToMove, maxGpuAllocationsToMove;

    private readonly BlockListDefragmentationContext[] defaultPoolContexts =
        new BlockListDefragmentationContext[Vk.MaxMemoryTypes];

    private readonly List<BlockListDefragmentationContext> customPoolContexts = new();

    internal DefragmentationContext(
        VulkanMemoryAllocator allocator,
        uint currentFrame,
        uint flags,
        DefragmentationStats stats)
    {
        throw new NotImplementedException();
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }

    internal void AddPools(params VulkanMemoryPool[] pools)
    {
        throw new NotImplementedException();
    }

    internal void AddAllocations(Allocation[] allocations, out bool[] allocationsChanged)
    {
        throw new NotImplementedException();
    }

    internal Result Defragment(
        ulong maxCpuBytesToMove,
        int maxCpuAllocationsToMove,
        ulong maxGpuBytesToMove,
        int maxGpuAllocationsToMove,
        CommandBuffer cbuffer,
        DefragmentationStats stats,
        DefragmentationFlags flags)
    {
        throw new NotImplementedException();
    }

    internal Result DefragmentationPassBegin(ref DefragmentationPassMoveInfo[] info)
    {
        throw new NotImplementedException();
    }

    internal Result DefragmentationPassEnd()
    {
        throw new NotImplementedException();
    }
}
