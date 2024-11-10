using System;
using System.Diagnostics;
using Silk.NET.Vulkan;

namespace VMASharp;

internal class DedicatedAllocation : Allocation
{
    private readonly DeviceMemory memory;
    private IntPtr mappedData;

    public DedicatedAllocation(
        VulkanMemoryAllocator allocator,
        int memTypeIndex,
        DeviceMemory memory,
        SuballocationType suballocType,
        IntPtr mappedData,
        long size) : base(allocator, 0)
    {
        this.memory = memory;
        this.mappedData = mappedData;
        MemoryTypeIndex = memTypeIndex;
    }

    public override DeviceMemory DeviceMemory => memory;

    public override long Offset
    {
        get => 0;
        internal set => throw new InvalidOperationException();
    }

    public override IntPtr MappedData => MapCount != 0 ? mappedData : default;

    internal override bool CanBecomeLost => false;

    internal unsafe Result DedicatedAllocMap(out IntPtr pData)
    {
        if (MapCount != 0)
        {
            if ((MapCount & int.MaxValue) >= int.MaxValue)
            {
                throw new InvalidOperationException("Dedicated allocation mapped too many times simultaneously");
            }

            Debug.Assert(mappedData != default);

            pData = mappedData;
            MapCount += 1;

            return Result.Success;
        }

        pData = default;

        IntPtr tmp;
        var res = VkApi.MapMemory(Allocator.Device, memory, 0, Vk.WholeSize, 0, (void**)&tmp);

        if (res == Result.Success)
        {
            mappedData = tmp;
            MapCount = 1;
            pData = tmp;
        }

        return res;
    }

    internal void DedicatedAllocUnmap()
    {
        if ((MapCount & int.MaxValue) != 0)
        {
            MapCount -= 1;

            if (MapCount == 0)
            {
                mappedData = default;
                VkApi.UnmapMemory(Allocator.Device, memory);
            }
        }
        else
        {
            throw new InvalidOperationException("Unmapping dedicated allocation not previously mapped");
        }
    }

    public void CalcStatsInfo(out StatInfo stats)
    {
        StatInfo.Init(out stats);
        stats.BlockCount = 1;
        stats.AllocationCount = 1;
        stats.UsedBytes = Size;
        stats.AllocationSizeMin = stats.AllocationSizeMax = Size;
    }

    public override IntPtr Map()
    {
        var res = DedicatedAllocMap(out var pData);

        if (res != Result.Success)
        {
            throw new MapMemoryException(res);
        }

        return pData;
    }

    public override void Unmap()
    {
        DedicatedAllocUnmap();
    }
}
