using System;
using Silk.NET.Vulkan;
using VMASharp;

namespace VulkanCube;

public abstract unsafe class CommandPoolCreationExample : SwapchainCreationExample
{
    protected readonly CommandPool CommandPool;

    protected CommandPoolCreationExample()
    {
        CommandPool = CreateCommandPool();
    }

    public override void Dispose()
    {
        VkApi.DestroyCommandPool(Device, CommandPool, null);

        base.Dispose();
    }

    private CommandPool CreateCommandPool()
    {
        var poolCreateInfo = new CommandPoolCreateInfo(
            flags: CommandPoolCreateFlags.CommandPoolCreateResetCommandBufferBit,
            queueFamilyIndex: QueueIndices.GraphicsFamily.Value);

        CommandPool pool;
        var res = VkApi.CreateCommandPool(Device, &poolCreateInfo, null, &pool);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Command Pool Creation Failed!", res);
        }

        return pool;
    }

    //Helper methods for other examples
    protected static void BeginCommandBuffer(CommandBuffer buffer, CommandBufferUsageFlags flags = default)
    {
        BeginCommandBuffer(buffer, flags, null);
    }

    protected static void BeginCommandBuffer(CommandBuffer buffer, CommandBufferUsageFlags flags, CommandBufferInheritanceInfo* inheritInfo)
    {
        var info = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = flags,
            PInheritanceInfo = inheritInfo,
        };

        var res = VkApi.BeginCommandBuffer(buffer, &info);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Failed to begin Command Buffer recording!", res);
        }
    }

    protected static void EndCommandBuffer(CommandBuffer buffer)
    {
        var res = VkApi.EndCommandBuffer(buffer);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Failed to end Command Buffer recording!", res);
        }
    }

    protected CommandBuffer AllocateCommandBuffer(CommandBufferLevel level)
    {
        var info = new CommandBufferAllocateInfo(commandPool: CommandPool, level: level, commandBufferCount: 1);

        CommandBuffer buffer;

        var res = VkApi.AllocateCommandBuffers(Device, &info, &buffer);

        if (res != Result.Success)
        {
            throw new Exception("Unable to allocate command buffers");
        }

        return buffer;
    }

    protected CommandBuffer[] AllocateCommandBuffers(int count, CommandBufferLevel level)
    {
        var info = new CommandBufferAllocateInfo(commandPool: CommandPool, level: level, commandBufferCount: (uint)count);

        var buffers = new CommandBuffer[count];

        fixed (CommandBuffer* pbuffers = buffers)
        {
            var res = VkApi.AllocateCommandBuffers(Device, &info, pbuffers);

            if (res != Result.Success)
            {
                throw new Exception("Unable to allocate command buffers");
            }
        }

        return buffers;
    }

    protected void FreeCommandBuffer(CommandBuffer buffer)
    {
        VkApi.FreeCommandBuffers(Device, CommandPool, 1, &buffer);
    }

    protected void FreeCommandBuffers(ReadOnlySpan<CommandBuffer> buffers)
    {
        fixed (CommandBuffer* pbuffers = buffers)
        {
            VkApi.FreeCommandBuffers(Device, CommandPool, (uint)buffers.Length, pbuffers);
        }
    }
}
