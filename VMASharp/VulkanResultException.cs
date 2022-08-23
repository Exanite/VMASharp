using System;
using System.Collections.Generic;
using System.Text;
using Silk.NET.Vulkan;

namespace VMASharp;

public class VulkanResultException : ApplicationException
{
    public readonly Result? Result;

    public VulkanResultException(string message) : base(message) { }

    public VulkanResultException(string message, Exception? innerException) : base(message, innerException) { }

    public VulkanResultException(Result res) : base("Vulkan returned an API error code") {
        Result = res;
    }

    public VulkanResultException(string message, Result res) : base(message) {
        Result = res;
    }
}