using Silk.NET.Vulkan;

#pragma warning disable CA1815

namespace VMASharp;

public struct VulkanMemoryAllocatorCreateInfo
{
    /// <summary>
    /// Flags for created allocator
    /// </summary>
    public AllocatorCreateFlags Flags;

    public Version32 VulkanApiVersion;

    public Vk VulkanApiObject;

    public Instance Instance;

    public PhysicalDevice PhysicalDevice;

    public Device LogicalDevice;

    public long PreferredLargeHeapBlockSize;

    public long[]? HeapSizeLimits;

    public int FrameInUseCount;

    public VulkanMemoryAllocatorCreateInfo(
        Version32 vulkanApiVersion,
        Vk vulkanApiObject,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device logicalDevice,
        AllocatorCreateFlags flags = default,
        long preferredLargeHeapBlockSize = 0,
        long[]? heapSizeLimits = null,
        int frameInUseCount = 0)
    {
        Flags = flags;
        VulkanApiVersion = vulkanApiVersion;
        VulkanApiObject = vulkanApiObject;
        Instance = instance;
        PhysicalDevice = physicalDevice;
        LogicalDevice = logicalDevice;
        PreferredLargeHeapBlockSize = preferredLargeHeapBlockSize;
        HeapSizeLimits = heapSizeLimits;
        FrameInUseCount = frameInUseCount;
    }
}
