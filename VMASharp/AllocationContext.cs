namespace VMASharp;

public struct AllocationContext
{
    public int CurrentFrame, FrameInUseCount;
    public long BufferImageGranularity;
    public long AllocationSize;
    public long AllocationAlignment;
    public AllocationStrategyFlags Strategy;
    public SuballocationType SuballocationType;
    public bool CanMakeOtherLost;

    public AllocationContext(
        int currentFrame,
        int framesInUse,
        long bufferImageGranularity,
        long allocationSize,
        long allocationAlignment,
        AllocationStrategyFlags strategy,
        SuballocationType suballocType,
        bool canMakeOtherLost)
    {
        CurrentFrame = currentFrame;
        FrameInUseCount = framesInUse;
        BufferImageGranularity = bufferImageGranularity;
        AllocationSize = allocationSize;
        AllocationAlignment = allocationAlignment;
        Strategy = strategy;
        SuballocationType = suballocType;
        CanMakeOtherLost = canMakeOtherLost;
    }
}
