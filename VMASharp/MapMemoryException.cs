using Silk.NET.Vulkan;

namespace VMASharp;

public class MapMemoryException : VulkanResultException
{
    public MapMemoryException(string message) : base(message) {}

    public MapMemoryException(Result res) : base("Mapping a Device Memory block encountered an issue", res) {}

    public MapMemoryException(string message, Result res) : base(message, res) {}
}
