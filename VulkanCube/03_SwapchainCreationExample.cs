using System;
using System.Diagnostics;
using Silk.NET.Vulkan;
using VMASharp;

namespace VulkanCube;

public unsafe abstract class SwapchainCreationExample : DeviceCreationExample
{
    protected readonly SwapchainKHR Swapchain;
    protected readonly Extent2D SwapchainExtent;
    protected readonly Format SwapchainImageFormat;
    protected readonly SwapchainImage[] SwapchainImages;

    protected int SwapchainImageCount => SwapchainImages.Length;

    protected SwapchainCreationExample()
    {
        Swapchain = CreateSwapchain(out SwapchainExtent, out SwapchainImageFormat, out SwapchainImages);
    }

    public override void Dispose()
    {
        for (var i = 0; i < SwapchainImages.Length; ++i)
        {
            VkApi.DestroyImageView(Device, SwapchainImages[i].View, null);
        }

        VkSwapchain.DestroySwapchain(Device, Swapchain, null);

        base.Dispose();
    }

    private SwapchainKHR CreateSwapchain(out Extent2D extent, out Format swapImageFormat, out SwapchainImage[] swapImages)
    {
        QuerySwapchainSupport(PhysicalDevice, out var details);

        var surfaceFormat = ChooseSwapSurfaceFormat(details.Formats);
        var presentMode = ChooseSwapPresentMode(details.PresentModes);
        extent = ChooseSwapExtent(details.Capabilities);

        var imageCount = details.Capabilities.MinImageCount + 1;

        if (details.Capabilities.MaxImageCount > 0 && imageCount > details.Capabilities.MaxImageCount)
        {
            imageCount = details.Capabilities.MaxImageCount;
        }

        var createInfo = new SwapchainCreateInfoKHR
        {
            SType = StructureType.SwapchainCreateInfoKhr,
            Surface = WindowSurface,
            MinImageCount = imageCount,
            ImageFormat = surfaceFormat.Format,
            ImageColorSpace = surfaceFormat.ColorSpace,
            ImageExtent = extent,
            ImageArrayLayers = 1,
            ImageUsage = ImageUsageFlags.ImageUsageColorAttachmentBit,
        };

        if (QueueIndices.GraphicsFamily != QueueIndices.PresentFamily)
        {
            createInfo.ImageSharingMode = SharingMode.Concurrent;
            createInfo.QueueFamilyIndexCount = 2;

            var indices = stackalloc uint[2] { QueueIndices.GraphicsFamily.Value, QueueIndices.PresentFamily.Value };

            createInfo.PQueueFamilyIndices = indices;
        }
        else
        {
            createInfo.ImageSharingMode = SharingMode.Exclusive;
        }

        createInfo.PreTransform = details.Capabilities.CurrentTransform;
        createInfo.CompositeAlpha = CompositeAlphaFlagsKHR.CompositeAlphaOpaqueBitKhr;
        createInfo.PresentMode = presentMode;
        createInfo.Clipped = true;

        createInfo.OldSwapchain = default;

        SwapchainKHR swapchain;

        var res = VkSwapchain.CreateSwapchain(Device, &createInfo, null, &swapchain);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Failed to create swapchain!", res);
        }

        uint count = 0;

