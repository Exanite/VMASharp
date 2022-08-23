namespace VMASharp;

public struct AllocationRequest
{
    public const long LostAllocationCost = 1048576;

    public long Offset, SumFreeSize, SumItemSize;
    public long ItemsToMakeLostCount;

    public object Item;

    public object CustomData;

    public AllocationRequestType Type;

    public readonly long CalcCost() {
        return SumItemSize + ItemsToMakeLostCount * LostAllocationCost;
    }
}