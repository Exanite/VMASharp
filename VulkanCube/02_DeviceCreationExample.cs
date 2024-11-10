using System;
using System.Collections.Generic;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using VMASharp;

namespace VulkanCube;

public unsafe abstract class DeviceCreationExample : InstanceCreationExample
{
    private static readonly string[] RequiredDeviceExtensions = { "VK_KHR_swapchain" };

    protected readonly PhysicalDevice PhysicalDevice;
    protected readonly QueueFamilyIndices QueueIndices;
    protected readonly Device Device;
    protected readonly Queue GraphicsQueue, PresentQueue;
    protected readonly KhrSwapchain VkSwapchain;

    protected DeviceCreationExample()
    {
        PhysicalDevice = SelectPhysicalDevice(out QueueIndices);
        Device = CreateLogicalDevice(out GraphicsQueue, out PresentQueue);

        if (!VkApi.TryGetDeviceExtension(Instance, Device, out VkSwapchain))
        {
            throw new Exception("VK_KHR_Swapchain is missing or not specified");
        }
    }

    public override void Dispose()
    {
        VkSwapchain.Dispose();

        VkApi.DestroyDevice(Device, null);

        base.Dispose();
    }

    private PhysicalDevice SelectPhysicalDevice(out QueueFamilyIndices indices)
    {
        uint count = 0;
        var res = VkApi.EnumeratePhysicalDevices(Instance, &count, null);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Unable to enumerate physical devices", res);
        }

        if (count == 0)
        {
            throw new Exception("No physical devices found!");
        }

        var deviceList = stackalloc PhysicalDevice[(int)count];

        res = VkApi.EnumeratePhysicalDevices(Instance, &count, deviceList);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Unable to enumerate physical devices", res);
        }

        for (uint i = 0; i < count; ++i)
        {
            var device = deviceList[i];

            if (IsDeviceSuitable(device, out indices))
            {
                return device;
            }
        }

        throw new Exception("No suitable device found!");
    }

    private Device CreateLogicalDevice(out Queue GraphicsQueue, out Queue PresentQueue)
    {
        var queueInfos = stackalloc DeviceQueueCreateInfo[2];
        uint infoCount = 1;
        var queuePriority = 1f;

        queueInfos[0] = new DeviceQueueCreateInfo(queueFamilyIndex: (uint)QueueIndices.GraphicsFamily, queueCount: 1, pQueuePriorities: &queuePriority);

        if (QueueIndices.GraphicsFamily != QueueIndices.PresentFamily)
        {
            infoCount = 2;

            queueInfos[1] = new DeviceQueueCreateInfo(queueFamilyIndex: (uint)QueueIndices.PresentFamily, queueCount: 1, pQueuePriorities: &queuePriority);
        }

        PhysicalDeviceFeatures features = default;

        using var extensionNames = SilkMarshal.StringArrayToMemory(RequiredDeviceExtensions);

        var depthStencilFeature = new PhysicalDeviceSeparateDepthStencilLayoutsFeatures
        {
            SType = StructureType.PhysicalDeviceSeparateDepthStencilLayoutsFeatures,
            SeparateDepthStencilLayouts = true,
        };

        var createInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            //PNext = &depthStencilFeature,
            QueueCreateInfoCount = infoCount,
            PQueueCreateInfos = queueInfos,
            EnabledExtensionCount = (uint)RequiredDeviceExtensions.Length,
            PpEnabledExtensionNames = (byte**)extensionNames,
            PEnabledFeatures = &features,
        };

        Device device;
        var res = VkApi.CreateDevice(PhysicalDevice, &createInfo, null, &device);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Logical Device Creation Failed!", res);
        }

        Queue queue = default;
        VkApi.GetDeviceQueue(device, (uint)QueueIndices.GraphicsFamily, 0, &queue);

        GraphicsQueue = queue;

        if (QueueIndices.GraphicsFamily != QueueIndices.PresentFamily)
        {
            queue = default;
            VkApi.GetDeviceQueue(device, (uint)QueueIndices.PresentFamily, 0, &queue);
        }

        PresentQueue = queue;

        return device;
    }

    private bool IsDeviceSuitable(PhysicalDevice device, out QueueFamilyIndices indices)
    {
        FindQueueFamilies(device, out indices);

        return indices.IsComplete() && HasAllRequiredExtensions(device) && IsSwapchainSupportAdequate(device);
    }

    private void FindQueueFamilies(PhysicalDevice device, out QueueFamilyIndices indices)
    {
        indices = new QueueFamilyIndices();

        var families = QuerryQueueFamilyProperties(device);

        for (var i = 0; i < families.Length; ++i)
        {
            ref var queueFamily = ref families[i];

            const QueueFlags GraphicsQueueBits = QueueFlags.QueueGraphicsBit | QueueFlags.QueueTransferBit;

            if ((queueFamily.QueueFlags & GraphicsQueueBits) == GraphicsQueueBits)
            {
                indices.GraphicsFamily = (uint)i;
            }

            var res = VkSurface.GetPhysicalDeviceSurfaceSupport(device, (uint)i, WindowSurface, out var presentSupport);

            if (res == Result.Success && presentSupport)
            {
                indices.PresentFamily = (uint)i;
            }

            if (indices.IsComplete())
            {
                break;
            }
        }
    }

    private static QueueFamilyProperties[] QuerryQueueFamilyProperties(PhysicalDevice device)
    {
        uint count = 0;
        VkApi.GetPhysicalDeviceQueueFamilyProperties(device, &count, null);

        if (count == 0)
        {
            return Array.Empty<QueueFamilyProperties>();
        }

        var arr = new QueueFamilyProperties[count];

        fixed (QueueFamilyProperties* pProperties = arr)
        {
            VkApi.GetPhysicalDeviceQueueFamilyProperties(device, &count, pProperties);
        }

        return arr;
    }

    private static bool HasAllRequiredExtensions(PhysicalDevice device)
    {
        uint count = 0;
        var res = VkApi.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, null);

        if (res != Result.Success || count == 0)
        {
            return false;
        }

        var pExtensions = stackalloc ExtensionProperties[(int)count];

        res = VkApi.EnumerateDeviceExtensionProperties(device, (byte*)null, &count, pExtensions);

        if (res != Result.Success)
        {
            return false;
        }

        var extensions = new HashSet<string>((int)count, StringComparer.OrdinalIgnoreCase);

        for (uint i = 0; i < count; ++i)
        {
            var name = SilkMarshal.PtrToString((nint)pExtensions[i].ExtensionName);

            extensions.Add(name);
        }

        foreach (var ext in RequiredDeviceExtensions)
        {
            if (!extensions.Contains(ext))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsSwapchainSupportAdequate(PhysicalDevice device) //If there are either no surface formats or no present modes supported, then this method returns false.
    {
        uint count = 0;

        VkSurface.GetPhysicalDeviceSurfaceFormats(device, WindowSurface, &count, null);

        if (count == 0)
        {
            return false;
        }

        count = 0;
        VkSurface.GetPhysicalDeviceSurfacePresentModes(device, WindowSurface, &count, null);

        return count != 0;
    }

    protected struct QueueFamilyIndices
    {
        public uint? GraphicsFamily;
        public uint? PresentFamily;

        public bool IsComplete()
        {
            return GraphicsFamily.HasValue && PresentFamily.HasValue;
        }
    }
}
