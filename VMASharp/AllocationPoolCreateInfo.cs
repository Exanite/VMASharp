using System;
using VMASharp.Metadata;

namespace VMASharp;

public struct AllocationPoolCreateInfo
{
    /// <summary>
    /// Memory type index to allocate from, non-optional
    /// </summary>
    public int MemoryTypeIndex;

    public PoolCreateFlags Flags;

    public long BlockSize;

    public int MinBlockCount;

    public int MaxBlockCount;

    public int FrameInUseCount;

    public Func<long, IBlockMetadata>? AllocationAlgorithmCreate;

    public AllocationPoolCreateInfo(
        int memoryTypeIndex,
        PoolCreateFlags flags = 0,
        long blockSize = 0,
        int minBlockCount = 0,
        int maxBlockCount = 0,
        int frameInUseCount = 0,
        Func<long, IBlockMetadata>? allocationAlgorithemCreate = null)
    {
        MemoryTypeIndex = memoryTypeIndex;
        Flags = flags;
        BlockSize = blockSize;
        MinBlockCount = minBlockCount;
        MaxBlockCount = maxBlockCount;
        FrameInUseCount = frameInUseCount;
        AllocationAlgorithmCreate = allocationAlgorithemCreate;
    }
}
