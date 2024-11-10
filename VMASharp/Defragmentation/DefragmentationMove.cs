namespace VMASharp.Defragmentation;

internal struct DefragmentationMove
{
    public int SourceBlockIndex, DestinationBlockIndex;

    public ulong SourceOffset, DestinationOffset, Size;

    public Allocation Allocation;

    public VulkanMemoryBlock SourceBlock, DestinationBlock;
}
