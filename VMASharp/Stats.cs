using Silk.NET.Vulkan;

namespace VMASharp;

public class Stats
{
    public readonly StatInfo[] MemoryType;
    public readonly StatInfo[] MemoryHeap;
    public StatInfo Total;

    internal Stats(StatInfo[] memoryTypes, StatInfo[] memoryHeaps, in StatInfo total)
    {
        MemoryType = memoryTypes;
        MemoryHeap = memoryHeaps;
        Total = total;
    }

    internal Stats()
    {
        StatInfo.Init(out Total);

        MemoryType = new StatInfo[Vk.MaxMemoryTypes];
        MemoryHeap = new StatInfo[Vk.MaxMemoryHeaps];

        for (var i = 0; i < Vk.MaxMemoryTypes; ++i)
        {
            StatInfo.Init(out MemoryType[i]);
        }

        for (var i = 0; i < Vk.MaxMemoryHeaps; ++i)
        {
            StatInfo.Init(out MemoryHeap[i]);
        }
    }

    internal void PostProcess()
    {
        StatInfo.PostProcessCalcStatInfo(ref Total);

        for (var i = 0; i < Vk.MaxMemoryTypes; ++i)
        {
            StatInfo.PostProcessCalcStatInfo(ref MemoryType[i]);
        }

        for (var i = 0; i < Vk.MaxMemoryHeaps; ++i)
        {
            StatInfo.PostProcessCalcStatInfo(ref MemoryHeap[i]);
        }
    }
}
