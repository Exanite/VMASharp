namespace VMASharp;

[Flags]
public enum AllocationCreateFlags
{
    DedicatedMemory  = 0x0001,
    NeverAllocate    = 0x0002,
    Mapped           = 0x0004,
    CanBecomeLost    = 0x0008,
    CanMakeOtherLost = 0x0010,
    UpperAddress     = 0x0040,
    DontBind         = 0x0080,
    WithinBudget     = 0x0100,
}