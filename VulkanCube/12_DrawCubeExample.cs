using Silk.NET.Vulkan;
using Silk.NET.Windowing;
using VMASharp;

namespace VulkanCube;

public sealed unsafe class DrawCubeExample : GraphicsPipelineExample
{
    public const int MaxFramesInFlight = 2;

    private CommandBuffer[] secondaryCommandBuffers;

    private readonly FrameCommandContext[] frameContexts = new FrameCommandContext[MaxFramesInFlight];

    private int currentFrame = 0;

    public DrawCubeExample()
    {
        RecordSecondaryCommandBuffers();

        InitializeFrameContexts();

        if (!BufferCopyPromise.IsCompleted)
        {
            BufferCopyPromise.Wait();
        }
    }

    public override void Run()
    {
        DisplayWindow.Render += DrawFrame;

        DisplayWindow.Run();

        VkApi.DeviceWaitIdle(Device);
    }

    public override void Dispose()
    {
        var primarys = stackalloc CommandBuffer[MaxFramesInFlight];

        for (var i = 0; i < MaxFramesInFlight; ++i)
        {
            ref var ctx = ref frameContexts[i];

            primarys[i] = ctx.CmdBuffer;

            VkApi.DestroyFence(Device, ctx.Fence, null);

            VkApi.DestroySemaphore(Device, ctx.ImageAvailable, null);
            VkApi.DestroySemaphore(Device, ctx.RenderFinished, null);
        }

        VkApi.FreeCommandBuffers(Device, CommandPool, MaxFramesInFlight, primarys);

        fixed (CommandBuffer* cbuffers = secondaryCommandBuffers)
        {
            VkApi.FreeCommandBuffers(Device, CommandPool, (uint)frameContexts.Length, cbuffers);
        }

        base.Dispose();
    }

    private void DrawFrame(double dTime)
    {
        ref var ctx = ref frameContexts[currentFrame];

        //Wait for a previous render operation to finish
        VkApi.WaitForFences(Device, 1, in ctx.Fence, true, ulong.MaxValue);

        //Acquire the next image index to render to, synchronize when its available
        uint nextImage = 0;
        var res = VkSwapchain.AcquireNextImage(Device, Swapchain, ulong.MaxValue, ctx.ImageAvailable, default, &nextImage);

        switch (res)
        {
            case Result.Success:
                break;
            case Result.ErrorOutOfDateKhr: //Window surface changed size, handling that is outside the scope of this example
                DisplayWindow.Close();

                return;
            default:
                throw new VulkanResultException("Failed to acquire next swapchain image!", res);
        }

        //Push semaphores, command buffer, and Pipeline Stage Flags to the stack to allow "fixed-less" addressing

        var waitSemaphore = ctx.ImageAvailable;
        var signalSemaphore = ctx.RenderFinished; //This semaphore will be used to synchronize presentation of the rendered image.

        var waitStages = PipelineStageFlags.PipelineStageColorAttachmentOutputBit;

        var buffer = RecordPrimaryCommandBuffer(ctx.CmdBuffer, (int)nextImage); //Records primary command buffer on the fly

        //Fill out queue submit info
        var submitInfo = new SubmitInfo(
            waitSemaphoreCount: 1, pWaitSemaphores: &waitSemaphore, pWaitDstStageMask: &waitStages,
            commandBufferCount: 1, pCommandBuffers: &buffer,
            signalSemaphoreCount: 1, pSignalSemaphores: &signalSemaphore);

        //Reset Fence to unsignaled
        VkApi.ResetFences(Device, 1, in ctx.Fence);

        //Submit to Graphics queue
        res = VkApi.QueueSubmit(GraphicsQueue, 1, &submitInfo, ctx.Fence);
        if (res != Result.Success)
        {
            throw new VulkanResultException("Failed to submit draw command buffer!", res);
        }

        fixed (SwapchainKHR* swapchain = &Swapchain)
        {
            var presentInfo = new PresentInfoKHR(
                waitSemaphoreCount: 1,
                pWaitSemaphores: &signalSemaphore,
                swapchainCount: 1,
                pSwapchains: swapchain,
                pImageIndices: &nextImage);

            VkSwapchain.QueuePresent(PresentQueue, &presentInfo);
        }

        currentFrame = (currentFrame + 1) % MaxFramesInFlight;
        Allocator.CurrentFrameIndex = currentFrame;
    }

    private void RecordSecondaryCommandBuffers()
    {
        const uint secondaryCommandBufferCount = 1;

        secondaryCommandBuffers = new CommandBuffer[secondaryCommandBufferCount];

        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = CommandPool,
            Level = CommandBufferLevel.Secondary,
            CommandBufferCount = secondaryCommandBufferCount,
        };

