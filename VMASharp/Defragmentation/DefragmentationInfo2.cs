using System;
using System.Collections.Generic;
using System.Text;
using Silk.NET.Vulkan;
using VMASharp;

namespace VMASharp.Defragmentation;

public struct DefragmentationInfo2
{
    public DefragmentationFlags Flags;

    public Allocation[] Allocations;

    public bool[] AllocationsChanged;

    public VulkanMemoryPool[] Pools;

    public ulong MaxCPUBytesToMove;

    public int MaxCPUAllocationsToMove;

    public ulong MaxGPUBytesToMove;

    public int MaxGPUAllocationsToMove;

    public CommandBuffer CommandBuffer;
}