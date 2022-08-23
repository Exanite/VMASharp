using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.CompilerServices;

namespace VMASharp;

public struct AllocationBudget
{
    public long BlockBytes;
    public long AllocationBytes;
    public long Usage;
    public long Budget;

    public AllocationBudget(long blockBytes, long allocationBytes, long usage, long budget) {
        BlockBytes = blockBytes;
        AllocationBytes = allocationBytes;
        Usage = usage;
        Budget = budget;
    }
}