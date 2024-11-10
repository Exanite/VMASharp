using Silk.NET.Vulkan;

namespace VMASharp;

public struct AllocationCreateInfo
{
    public AllocationCreateFlags Flags;

    public AllocationStrategyFlags Strategy;

    public MemoryUsage Usage;

    public MemoryPropertyFlags RequiredFlags;

    public MemoryPropertyFlags PreferredFlags;

    public uint MemoryTypeBits;

    public VulkanMemoryPool? Pool;

    public object? UserData;

    public AllocationCreateInfo(
        AllocationCreateFlags flags = default,
        AllocationStrategyFlags strategy = default,
        MemoryUsage usage = default,
        MemoryPropertyFlags requiredFlags = default,
        MemoryPropertyFlags preferredFlags = default,
        uint memoryTypeBits = 0,
        VulkanMemoryPool? pool = null,
        object? userData = null)
    {
        Flags = flags;
        Strategy = strategy;
        Usage = usage;
        RequiredFlags = requiredFlags;
        PreferredFlags = preferredFlags;
        MemoryTypeBits = memoryTypeBits;
        Pool = pool;
        UserData = userData;
    }
}
