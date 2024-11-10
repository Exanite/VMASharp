using Silk.NET.Vulkan;
using VMASharp;

namespace VulkanCube;

public abstract unsafe class FrameBuffersExample : ShaderModulesExample
{
    protected readonly Framebuffer[] FrameBuffers;

    protected FrameBuffersExample()
    {
        FrameBuffers = CreateFrameBuffers();
    }

    public override void Dispose()
    {
        foreach (var fb in FrameBuffers)
        {
            VkApi.DestroyFramebuffer(Device, fb, null);
        }

        base.Dispose();
    }

    private Framebuffer[] CreateFrameBuffers()
    {
        var attachments = stackalloc ImageView[2] { default, DepthBuffer.View };

        var createInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = RenderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
            Width = SwapchainExtent.Width,
            Height = SwapchainExtent.Height,
            Layers = 1,
        };

        var arr = new Framebuffer[SwapchainImages.Length];

        for (var i = 0; i < arr.Length; ++i)
        {
            attachments[0] = SwapchainImages[i].View;

            Framebuffer tmp;

            var res = VkApi.CreateFramebuffer(Device, &createInfo, null, &tmp);

            if (res != Result.Success)
            {
                throw new VulkanResultException("Failed to create Shader Module!", res);
            }

            arr[i] = tmp;
        }

        return arr;
    }
}
