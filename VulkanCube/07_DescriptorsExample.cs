using Silk.NET.Vulkan;
using VMASharp;

namespace VulkanCube;

public abstract unsafe class DescriptorSetExample : LayoutsExample
{
    protected readonly DescriptorPool DescriptorPool;
    protected readonly DescriptorSet[] DescriptorSets;

    protected DescriptorSetExample()
    {
        DescriptorPool = CreateDescriptorPool();

        DescriptorSets = AllocateDescriptorSets();

        var info = new DescriptorBufferInfo
        {
            Buffer = UniformBuffer,
            Offset = 0,
            Range = UniformBufferSize,
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = DescriptorSets[0],
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBuffer,
            PBufferInfo = &info,
            DstArrayElement = 0,
            DstBinding = 0,
        };

        VkApi.UpdateDescriptorSets(Device, 1, &write, 0, null);
    }

    public override void Dispose()
    {
        VkApi.FreeDescriptorSets(Device, DescriptorPool, (uint)DescriptorSets.Length, in DescriptorSets[0]);

        VkApi.DestroyDescriptorPool(Device, DescriptorPool, null);

        base.Dispose();
    }

    private DescriptorPool CreateDescriptorPool()
    {
        var typeCount = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBuffer,
            DescriptorCount = 1,
        };

        var createInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = 1,
            PoolSizeCount = 1,
            PPoolSizes = &typeCount,
            Flags = DescriptorPoolCreateFlags.DescriptorPoolCreateFreeDescriptorSetBit,
        };

        DescriptorPool pool;

        var res = VkApi.CreateDescriptorPool(Device, &createInfo, null, &pool);

        if (res != Result.Success)
        {
            throw new VulkanResultException("Failed to create Descriptor Pool!", res);
        }

        return pool;
    }

    private DescriptorSet[] AllocateDescriptorSets()
    {
        fixed (DescriptorSetLayout* pLayouts = DescriptorSetLayouts)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = DescriptorPool,
                DescriptorSetCount = (uint)DescriptorSetLayouts.Length,
                PSetLayouts = pLayouts,
            };

            var arr = new DescriptorSet[DescriptorSetLayouts.Length];

            fixed (DescriptorSet* pSets = arr)
            {
                var res = VkApi.AllocateDescriptorSets(Device, &allocInfo, pSets);

                if (res != Result.Success)
                {
                    throw new VulkanResultException("Failed to allocate Descriptor Sets!", res);
                }

                return arr;
            }
        }
    }
}
