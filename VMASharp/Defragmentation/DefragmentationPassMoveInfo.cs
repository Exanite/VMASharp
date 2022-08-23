using Silk.NET.Vulkan;

namespace VMASharp.Defragmentation;

public struct DefragmentationPassMoveInfo
{
    public Allocation Allocation;

    public DeviceMemory Memory;

    public ulong Offset;
}