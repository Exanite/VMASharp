using System;
using System.Diagnostics;
using Silk.NET.Vulkan;

namespace VMASharp;

public sealed class BlockAllocation : Allocation
{
    internal VulkanMemoryBlock Block = null!;
    internal SuballocationType SuballocationType;
    private bool _canBecomeLost;

    internal BlockAllocation(VulkanMemoryAllocator allocator, int currentFrameIndex) : base(allocator,
        currentFrameIndex) {}

    public override DeviceMemory DeviceMemory => Block.DeviceMemory;

    public override long Offset { get; internal set; }

    public override IntPtr MappedData
    {
        get
        {
            if (MapCount != 0)
            {
                var mapdata = Block.MappedData;

                Debug.Assert(mapdata != default);

                return new IntPtr(mapdata.ToInt64() + Offset);
            }

            return default;
        }
    }

    internal override bool CanBecomeLost => _canBecomeLost;

    internal void InitBlockAllocation(
        VulkanMemoryBlock block,
        long offset,
        long alignment,
        long size,
        int memoryTypeIndex,
        SuballocationType subType,
        bool mapped,
        bool canBecomeLost)
    {
        Block = block;
        Offset = offset;
        Alignment = alignment;
        Size = size;
        MemoryTypeIndex = memoryTypeIndex;
        MapCount = mapped ? int.MinValue : 0;
        SuballocationType = subType;
        _canBecomeLost = canBecomeLost;
    }

    internal void ChangeAllocation(VulkanMemoryBlock block, long offset)
    {
        Debug.Assert(block != null! && offset >= 0);

        if (!ReferenceEquals(block, Block))
        {
            var mapRefCount = MapCount & int.MaxValue;

            if (IsPersistantMapped)
            {
                mapRefCount += 1;
            }

            Block.Unmap(mapRefCount);
            block.Map(mapRefCount);

            Block = block;
        }

        Offset = offset;
    }

    private void BlockAllocMap()
    {
        if ((MapCount & int.MaxValue) < int.MaxValue)
        {
            MapCount += 1;
        }
        else
        {
            throw new InvalidOperationException("Allocation mapped too many times simultaniously");
        }
    }

    private void BlockAllocUnmap()
    {
        if ((MapCount & int.MaxValue) > 0)
        {
            MapCount -= 1;
        }
        else
        {
            throw new InvalidOperationException("Unmapping allocation not previously mapped");
        }
    }

    public override IntPtr Map()
    {
        if (CanBecomeLost)
        {
            throw new InvalidOperationException("Cannot map an allocation that can become lost");
        }

        var data = Block.Map(1);

        data = new IntPtr(data.ToInt64() + Offset);

        BlockAllocMap();

        return data;
    }

    public override void Unmap()
    {
        BlockAllocUnmap();
        Block.Unmap(1);
    }
}
