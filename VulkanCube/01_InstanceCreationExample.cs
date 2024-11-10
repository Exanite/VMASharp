using System;
using System.Collections.Generic;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using VMASharp;

namespace VulkanCube;

public abstract unsafe class InstanceCreationExample : ExampleBase
{
    internal static readonly Vk VkApi;

    static InstanceCreationExample()
    {
        VkApi = Vk.GetApi();
    }

    protected readonly IWindow DisplayWindow;
    protected readonly Instance Instance;
    protected readonly KhrSurface VkSurface;

    protected readonly SurfaceKHR WindowSurface;

    protected InstanceCreationExample()
    {
        DisplayWindow = CreateWindow();
        Instance = CreateInstance();

        if (!VkApi.TryGetInstanceExtension(Instance, out VkSurface))
        {
            throw new Exception("VK_KHR_Surface is missing or not specified");
        }

        WindowSurface = DisplayWindow.VkSurface.Create<AllocationCallbacks>(Instance.ToHandle(), null).ToSurface();
    }

    private static IWindow CreateWindow()
    {
        var options = WindowOptions.DefaultVulkan;

        options.Title = "Hello Cube";
        options.FramesPerSecond = 60;

        Window.PrioritizeGlfw();

        var window = Window.Create(options);

        window.Initialize();

        if (window.VkSurface == null)
        {
            throw new NotSupportedException("Vulkan is not supported.");
        }

        return window;
    }

    private Instance CreateInstance()
    {
        using var appName = SilkMarshal.StringToMemory("Hello Cube");
        using var engineName = SilkMarshal.StringToMemory("Custom Engine");

        var appInfo = new ApplicationInfo
        (
            pApplicationName: (byte*)appName,
            applicationVersion: new Version32(0, 0, 1),
            pEngineName: (byte*)engineName,
            engineVersion: new Version32(0, 0, 1),
            apiVersion: Vk.Version11
        );

        var extensions = new List<string>(GetWindowExtensions())
        {
            Debugging.DebugExtensionString,
        };

        string[] layers = { "VK_LAYER_KHRONOS_validation" };

        using var extList = SilkMarshal.StringArrayToMemory(extensions);
        using var layerList = SilkMarshal.StringArrayToMemory(layers);

        var instInfo = new InstanceCreateInfo(pApplicationInfo: &appInfo,
            enabledLayerCount: (uint)layers.Length,
            ppEnabledLayerNames: (byte**)layerList,
            enabledExtensionCount: (uint)extensions.Count,
            ppEnabledExtensionNames: (byte**)extList);

        Instance inst;
        var res = VkApi.CreateInstance(&instInfo, null, &inst);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Instance Creation Failed", res);
        }

        return inst;
    }

    public override void Dispose()
    {
        VkSurface.DestroySurface(Instance, WindowSurface, null);

        VkApi.DestroyInstance(Instance, null);

        DisplayWindow.Reset();
    }

    private string[] GetWindowExtensions()
    {
        var ptr = (IntPtr)DisplayWindow.VkSurface.GetRequiredExtensions(out var count);

        var arr = new string[count];

        SilkMarshal.CopyPtrToStringArray(ptr, arr);

        return arr;
    }
}