        fixed (CommandBuffer* commandBuffers = secondaryCommandBuffers)
        {
            var res = VkApi.AllocateCommandBuffers(Device, &allocInfo, commandBuffers);

            if (res != Result.Success)
            {
                throw new VulkanResultException("Failed to allocate command buffers!", res);
            }
        }

        var viewport = new Viewport
        {
            X = 0.0f,
            Y = 0.0f,
            Width = SwapchainExtent.Width,
            Height = SwapchainExtent.Height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f,
        };

        var scissor = new Rect2D(default, SwapchainExtent);

        var inherit = new CommandBufferInheritanceInfo(renderPass: RenderPass, subpass: 0);

        const CommandBufferUsageFlags usageFlags = CommandBufferUsageFlags.CommandBufferUsageRenderPassContinueBit | CommandBufferUsageFlags.CommandBufferUsageSimultaneousUseBit;

        var drawCommandBuffer = secondaryCommandBuffers[0];

        BeginCommandBuffer(drawCommandBuffer, usageFlags, &inherit);

        VkApi.CmdBindPipeline(drawCommandBuffer, PipelineBindPoint.Graphics, GraphicsPipeline);

        VkApi.CmdSetViewport(drawCommandBuffer, 0, 1, &viewport);
        VkApi.CmdSetScissor(drawCommandBuffer, 0, 1, &scissor);

        fixed (DescriptorSet* pDescriptorSets = DescriptorSets)
        {
            var setCount = (uint)DescriptorSets.Length;

            VkApi.CmdBindDescriptorSets(drawCommandBuffer, PipelineBindPoint.Graphics, GraphicsPipelineLayout, 0, setCount, pDescriptorSets, 0, null);
        }

        var vertexBuffer = VertexBuffer;
        ulong offset = 0;

        VkApi.CmdBindVertexBuffers(drawCommandBuffer, 0, 1, &vertexBuffer, &offset);

        vertexBuffer = InstanceBuffer;

        VkApi.CmdBindVertexBuffers(drawCommandBuffer, 1, 1, &vertexBuffer, &offset);

        VkApi.CmdBindIndexBuffer(drawCommandBuffer, IndexBuffer, 0, IndexType.Uint16);

        VkApi.CmdDrawIndexed(drawCommandBuffer, IndexCount, InstanceCount, 0, 0, 0);
        EndCommandBuffer(drawCommandBuffer);
    }

    private void InitializeFrameContexts()
    {
        var buffers = AllocateCommandBuffers(MaxFramesInFlight, CommandBufferLevel.Primary);

        for (var i = 0; i < MaxFramesInFlight; ++i)
        {
            ref var ctx = ref frameContexts[i];

            ctx.CmdBuffer = buffers[i];

            ctx.Fence = CreateFence(true);

            ctx.ImageAvailable = CreateSemaphore();

            ctx.RenderFinished = CreateSemaphore();
        }
    }

    private CommandBuffer RecordPrimaryCommandBuffer(CommandBuffer primary, int framebufferIndex)
    {
        var res = VkApi.ResetCommandBuffer(primary, 0);

        if (res != Result.Success) {}

        var clearValues = stackalloc ClearValue[2]
        {
            new ClearValue(new ClearColorValue
            {
                Float32_0 = 0,
                Float32_1 = 0,
                Float32_2 = 0,
                Float32_3 = 1,
            }),
            new ClearValue(depthStencil: new ClearDepthStencilValue(1.0f)),
        };

        var renderPassInfo = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = RenderPass,
            Framebuffer = FrameBuffers[framebufferIndex],
            RenderArea = new Rect2D(new Offset2D(0, 0), SwapchainExtent),
            ClearValueCount = 2,
            PClearValues = clearValues,
        };

        BeginCommandBuffer(primary, CommandBufferUsageFlags.CommandBufferUsageOneTimeSubmitBit);

        VkApi.CmdBeginRenderPass(primary, &renderPassInfo, SubpassContents.SecondaryCommandBuffers);

        fixed (CommandBuffer* cmds = secondaryCommandBuffers)
        {
            VkApi.CmdExecuteCommands(primary, (uint)secondaryCommandBuffers.Length, cmds);
        }

        VkApi.CmdEndRenderPass(primary);

        EndCommandBuffer(primary);

        return primary;
    }

    private Semaphore CreateSemaphore()
    {
        var semInfo = new SemaphoreCreateInfo
        {
            SType = StructureType.SemaphoreCreateInfo,
        };

        Semaphore sem;
        var res = VkApi.CreateSemaphore(Device, &semInfo, null, &sem);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Failed to create Semaphore!", res);
        }

        return sem;
    }

    private struct FrameCommandContext
    {
        public CommandBuffer CmdBuffer;
        public Fence Fence;
        public Semaphore ImageAvailable, RenderFinished;
    }
}
