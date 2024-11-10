using Silk.NET.Vulkan;

namespace VMASharp.Defragmentation;

public struct DefragmentationInfo2
{
    public DefragmentationFlags Flags;

    public Allocation[] Allocations;

    public bool[] AllocationsChanged;

    public VulkanMemoryPool[] Pools;

    public ulong MaxCpuBytesToMove;

    public int MaxCpuAllocationsToMove;

    public ulong MaxGpuBytesToMove;

    public int MaxGpuAllocationsToMove;

    public CommandBuffer CommandBuffer;
}