        res = VkSwapchain.GetSwapchainImages(Device, swapchain, &count, null);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Failed to retrieve swapchain images!", res);
        }

        var images = stackalloc Image[(int)count];

        res = VkSwapchain.GetSwapchainImages(Device, swapchain, &count, images);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Failed to retrieve swapchain images!", res);
        }

        var viewCreateInfo = new ImageViewCreateInfo
        {
            SType = StructureType.ImageViewCreateInfo,
            ViewType = ImageViewType.ImageViewType2D,
            Format = surfaceFormat.Format,
            Components =
            {
                R = ComponentSwizzle.Identity,
                G = ComponentSwizzle.Identity,
                B = ComponentSwizzle.Identity,
                A = ComponentSwizzle.Identity,
            },
            SubresourceRange =
            {
                AspectMask = ImageAspectFlags.ImageAspectColorBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
        };

        var arr = new SwapchainImage[count];

        for (var i = 0; i < arr.Length; ++i)
        {
            viewCreateInfo.Image = images[i];

            ImageView view = default;
            res = VkApi.CreateImageView(Device, &viewCreateInfo, null, &view);

            if (res != Result.Success)
            {
                throw new VulkanResultException("Swapchain image view creation failed!", res);
            }

            arr[i] = new SwapchainImage { Image = images[i], View = view };
        }

        swapImageFormat = surfaceFormat.Format;
        swapImages = arr;

        return swapchain;
    }

    private void QuerySwapchainSupport(PhysicalDevice device, out SwapChainSupportDetails details)
    {
        details = new SwapChainSupportDetails();

        var res = VkSurface.GetPhysicalDeviceSurfaceCapabilities(device, WindowSurface, out details.Capabilities);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Unable to get Surface Capabilities of this physical device!", res);
        }

        uint count = 0;
        res = VkSurface.GetPhysicalDeviceSurfaceFormats(device, WindowSurface, &count, null);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Unable to get Surface Formats of this physical device!", res);
        }

        if (count != 0)
        {
            details.Formats = new SurfaceFormatKHR[count];

            fixed (SurfaceFormatKHR* pFormats = details.Formats)
            {
                res = VkSurface.GetPhysicalDeviceSurfaceFormats(device, WindowSurface, &count, pFormats);
            }

            if (res != Result.Success)
            {
                throw new VulkanResultException("Unable to get Surface Formats of this physical device!", res);
            }

            count = 0; //Reset count because its now non-zero
        }
        else
        {
            details.Formats = Array.Empty<SurfaceFormatKHR>();
        }

        res = VkSurface.GetPhysicalDeviceSurfacePresentModes(device, WindowSurface, &count, null);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Unable to get Surface Present Modes of this physical device!", res);
        }

        if (count != 0)
        {
            details.PresentModes = new PresentModeKHR[count];

            fixed (PresentModeKHR* pPresentModes = details.PresentModes)
            {
                res = VkSurface.GetPhysicalDeviceSurfacePresentModes(device, WindowSurface, &count, pPresentModes);
            }

            if (res != Result.Success)
            {
                throw new VulkanResultException("Unable to get Surface Present Modes of this physical device!", res);
            }
        }
        else
        {
            details.PresentModes = Array.Empty<PresentModeKHR>();
        }
    }

    private SurfaceFormatKHR ChooseSwapSurfaceFormat(SurfaceFormatKHR[] formats)
    {
        Debug.Assert(formats.Length > 0);

        var i = Array.FindIndex(formats, format => format.Format == Format.B8G8R8A8Unorm);

        return i < 0 ? formats[0] : formats[i];
    }

    private PresentModeKHR ChooseSwapPresentMode(PresentModeKHR[] presentModes)
    {
        var i = Array.FindIndex(presentModes, availablePresentMode => availablePresentMode == PresentModeKHR.PresentModeMailboxKhr);

        return i < 0 ? PresentModeKHR.PresentModeFifoKhr : PresentModeKHR.PresentModeMailboxKhr;
    }

    private Extent2D ChooseSwapExtent(in SurfaceCapabilitiesKHR capabilities)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue)
        {
            return capabilities.CurrentExtent;
        }

        var WinSize = DisplayWindow.Size;

        var width = Math.Clamp((uint)WinSize.X, capabilities.MinImageExtent.Width, capabilities.MaxImageExtent.Width);
        var height = Math.Clamp((uint)WinSize.Y, capabilities.MinImageExtent.Height, capabilities.MaxImageExtent.Height);

        return new Extent2D(width, height);
    }

    protected struct SwapChainSupportDetails
    {
        public SurfaceCapabilitiesKHR Capabilities;
        public SurfaceFormatKHR[] Formats;
        public PresentModeKHR[] PresentModes;
    }

    protected struct SwapchainImage
    {
        public Image Image;
        public ImageView View;
    }
}
