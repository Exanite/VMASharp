using System;
using System.IO;
using Silk.NET.Vulkan;
using VMASharp;

namespace VulkanCube;

public abstract unsafe class ShaderModulesExample : RenderPassExample
{
    protected ShaderModule VertexShader, FragmentShader;

    public ShaderModulesExample()
    {
        VertexShader = LoadShaderModule("../../../vert.spv");
        FragmentShader = LoadShaderModule("../../../frag.spv");
    }

    public override void Dispose()
    {
        VkApi.DestroyShaderModule(Device, VertexShader, null);
        VkApi.DestroyShaderModule(Device, FragmentShader, null);

        base.Dispose();
    }

    private ShaderModule LoadShaderModule(string filename)
    {
        var data = File.ReadAllBytes(filename);

        fixed (byte* pData = data)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = new UIntPtr((uint)data.Length),
                PCode = (uint*)pData,
            };

            ShaderModule module;
            var res = VkApi.CreateShaderModule(Device, &createInfo, null, &module);

            if (res != Result.Success)
            {
                throw new VulkanResultException("Failed to create Shader Module!", res);
            }

            return module;
        }
    }
}
