namespace VMASharp;

public struct PoolStats
{
    public long Size;

    public long UnusedSize;

    public int AllocationCount;

    public int UnusedRangeCount;

    public long UnusedRangeSizeMax;

    public int BlockCount;
}