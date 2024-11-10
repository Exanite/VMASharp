using System;
using System.Diagnostics;
using Silk.NET.Vulkan;
using VMASharp.Metadata;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VMASharp;

internal class VulkanMemoryBlock : IDisposable
{
    private Vk VkApi => allocator.VkApi;

    private readonly VulkanMemoryAllocator allocator;
    internal readonly IBlockMetadata MetaData;
    private readonly object syncLock = new();
    private int mapCount;


    public VulkanMemoryBlock(
        VulkanMemoryAllocator allocator,
        VulkanMemoryPool? pool,
        int memoryTypeIndex,
        DeviceMemory memory,
        uint id,
        IBlockMetadata metaObject)
    {
        this.allocator = allocator;
        ParentPool = pool;
        MemoryTypeIndex = memoryTypeIndex;
        DeviceMemory = memory;
        Id = id;

        MetaData = metaObject;
    }

    public VulkanMemoryPool? ParentPool { get; }

    public DeviceMemory DeviceMemory { get; }

    public int MemoryTypeIndex { get; }

    public uint Id { get; }

    public IntPtr MappedData { get; private set; }

    public void Dispose()
    {
        if (!MetaData.IsEmpty)
        {
            throw new InvalidOperationException(
                "Some allocations were not freed before destruction of this memory block!");
        }

        Debug.Assert(DeviceMemory.Handle != default);

        allocator.FreeVulkanMemory(MemoryTypeIndex, MetaData.Size, DeviceMemory);
    }

    [Conditional("DEBUG")]
    public void Validate()
    {
        Helpers.Validate(DeviceMemory.Handle != default && MetaData.Size > 0);

        MetaData.Validate();
    }

    public void CheckCorruption(VulkanMemoryAllocator allocator)
    {
        var data = Map(1);

        try
        {
            MetaData.CheckCorruption((nuint)data);
        }
        finally
        {
            Unmap(1);
        }
    }

    public unsafe IntPtr Map(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        lock (syncLock)
        {
            Debug.Assert(mapCount >= 0);

            if (mapCount > 0)
            {
                Debug.Assert(MappedData != default);

                mapCount += count;

                return MappedData;
            }

            if (count == 0)
            {
                return default;
            }

            IntPtr pData;
            var res = VkApi.MapMemory(allocator.Device, DeviceMemory, 0, Vk.WholeSize, 0,
                (void**)&pData);

            if (res != Result.Success)
            {
                throw new MapMemoryException(res);
            }

            mapCount = count;
            MappedData = pData;

            return pData;
        }
    }

    public void Unmap(int count)
    {
        if (count == 0)
        {
            return;
        }

        lock (syncLock)
        {
            var newCount = mapCount - count;

            if (newCount < 0)
            {
                throw new InvalidOperationException(
                    "Memory block is being unmapped while it was not previously mapped");
            }

            mapCount = newCount;

            if (newCount == 0)
            {
                MappedData = default;
                VkApi.UnmapMemory(allocator.Device, DeviceMemory);
            }
        }
    }

    public unsafe Result BindBufferMemory(
        Allocation allocation,
        long allocationLocalOffset,
        Buffer buffer,
        void* pNext)
    {
        Debug.Assert(allocation is BlockAllocation blockAlloc && blockAlloc.Block == this);

        Debug.Assert((ulong)allocationLocalOffset < (ulong)allocation.Size,
            "Invalid allocationLocalOffset. Did you forget that this offset is relative to the beginning of the allocation, not the whole memory block?");

        var memoryOffset = allocationLocalOffset + allocation.Offset;

        lock (syncLock)
        {
            return allocator.BindVulkanBuffer(buffer, DeviceMemory, memoryOffset, pNext);
        }
    }

    public unsafe Result BindImageMemory(
        Allocation allocation,
        long allocationLocalOffset,
        Image image,
        void* pNext)
    {
        Debug.Assert(allocation is BlockAllocation blockAlloc && blockAlloc.Block == this);

        Debug.Assert((ulong)allocationLocalOffset < (ulong)allocation.Size,
            "Invalid allocationLocalOffset. Did you forget that this offset is relative to the beginning of the allocation, not the whole memory block?");

        var memoryOffset = allocationLocalOffset + allocation.Offset;

        lock (syncLock)
        {
            return allocator.BindVulkanImage(image, DeviceMemory, memoryOffset, pNext);
        }
    }
}
