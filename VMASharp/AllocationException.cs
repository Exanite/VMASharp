using System;
using Silk.NET.Vulkan;

namespace VMASharp;

public class AllocationException : VulkanResultException
{
    public AllocationException(string message) : base(message) {}

    public AllocationException(string message, Exception? innerException) : base(message, innerException) {}

    public AllocationException(Result res) : base(res) {}

    public AllocationException(string message, Result res) : base(message, res) {}
}
