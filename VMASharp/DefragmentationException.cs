using Silk.NET.Vulkan;

namespace VMASharp;

public class DefragmentationException : VulkanResultException
{
    public DefragmentationException(string message) : base(message) { }

    public DefragmentationException(Result res) : base(res) { }

    public DefragmentationException(string message, Result res) : base(message, res) { }
}