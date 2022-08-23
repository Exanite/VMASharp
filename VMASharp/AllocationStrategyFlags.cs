namespace VMASharp;

[Flags]
public enum AllocationStrategyFlags
{
    BestFit          = 0x1,
    WorstFit         = 0x2,
    FirstFit         = 0x4,
    MinMemory        = BestFit,
    MinTime          = FirstFit,
    MinFragmentation = WorstFit,
}