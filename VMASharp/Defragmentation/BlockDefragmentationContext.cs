using Buffer = Silk.NET.Vulkan.Buffer;

namespace VMASharp.Defragmentation;

internal struct BlockDefragmentationContext
{
    public enum BlockFlags
    {
        Used = 0x01,
    }

    public BlockFlags Flags;
    public Buffer     Buffer;
}