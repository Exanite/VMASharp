using System;

namespace VMASharp;

[Flags]
public enum PoolCreateFlags
{
    IgnoreBufferImageGranularity = 0x0001,
    LinearAlgorithm = 0x0010,
    BuddyAlgorithm = 0x0020,
}
