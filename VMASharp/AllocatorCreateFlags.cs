using System;

namespace VMASharp;

[Flags]
public enum AllocatorCreateFlags
{
    /// <summary>
    /// Tells the allocator to not use any internal locking, not currently respected
    /// </summary>
    ExternallySyncronized = 0x00000001,

    //KhrDedicatedAllocation = 0x00000002,

    //KhrBindMemory2 = 0x00000004,

    /// <summary>
    /// Enables usage of the VK_EXT_memory_budget extension.
    /// You may set this flag only if you found out that this device extension is supported,
    /// enabled it on the device passed through <see cref="VulkanMemoryAllocatorCreateInfo.LogicalDevice"/>,
    /// and you want it to be used internally by this library.
    /// </summary>
    ExtMemoryBudget = 0x00000008,

    AMDDeviceCoherentMemory = 0x00000010,

    BufferDeviceAddress = 0x00000020,
}
