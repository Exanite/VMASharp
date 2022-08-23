using Silk.NET.Vulkan;
using Silk.NET.Core;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;
using JetBrains.Annotations;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace VMASharp;

using Defragmentation;
using VMASharp;

[PublicAPI]
public sealed unsafe class VulkanMemoryAllocator : IDisposable
{
    private const long             SmallHeapMaxSize   = 1024L * 1024 * 1024;
    private const BufferUsageFlags UnknownBufferUsage = unchecked((BufferUsageFlags)uint.MaxValue);

    internal Vk VkApi { get; }

    public Device Device { get; }
    internal readonly Instance Instance;

    internal readonly Version32 VulkanApiVersion;

    internal readonly bool UseExtMemoryBudget;
    internal readonly bool UseAmdDeviceCoherentMemory;
    internal readonly bool UseKhrBufferDeviceAddress;

    internal readonly uint HeapSizeLimitMask;

    private  PhysicalDeviceProperties       _physicalDeviceProperties;
    private PhysicalDeviceMemoryProperties _memoryProperties;

    internal readonly BlockList[] BlockLists = new BlockList[Vk.MaxMemoryTypes]; //Default Pools

    internal readonly DedicatedAllocationHandler[] DedicatedAllocations =
        new DedicatedAllocationHandler[Vk.MaxMemoryTypes];

    private readonly long           _preferredLargeHeapBlockSize;
    private readonly PhysicalDevice _physicalDevice;
    private          uint           _gpuDefragmentationMemoryTypeBits = uint.MaxValue;

    private readonly ReaderWriterLockSlim   _poolsMutex = new();
    private readonly List<VulkanMemoryPool> _pools      = new();
    internal         uint                   NextPoolId;

    internal readonly CurrentBudgetData Budget = new();

    [PublicAPI]
    public VulkanMemoryAllocator(in VulkanMemoryAllocatorCreateInfo createInfo) {
        VkApi = createInfo.VulkanApiObject ?? throw new ArgumentNullException(nameof(createInfo.VulkanApiObject), "API vtable is null");

        if (createInfo.Instance.Handle == default) {
            // ReSharper disable once NotResolvedInText
            throw new ArgumentNullException("createInfo.Instance");
        }

        if (createInfo.LogicalDevice.Handle == default) {
            // ReSharper disable once NotResolvedInText
            throw new ArgumentNullException("createInfo.LogicalDevice");
        }

        if (createInfo.PhysicalDevice.Handle == default) {
            // ReSharper disable once NotResolvedInText
            throw new ArgumentNullException("createInfo.PhysicalDevice");
        }

        if (createInfo.VulkanApiVersion < Vk.Version11) {
            throw new NotSupportedException("Vulkan API Version of less than 1.1 is not supported");
        }

        Instance = createInfo.Instance;
        _physicalDevice = createInfo.PhysicalDevice;
        Device = createInfo.LogicalDevice;

        VulkanApiVersion = createInfo.VulkanApiVersion;

        if (VulkanApiVersion == 0) {
            VulkanApiVersion = Vk.Version10;
        }

        UseExtMemoryBudget = (createInfo.Flags & AllocatorCreateFlags.ExtMemoryBudget) != 0;
        UseAmdDeviceCoherentMemory = (createInfo.Flags & AllocatorCreateFlags.AMDDeviceCoherentMemory) != 0;
        UseKhrBufferDeviceAddress = (createInfo.Flags & AllocatorCreateFlags.BufferDeviceAddress) != 0;

        VkApi.GetPhysicalDeviceProperties(_physicalDevice, out _physicalDeviceProperties);
        VkApi.GetPhysicalDeviceMemoryProperties(_physicalDevice, out _memoryProperties);

        Debug.Assert(Helpers.IsPow2(Helpers.DebugAlignment));
        Debug.Assert(Helpers.IsPow2(Helpers.DebugMinBufferImageGranularity));
        Debug.Assert(Helpers.IsPow2((long)_physicalDeviceProperties.Limits.BufferImageGranularity));
        Debug.Assert(Helpers.IsPow2((long)_physicalDeviceProperties.Limits.NonCoherentAtomSize));

        _preferredLargeHeapBlockSize = createInfo.PreferredLargeHeapBlockSize != 0
            ? createInfo.PreferredLargeHeapBlockSize
            : 256L * 1024 * 1024;

        GlobalMemoryTypeBits = CalculateGlobalMemoryTypeBits();

        if (createInfo.HeapSizeLimits != null) {
            Span<MemoryHeap> memoryHeaps =
                MemoryMarshal.CreateSpan(ref _memoryProperties.MemoryHeaps.Element0, MemoryHeapCount);

            int heapLimitLength = Math.Min(createInfo.HeapSizeLimits.Length, (int)Vk.MaxMemoryHeaps);

            for (int heapIndex = 0; heapIndex < heapLimitLength; ++heapIndex) {
                long limit = createInfo.HeapSizeLimits[heapIndex];

                if (limit <= 0) {
                    continue;
                }

                HeapSizeLimitMask |= 1u << heapIndex;
                ref MemoryHeap heap = ref memoryHeaps[heapIndex];

                if ((ulong)limit < heap.Size) {
                    heap.Size = (ulong)limit;
                }
            }
        }

        for (int memTypeIndex = 0; memTypeIndex < MemoryTypeCount; ++memTypeIndex) {
            long preferredBlockSize = CalcPreferredBlockSize(memTypeIndex);

            BlockLists[memTypeIndex] =
                new BlockList(this, null, memTypeIndex, preferredBlockSize, 0, int.MaxValue,
                    BufferImageGranularity, createInfo.FrameInUseCount, false,
                    Helpers.DefaultMetaObjectCreate);

            ref DedicatedAllocationHandler alloc = ref DedicatedAllocations[memTypeIndex];

            alloc.Allocations = new List<Allocation>();
            alloc.Mutex = new ReaderWriterLockSlim();
        }

        if (UseExtMemoryBudget) {
            UpdateVulkanBudget();
        }
    }

    public int CurrentFrameIndex { get; set; }

    internal long BufferImageGranularity => (long)Math.Max(1, _physicalDeviceProperties.Limits.BufferImageGranularity);

    internal int MemoryHeapCount => (int)MemoryProperties.MemoryHeapCount;

    internal int MemoryTypeCount => (int)MemoryProperties.MemoryTypeCount;

    internal bool IsIntegratedGpu => _physicalDeviceProperties.DeviceType == PhysicalDeviceType.IntegratedGpu;

    internal uint GlobalMemoryTypeBits { get; private set; }


    [PublicAPI]
    public void Dispose() {
        if (_pools.Count != 0) {
            throw new InvalidOperationException("");
        }

        int i = MemoryTypeCount;

        while (i-- != 0) {
            if (DedicatedAllocations[i].Allocations.Count != 0) {
                throw new InvalidOperationException("Unfreed dedicatedAllocations found");
            }

            BlockLists[i].Dispose();
        }
    }

    [PublicAPI]
    public MemoryPropertyFlags GetMemoryTypeProperties(int memoryTypeIndex) => _memoryProperties.MemoryTypes[memoryTypeIndex].PropertyFlags;

    [PublicAPI]
    public int? FindMemoryTypeIndex(uint memoryTypeBits, in AllocationCreateInfo allocInfo) {
        memoryTypeBits &= GlobalMemoryTypeBits;

        if (allocInfo.MemoryTypeBits != 0) {
            memoryTypeBits &= allocInfo.MemoryTypeBits;
        }

        MemoryPropertyFlags requiredFlags = allocInfo.RequiredFlags,
            preferredFlags = allocInfo.PreferredFlags,
            notPreferredFlags = default;

        switch (allocInfo.Usage) {
            case MemoryUsage.Unknown:
                break;
            case MemoryUsage.GPU_Only:
                if (IsIntegratedGpu ||
                    (preferredFlags & MemoryPropertyFlags.HostVisibleBit) == 0) {
                    preferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                }

                break;
            case MemoryUsage.CPU_Only:
                requiredFlags |= MemoryPropertyFlags.HostVisibleBit |
                                 MemoryPropertyFlags.HostCoherentBit;
                break;
            case MemoryUsage.CPU_To_GPU:
                requiredFlags |= MemoryPropertyFlags.HostVisibleBit;
                if (!IsIntegratedGpu ||
                    (preferredFlags & MemoryPropertyFlags.HostVisibleBit) == 0) {
                    preferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                }

                break;
            case MemoryUsage.GPU_To_CPU:
                requiredFlags |= MemoryPropertyFlags.HostVisibleBit;
                preferredFlags |= MemoryPropertyFlags.HostCachedBit;
                break;
            case MemoryUsage.CPU_Copy:
                notPreferredFlags |= MemoryPropertyFlags.DeviceLocalBit;
                break;
            case MemoryUsage.GPU_LazilyAllocated:
                requiredFlags |= MemoryPropertyFlags.LazilyAllocatedBit;
                break;
            default:
                throw new ArgumentException("Invalid Usage Flags");
        }

        if (((allocInfo.RequiredFlags | allocInfo.PreferredFlags) &
             (MemoryPropertyFlags.DeviceCoherentBitAmd |
              MemoryPropertyFlags.DeviceUncachedBitAmd)) == 0) {
            notPreferredFlags |= MemoryPropertyFlags.DeviceCoherentBitAmd;
        }

        int? memoryTypeIndex = null;
        int minCost = int.MaxValue;
        uint memTypeBit = 1;

        for (int memTypeIndex = 0; memTypeIndex < MemoryTypeCount; ++memTypeIndex, memTypeBit <<= 1) {
            if ((memTypeBit & memoryTypeBits) == 0)
                continue;

            MemoryPropertyFlags currFlags = _memoryProperties.MemoryTypes[memTypeIndex].PropertyFlags;

            if ((requiredFlags & ~currFlags) != 0)
                continue;

            int currCost = BitOperations.PopCount((uint)(preferredFlags & ~currFlags));

            currCost += BitOperations.PopCount((uint)(currFlags & notPreferredFlags));

            if (currCost < minCost) {
                if (currCost == 0) {
                    return memTypeIndex;
                }

                memoryTypeIndex = memTypeIndex;
                minCost = currCost;
            }
        }

        return memoryTypeIndex;
    }

    [PublicAPI]
    public int? FindMemoryTypeIndexForBufferInfo(in BufferCreateInfo bufferInfo,
        in AllocationCreateInfo allocInfo) {
        Buffer buffer;
        fixed (BufferCreateInfo* pBufferInfo = &bufferInfo) {
            Result res = VkApi.CreateBuffer(Device, pBufferInfo, null, &buffer);

            if (res != Result.Success) {
                throw new VulkanResultException(res);
            }
        }

        MemoryRequirements memReq;
        VkApi.GetBufferMemoryRequirements(Device, buffer, &memReq);

        int? tmp = FindMemoryTypeIndex(memReq.MemoryTypeBits, in allocInfo);

        VkApi.DestroyBuffer(Device, buffer, null);

        return tmp;
    }

    [PublicAPI]
    public int? FindMemoryTypeIndexForImageInfo(in ImageCreateInfo imageInfo, in AllocationCreateInfo allocInfo) {
        Image image;
        fixed (ImageCreateInfo* pImageInfo = &imageInfo) {
            Result res = VkApi.CreateImage(Device, pImageInfo, null, &image);

            if (res != Result.Success) {
                throw new VulkanResultException(res);
            }
        }

        MemoryRequirements memReq;
        VkApi.GetImageMemoryRequirements(Device, image, &memReq);

        int? tmp = FindMemoryTypeIndex(memReq.MemoryTypeBits, in allocInfo);

        VkApi.DestroyImage(Device, image, null);

        return tmp;
    }

    /// <summary>
    /// Allocate a block of memory
    /// </summary>
    /// <param name="requirements">Memory Requirements for the allocation</param>
    /// <param name="createInfo">Allocation Creation information</param>
    /// <returns>An object representing the allocation</returns>
    [PublicAPI]
    public Allocation AllocateMemory(in MemoryRequirements requirements, in AllocationCreateInfo createInfo) {
        DedicatedAllocationInfo dedicatedInfo = DedicatedAllocationInfo.Default;

        return AllocateMemory(in requirements, in dedicatedInfo, in createInfo, SuballocationType.Unknown);
    }

    /// <summary>
    /// Allocate a block of memory with the memory requirements of <paramref name="buffer"/>,
    /// optionally binding it to the allocation
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="createInfo"></param>
    /// <param name="bindToBuffer">Whether to bind <paramref name="buffer"/> to the allocation</param>
    /// <returns></returns>
    [PublicAPI]
    public Allocation AllocateMemoryForBuffer(Buffer buffer, in AllocationCreateInfo createInfo,
        bool bindToBuffer = false) {
        DedicatedAllocationInfo dedicatedInfo = DedicatedAllocationInfo.Default;
    
        dedicatedInfo.DedicatedBuffer = buffer;

        GetBufferMemoryRequirements(buffer, out MemoryRequirements memReq,
            out dedicatedInfo.RequiresDedicatedAllocation, out dedicatedInfo.PrefersDedicatedAllocation);

        Allocation alloc = AllocateMemory(in memReq, in dedicatedInfo, in createInfo, SuballocationType.Buffer);

        if (bindToBuffer) {
            alloc.BindBufferMemory(buffer);
        }

        return alloc;
    }

    /// <summary>
    /// Allocate a block of memory with the memory requirements of <paramref name="image"/>,
    /// optionally binding it to the allocation
    /// </summary>
    /// <param name="image"></param>
    /// <param name="createInfo"></param>
    /// <param name="bindToImage">Whether to bind <paramref name="image"/> to the allocation</param>
    /// <returns></returns>
    [PublicAPI]
    public Allocation AllocateMemoryForImage(Image image, in AllocationCreateInfo createInfo,
        bool bindToImage = false) {
        DedicatedAllocationInfo dedicatedInfo = DedicatedAllocationInfo.Default;

        dedicatedInfo.DedicatedImage = image;

        GetImageMemoryRequirements(image, out MemoryRequirements memReq, out dedicatedInfo.RequiresDedicatedAllocation,
            out dedicatedInfo.PrefersDedicatedAllocation);

        Allocation alloc = AllocateMemory(in memReq, in dedicatedInfo, in createInfo,
            SuballocationType.ImageUnknown);

        if (bindToImage) {
            alloc.BindImageMemory(image);
        }

        return alloc;
    }

    [PublicAPI]
    public Result CheckCorruption(uint memoryTypeBits) {
        throw new NotImplementedException();
    }

    [PublicAPI]
    public Buffer CreateBuffer(in BufferCreateInfo bufferInfo, in AllocationCreateInfo allocInfo,
        out Allocation allocation) {
        Result res;
        Buffer buffer;

        Allocation? alloc;


        fixed (BufferCreateInfo* pInfo = &bufferInfo) {
            pInfo->SType = StructureType.BufferCreateInfo;
            res = VkApi.CreateBuffer(Device, pInfo, null, &buffer);

            if (res < 0) {
                throw new AllocationException("Buffer creation failed", res);
            }
        }

        try {
            DedicatedAllocationInfo dedicatedInfo = default;

            dedicatedInfo.DedicatedBuffer = buffer;
            dedicatedInfo.DedicatedBufferUsage = bufferInfo.Usage;

            GetBufferMemoryRequirements(buffer, out MemoryRequirements memReq,
                out dedicatedInfo.RequiresDedicatedAllocation, out dedicatedInfo.PrefersDedicatedAllocation);

            alloc = AllocateMemory(in memReq, in dedicatedInfo, in allocInfo, SuballocationType.Buffer);
        }
        catch {
            VkApi.DestroyBuffer(Device, buffer, null);
            throw;
        }

        if ((allocInfo.Flags & AllocationCreateFlags.DontBind) == 0) {
            res = alloc.BindBufferMemory(buffer);

            if (res != Result.Success) {
                VkApi.DestroyBuffer(Device, buffer, null);
                alloc.Dispose();

                throw new AllocationException("Unable to bind memory to buffer", res);
            }
        }

        allocation = alloc;

        return buffer;
    }

    /// <summary>
    /// Create a vulkan image and allocate a block of memory to go with it.
    /// Unless specified otherwise with <seealso cref="AllocationCreateFlags.DontBind"/>, the Image will be bound to the memory for you.
    /// </summary>
    /// <param name="imageInfo">Information to create the image</param>
    /// <param name="allocInfo">Information to allocate memory for the image</param>
    /// <param name="allocation">The object corresponding to the allocation</param>
    /// <returns>The created image</returns>
    ///
    [PublicAPI]
    public Image CreateImage(in ImageCreateInfo imageInfo, in AllocationCreateInfo allocInfo,
        out Allocation allocation) {
        if (imageInfo.Extent.Width == 0 ||
            imageInfo.Extent.Height == 0 ||
            imageInfo.Extent.Depth == 0 ||
            imageInfo.MipLevels == 0 ||
            imageInfo.ArrayLayers == 0) {
            throw new ArgumentException("Invalid Image Info");
        }

        Result res;
        Image image;
        Allocation alloc;

        fixed (ImageCreateInfo* pInfo = &imageInfo) {
            pInfo->SType = StructureType.ImageCreateInfo;
            res = VkApi.CreateImage(Device, pInfo, null, &image);

            if (res < 0) {
                throw new AllocationException("Image creation failed", res);
            }
        }

        // ReSharper disable once UnusedVariable
        SuballocationType suballocType = imageInfo.Tiling == ImageTiling.Optimal
            ? SuballocationType.ImageOptimal
            : SuballocationType.ImageLinear;

        try {
            alloc = AllocateMemoryForImage(image, allocInfo);
        }
        catch {
            VkApi.DestroyImage(Device, image, null);
            throw;
        }

        if ((allocInfo.Flags & AllocationCreateFlags.DontBind) == 0) {
            res = alloc.BindImageMemory(image);

            if (res != Result.Success) {
                VkApi.DestroyImage(Device, image, null);
                alloc.Dispose();

                throw new AllocationException("Unable to Bind memory to image", res);
            }
        }

        allocation = alloc;

        return image;
    }

    public ref readonly PhysicalDeviceMemoryProperties MemoryProperties =>ref  _memoryProperties;

    internal int MemoryTypeIndexToHeapIndex(int typeIndex) {
        Debug.Assert(typeIndex < MemoryProperties.MemoryTypeCount);
        return (int)_memoryProperties.MemoryTypes[typeIndex].HeapIndex;
    }

    internal bool IsMemoryTypeNonCoherent(int memTypeIndex) {
        return (_memoryProperties.MemoryTypes[memTypeIndex].PropertyFlags & (MemoryPropertyFlags.HostVisibleBit |
                                                                             MemoryPropertyFlags.HostCoherentBit)) ==
               MemoryPropertyFlags.HostVisibleBit;
    }

    internal long GetMemoryTypeMinAlignment(int memTypeIndex) {
        return IsMemoryTypeNonCoherent(memTypeIndex)
            ? (long)Math.Max(1, _physicalDeviceProperties.Limits.NonCoherentAtomSize)
            : 1;
    }

    internal void GetBufferMemoryRequirements(Buffer buffer, out MemoryRequirements memReq,
        out bool requiresDedicatedAllocation, out bool prefersDedicatedAllocation) {
        BufferMemoryRequirementsInfo2 req = new() {
            SType = StructureType.BufferMemoryRequirementsInfo2,
            Buffer = buffer,
        };

        MemoryDedicatedRequirements dedicatedRequirements = new() {
            SType = StructureType.MemoryDedicatedRequirements,
        };

        MemoryRequirements2 memReq2 = new() {
            SType = StructureType.MemoryRequirements2,
            PNext = &dedicatedRequirements,
        };

        VkApi.GetBufferMemoryRequirements2(Device, &req, &memReq2);

        memReq = memReq2.MemoryRequirements;
        requiresDedicatedAllocation = dedicatedRequirements.RequiresDedicatedAllocation != 0;
        prefersDedicatedAllocation = dedicatedRequirements.PrefersDedicatedAllocation != 0;
    }

    internal void GetImageMemoryRequirements(Image image, out MemoryRequirements memReq,
        out bool requiresDedicatedAllocation, out bool prefersDedicatedAllocation) {
        ImageMemoryRequirementsInfo2 req = new() {
            SType = StructureType.ImageMemoryRequirementsInfo2,
            Image = image,
        };

        MemoryDedicatedRequirements dedicatedRequirements = new() {
            SType = StructureType.MemoryDedicatedRequirements,
        };

        MemoryRequirements2 memReq2 = new() {
            SType = StructureType.MemoryRequirements2,
            PNext = &dedicatedRequirements,
        };

        VkApi.GetImageMemoryRequirements2(Device, &req, &memReq2);

        memReq = memReq2.MemoryRequirements;
        requiresDedicatedAllocation = dedicatedRequirements.RequiresDedicatedAllocation != 0;
        prefersDedicatedAllocation = dedicatedRequirements.PrefersDedicatedAllocation != 0;
    }

    internal Allocation AllocateMemory(in MemoryRequirements memReq, in DedicatedAllocationInfo dedicatedInfo,
        in AllocationCreateInfo createInfo, SuballocationType suballocType) {
        Debug.Assert(Helpers.IsPow2((long)memReq.Alignment));

        if (memReq.Size == 0)
            throw new ArgumentException("Allocation size cannot be 0");

        const AllocationCreateFlags checkFlags1 =
            AllocationCreateFlags.DedicatedMemory | AllocationCreateFlags.NeverAllocate;
        const AllocationCreateFlags checkFlags2 = AllocationCreateFlags.Mapped | AllocationCreateFlags.CanBecomeLost;

        if ((createInfo.Flags & checkFlags1) == checkFlags1) {
            throw new ArgumentException(
                "Specifying AllocationCreateFlags.DedicatedMemory with AllocationCreateFlags.NeverAllocate is invalid");
        } else if ((createInfo.Flags & checkFlags2) == checkFlags2) {
            throw new ArgumentException(
                "Specifying AllocationCreateFlags.Mapped with AllocationCreateFlags.CanBecomeLost is invalid");
        }

        if (dedicatedInfo.RequiresDedicatedAllocation) {
            if ((createInfo.Flags & AllocationCreateFlags.NeverAllocate) != 0) {
                throw new AllocationException(
                    "AllocationCreateFlags.NeverAllocate specified while dedicated allocation required",
                    Result.ErrorOutOfDeviceMemory);
            }

            if (createInfo.Pool != null) {
                throw new ArgumentException("Pool specified while dedicated allocation required");
            }
        }

        if (createInfo.Pool != null && (createInfo.Flags & AllocationCreateFlags.DedicatedMemory) != 0) {
            throw new ArgumentException(
                "Specified AllocationCreateFlags.DedicatedMemory when createInfo.Pool is not null");
        }

        if (createInfo.Pool != null) {
            int memoryTypeIndex = createInfo.Pool.BlockList.MemoryTypeIndex;
            long alignmentForPool =
                Math.Max((long)memReq.Alignment, GetMemoryTypeMinAlignment(memoryTypeIndex));

            AllocationCreateInfo infoForPool = createInfo;

            if ((createInfo.Flags & AllocationCreateFlags.Mapped) != 0 &&
                (_memoryProperties.MemoryTypes[memoryTypeIndex].PropertyFlags &
                 MemoryPropertyFlags.HostVisibleBit) == 0) {
                infoForPool.Flags &= ~AllocationCreateFlags.Mapped;
            }

            return createInfo.Pool.BlockList.Allocate(CurrentFrameIndex, (long)memReq.Size, alignmentForPool,
                infoForPool, suballocType);
        } else {
            uint memoryTypeBits = memReq.MemoryTypeBits;
            int? typeIndex = FindMemoryTypeIndex(memoryTypeBits, createInfo);

            if (typeIndex == null) {
                throw new AllocationException("Unable to find suitable memory type for allocation",
                    Result.ErrorFeatureNotPresent);
            }

            long alignmentForType =
                Math.Max((long)memReq.Alignment, GetMemoryTypeMinAlignment((int)typeIndex));

            return AllocateMemoryOfType((long)memReq.Size, alignmentForType, in dedicatedInfo, in createInfo,
                (int)typeIndex, suballocType);
        }
    }

    public void FreeMemory(Allocation allocation) {
        if (allocation is null) {
            throw new ArgumentNullException(nameof(allocation));
        }

        if (allocation.TouchAllocation()) {
            if (allocation is BlockAllocation blockAlloc) {
                BlockList list;
                VulkanMemoryPool? pool = blockAlloc.Block.ParentPool;

                if (pool != null) {
                    list = pool.BlockList;
                } else {
                    list = BlockLists[allocation.MemoryTypeIndex];
                    Debug.Assert(list != null);
                }

                list.Free(allocation);
            } else {
                DedicatedAllocation? dedicated = allocation as DedicatedAllocation;

                Debug.Assert(dedicated != null);

                FreeDedicatedMemory(dedicated);
            }
        }

        Budget.RemoveAllocation(MemoryTypeIndexToHeapIndex(allocation.MemoryTypeIndex), allocation.Size);
    }

    public Stats CalculateStats() {
        Stats newStats = new();

        for (int i = 0; i < MemoryTypeCount; ++i) {
            BlockList? list = BlockLists[i];

            Debug.Assert(list != null);

            list.AddStats(newStats);
        }

        _poolsMutex.EnterReadLock();
        try {
            foreach (VulkanMemoryPool? pool in _pools) {
                pool.BlockList.AddStats(newStats);
            }
        }
        finally {
            _poolsMutex.ExitReadLock();
        }

        for (int typeIndex = 0; typeIndex < MemoryTypeCount; ++typeIndex) {
            int heapIndex = MemoryTypeIndexToHeapIndex(typeIndex);

            ref DedicatedAllocationHandler handler = ref DedicatedAllocations[typeIndex];

            handler.Mutex.EnterReadLock();

            try {
                foreach (Allocation? alloc in handler.Allocations) {
                    ((DedicatedAllocation)alloc).CalcStatsInfo(out StatInfo stat);

                    StatInfo.Add(ref newStats.Total, stat);
                    StatInfo.Add(ref newStats.MemoryType[typeIndex], stat);
                    StatInfo.Add(ref newStats.MemoryHeap[heapIndex], stat);
                }
            }
            finally {
                handler.Mutex.ExitReadLock();
            }
        }

        newStats.PostProcess();

        return newStats;
    }

    internal void GetBudget(int heapIndex, out AllocationBudget outBudget) {
        Unsafe.SkipInit(out outBudget);

        if ((uint)heapIndex >= Vk.MaxMemoryHeaps) {
            throw new ArgumentOutOfRangeException(nameof(heapIndex));
        }

        if (UseExtMemoryBudget) {
            //Reworked to remove recursion
            if (Budget.OperationsSinceBudgetFetch >= 30) {
                UpdateVulkanBudget(); //Outside of mutex lock
            }

            Budget.BudgetMutex.EnterReadLock();
            try {
                ref CurrentBudgetData.InternalBudgetStruct heapBudget = ref Budget.BudgetData[heapIndex];

                outBudget.BlockBytes = heapBudget.BlockBytes;
                outBudget.AllocationBytes = heapBudget.AllocationBytes;

                if (heapBudget.VulkanUsage + outBudget.BlockBytes > heapBudget.BlockBytesAtBudgetFetch) {
                    outBudget.Usage = heapBudget.VulkanUsage + outBudget.BlockBytes -
                                      heapBudget.BlockBytesAtBudgetFetch;
                } else {
                    outBudget.Usage = 0;
                }

                outBudget.Budget = Math.Min(heapBudget.VulkanBudget, (long)_memoryProperties.MemoryHeaps[heapIndex].Size);
            }
            finally {
                Budget.BudgetMutex.ExitReadLock();
            }
        } else {
            ref CurrentBudgetData.InternalBudgetStruct heapBudget = ref Budget.BudgetData[heapIndex];

            outBudget.BlockBytes = heapBudget.BlockBytes;
            outBudget.AllocationBytes = heapBudget.AllocationBytes;

            outBudget.Usage = heapBudget.BlockBytes;
            outBudget.Budget = (long)(_memoryProperties.MemoryHeaps[heapIndex].Size * 8 / 10); //80% heuristics
        }
    }

    internal void GetBudget(int firstHeap, AllocationBudget[] outBudgets) {
        Debug.Assert(outBudgets != null! && outBudgets.Length != 0);
        Array.Clear(outBudgets, 0, outBudgets.Length);

        if ((uint)(outBudgets.Length + firstHeap) >= Vk.MaxMemoryHeaps) {
            throw new ArgumentOutOfRangeException();
        }

        if (UseExtMemoryBudget) {
            //Reworked to remove recursion
            if (Budget.OperationsSinceBudgetFetch >= 30) {
                UpdateVulkanBudget(); //Outside of mutex lock
            }

            Budget.BudgetMutex.EnterReadLock();
            try {
                for (int i = 0; i < outBudgets.Length; ++i) {
                    int heapIndex = i + firstHeap;
                    ref AllocationBudget outBudget = ref outBudgets[i];

                    ref CurrentBudgetData.InternalBudgetStruct heapBudget = ref Budget.BudgetData[heapIndex];

                    outBudget.BlockBytes = heapBudget.BlockBytes;
                    outBudget.AllocationBytes = heapBudget.AllocationBytes;

                    if (heapBudget.VulkanUsage + outBudget.BlockBytes > heapBudget.BlockBytesAtBudgetFetch) {
                        outBudget.Usage = heapBudget.VulkanUsage + outBudget.BlockBytes -
                                          heapBudget.BlockBytesAtBudgetFetch;
                    } else {
                        outBudget.Usage = 0;
                    }

                    outBudget.Budget = Math.Min(heapBudget.VulkanBudget, (long)_memoryProperties.MemoryHeaps[heapIndex].Size);
                }
            }
            finally {
                Budget.BudgetMutex.ExitReadLock();
            }
        } else {
            for (int i = 0; i < outBudgets.Length; ++i) {
                int heapIndex = i + firstHeap;
                ref AllocationBudget outBudget = ref outBudgets[i];
                ref CurrentBudgetData.InternalBudgetStruct heapBudget = ref Budget.BudgetData[heapIndex];

                outBudget.BlockBytes = heapBudget.BlockBytes;
                outBudget.AllocationBytes = heapBudget.AllocationBytes;

                outBudget.Usage = heapBudget.BlockBytes;
                outBudget.Budget = (long)(_memoryProperties.MemoryHeaps[heapIndex].Size * 8 / 10); //80% heuristics
            }
        }
    }

    internal Result DefragmentationBegin(in DefragmentationInfo2 info, DefragmentationStats stats,
        DefragmentationContext context) {
        throw new NotImplementedException();
    }

    internal Result DefragmentationEnd(DefragmentationContext context) {
        throw new NotImplementedException();
    }

    internal Result DefragmentationPassBegin(ref DefragmentationPassMoveInfo[] passInfo,
        DefragmentationContext context) {
        throw new NotImplementedException();
    }

    internal Result DefragmentationPassEnd(DefragmentationContext context) {
        throw new NotImplementedException();
    }

    public VulkanMemoryPool CreatePool(in AllocationPoolCreateInfo createInfo) {
        AllocationPoolCreateInfo tmpCreateInfo = createInfo;

        if (tmpCreateInfo.MaxBlockCount == 0) {
            tmpCreateInfo.MaxBlockCount = int.MaxValue;
        }

        if (tmpCreateInfo.MinBlockCount > tmpCreateInfo.MaxBlockCount) {
            throw new ArgumentException("Min block count is higher than max block count");
        }

        if (tmpCreateInfo.MemoryTypeIndex >= MemoryTypeCount ||
            ((1u << tmpCreateInfo.MemoryTypeIndex) & GlobalMemoryTypeBits) == 0) {
            throw new ArgumentException("Invalid memory type index");
        }

        long preferredBlockSize = CalcPreferredBlockSize(tmpCreateInfo.MemoryTypeIndex);

        VulkanMemoryPool pool = new(this, tmpCreateInfo, preferredBlockSize);

        _poolsMutex.EnterWriteLock();
        try {
            _pools.Add(pool);
        }
        finally {
            _poolsMutex.ExitWriteLock();
        }

        return pool;
    }

    internal void DestroyPool(VulkanMemoryPool pool) {
        _poolsMutex.EnterWriteLock();
        try {
            bool success = _pools.Remove(pool);
            Debug.Assert(success, "Pool not found in allocator");
        }
        finally {
            _poolsMutex.ExitWriteLock();
        }
    }

    internal void GetPoolStats(VulkanMemoryPool pool, out PoolStats stats) {
        pool.BlockList.GetPoolStats(out stats);
    }

    internal int MakePoolAllocationsLost(VulkanMemoryPool pool) =>
        pool.BlockList.MakePoolAllocationsLost(CurrentFrameIndex);

    internal Result CheckPoolCorruption(VulkanMemoryPool pool) {
        throw new NotImplementedException();
    }

    internal Allocation CreateLostAllocation() {
        throw new NotImplementedException();
    }

    internal Result AllocateVulkanMemory(in MemoryAllocateInfo allocInfo, out DeviceMemory memory) {
        int heapIndex = MemoryTypeIndexToHeapIndex((int)allocInfo.MemoryTypeIndex);
        ref CurrentBudgetData.InternalBudgetStruct budgetData = ref Budget.BudgetData[heapIndex];

        if ((HeapSizeLimitMask & (1u << heapIndex)) != 0) {
            long heapSize, blockBytes, blockBytesAfterAlloc;

            heapSize = (long)_memoryProperties.MemoryHeaps[heapIndex].Size;

            do {
                blockBytes = budgetData.BlockBytes;
                blockBytesAfterAlloc = blockBytes + (long)allocInfo.AllocationSize;

                if (blockBytesAfterAlloc > heapSize) {
                    throw new AllocationException("Budget limit reached for heap index " + heapIndex,
                        Result.ErrorOutOfDeviceMemory);
                }
            } while (Interlocked.CompareExchange(ref budgetData.BlockBytes, blockBytesAfterAlloc, blockBytes) !=
                     blockBytes);
        } else {
            Interlocked.Add(ref budgetData.BlockBytes, (long)allocInfo.AllocationSize);
        }

        fixed (MemoryAllocateInfo* pInfo = &allocInfo)
        fixed (DeviceMemory* pMemory = &memory) {
            Result res = VkApi.AllocateMemory(Device, pInfo, null, pMemory);

            if (res == Result.Success) {
                Interlocked.Increment(ref Budget.OperationsSinceBudgetFetch);
            } else {
                Interlocked.Add(ref budgetData.BlockBytes, -(long)allocInfo.AllocationSize);
            }

            return res;
        }
    }

    internal void FreeVulkanMemory(int memoryType, long size, DeviceMemory memory) {
        VkApi.FreeMemory(Device, memory, null);

        Interlocked.Add(ref Budget.BudgetData[MemoryTypeIndexToHeapIndex(memoryType)].BlockBytes, -size);
    }

    internal Result BindVulkanBuffer(Buffer buffer, DeviceMemory memory, long offset, void* pNext) {
        if (pNext == null) return VkApi.BindBufferMemory(Device, buffer, memory, (ulong)offset);
        BindBufferMemoryInfo info = new(pNext: pNext, buffer: buffer, memory: memory,
            memoryOffset: (ulong)offset);

        return VkApi.BindBufferMemory2(Device, 1, &info);
    }

    internal Result BindVulkanImage(Image image, DeviceMemory memory, long offset, void* pNext) {
        if (pNext != default) {
            BindImageMemoryInfo info = new() {
                SType = StructureType.BindBufferMemoryInfo,
                PNext = pNext,
                Image = image,
                Memory = memory,
                MemoryOffset = (ulong)offset,
            };

            return VkApi.BindImageMemory2(Device, 1, &info);
        } else {
            return VkApi.BindImageMemory(Device, image, memory, (ulong)offset);
        }
    }

#pragma warning disable CS0162
    [SuppressMessage("ReSharper", "HeuristicUnreachableCode")]
    internal void FillAllocation(Allocation allocation, byte pattern) {
        if (!Helpers.DebugInitializeAllocations || allocation.CanBecomeLost ||
            (_memoryProperties.MemoryTypes[allocation.MemoryTypeIndex].PropertyFlags &
             MemoryPropertyFlags.HostVisibleBit) == 0) return;
        IntPtr pData = allocation.Map();

        Unsafe.InitBlockUnaligned(ref *(byte*)pData, pattern, (uint)allocation.Size);

        FlushOrInvalidateAllocation(allocation, 0, long.MaxValue, CacheOperation.Flush);

        allocation.Unmap();
    }
#pragma warning restore CS0162

    internal uint GetGpuDefragmentationMemoryTypeBits() {
        uint memTypeBits = _gpuDefragmentationMemoryTypeBits;
        if (memTypeBits != uint.MaxValue) return memTypeBits;

        memTypeBits = CalculateGpuDefragmentationMemoryTypeBits();
        _gpuDefragmentationMemoryTypeBits = memTypeBits;

        return memTypeBits;
    }

    private long CalcPreferredBlockSize(int memTypeIndex) {
        int heapIndex = MemoryTypeIndexToHeapIndex(memTypeIndex);

        Debug.Assert((uint)heapIndex < Vk.MaxMemoryHeaps);

        long heapSize = (long)_memoryProperties.MemoryHeaps[heapIndex].Size;

        return Helpers.AlignUp(heapSize <= SmallHeapMaxSize ? heapSize / 8 : _preferredLargeHeapBlockSize,
            32);
    }

    private Allocation AllocateMemoryOfType(long size, long alignment, in DedicatedAllocationInfo dedicatedInfo,
        in AllocationCreateInfo createInfo,
        int memoryTypeIndex, SuballocationType suballocType) {
        AllocationCreateInfo finalCreateInfo = createInfo;

        if ((finalCreateInfo.Flags & AllocationCreateFlags.Mapped) != 0 &&
            (_memoryProperties.MemoryTypes[memoryTypeIndex].PropertyFlags & MemoryPropertyFlags.HostVisibleBit) == 0) {
            finalCreateInfo.Flags &= ~AllocationCreateFlags.Mapped;
        }

        if (finalCreateInfo.Usage == MemoryUsage.GPU_LazilyAllocated) {
            finalCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
        }

        BlockList blockList = BlockLists[memoryTypeIndex];

        long preferredBlockSize = blockList.PreferredBlockSize;
        bool preferDedicatedMemory =
            dedicatedInfo.RequiresDedicatedAllocation | dedicatedInfo.PrefersDedicatedAllocation ||
            size > preferredBlockSize / 2;

        if (preferDedicatedMemory && (finalCreateInfo.Flags & AllocationCreateFlags.NeverAllocate) == 0 &&
            finalCreateInfo.Pool == null) {
            finalCreateInfo.Flags |= AllocationCreateFlags.DedicatedMemory;
        }

        Exception? blockAllocException = null;

        if ((finalCreateInfo.Flags & AllocationCreateFlags.DedicatedMemory) == 0) {
            try {
                return blockList.Allocate(CurrentFrameIndex, size, alignment, finalCreateInfo, suballocType);
            }
            catch (Exception e) {
                blockAllocException = e;
            }
        }

        //Try a dedicated allocation if a block allocation failed, or if specified as a dedicated allocation
        if ((finalCreateInfo.Flags & AllocationCreateFlags.NeverAllocate) != 0) {
            throw new AllocationException(
                "Block List allocation failed, and `AllocationCreateFlags.NeverAllocate` specified",
                blockAllocException);
        }

        return AllocateDedicatedMemory(size, suballocType, memoryTypeIndex,
            (finalCreateInfo.Flags & AllocationCreateFlags.WithinBudget) != 0,
            (finalCreateInfo.Flags & AllocationCreateFlags.Mapped) != 0,
            finalCreateInfo.UserData, in dedicatedInfo);
    }

    private Allocation AllocateDedicatedMemoryPage(
        long size, SuballocationType suballocType, int memTypeIndex, in MemoryAllocateInfo allocInfo, bool map,
        object? userData) {
        Result res = AllocateVulkanMemory(in allocInfo, out DeviceMemory memory);

        if (res < 0) {
            throw new AllocationException("Dedicated memory allocation Failed", res);
        }

        IntPtr mappedData = default;
        if (map) {
            res = VkApi.MapMemory(Device, memory, 0, Vk.WholeSize, 0, (void**)&mappedData);

            if (res < 0) {
                FreeVulkanMemory(memTypeIndex, size, memory);

                throw new AllocationException("Unable to map dedicated allocation", res);
            }
        }

        DedicatedAllocation allocation = new(this, memTypeIndex, memory, suballocType, mappedData, size) {
            UserData = userData,
        };

        Budget.AddAllocation(MemoryTypeIndexToHeapIndex(memTypeIndex), size);

        FillAllocation(allocation, Helpers.AllocationFillPatternCreated);

        return allocation;
    }

    private Allocation AllocateDedicatedMemory(long size, SuballocationType suballocType, int memTypeIndex,
        bool withinBudget, bool map, object? userData, in DedicatedAllocationInfo dedicatedInfo) {
        int heapIndex = MemoryTypeIndexToHeapIndex(memTypeIndex);

        if (withinBudget) {
            GetBudget(heapIndex, out AllocationBudget budget);
            if (budget.Usage + size > budget.Budget) {
                throw new AllocationException("Memory Budget limit reached for heap index " + heapIndex,
                    Result.ErrorOutOfDeviceMemory);
            }
        }

        MemoryAllocateInfo allocInfo = new() {
            SType = StructureType.MemoryAllocateInfo,
            MemoryTypeIndex = (uint)memTypeIndex,
            AllocationSize = (ulong)size,
        };

        Debug.Assert(
            !(dedicatedInfo.DedicatedBuffer.Handle != default && dedicatedInfo.DedicatedImage.Handle != default),
            "dedicated buffer and dedicated image were both specified");

        MemoryDedicatedAllocateInfo dedicatedAllocInfo = new(StructureType.MemoryDedicatedAllocateInfo);

        if (dedicatedInfo.DedicatedBuffer.Handle != default) {
            dedicatedAllocInfo.Buffer = dedicatedInfo.DedicatedBuffer;
            allocInfo.PNext = &dedicatedAllocInfo;
        } else if (dedicatedInfo.DedicatedImage.Handle != default) {
            dedicatedAllocInfo.Image = dedicatedInfo.DedicatedImage;
            allocInfo.PNext = &dedicatedAllocInfo;
        }

        MemoryAllocateFlagsInfoKHR allocFlagsInfo = new(StructureType.MemoryAllocateFlagsInfoKhr);
        if (UseKhrBufferDeviceAddress) {
            bool canContainBufferWithDeviceAddress = true;

            if (dedicatedInfo.DedicatedBuffer.Handle != default) {
                canContainBufferWithDeviceAddress = dedicatedInfo.DedicatedBufferUsage == UnknownBufferUsage
                                                    || (dedicatedInfo.DedicatedBufferUsage &
                                                        BufferUsageFlags.ShaderDeviceAddressBitKhr) != 0;
            } else if (dedicatedInfo.DedicatedImage.Handle != default) {
                canContainBufferWithDeviceAddress = false;
            }

            if (canContainBufferWithDeviceAddress) {
                allocFlagsInfo.Flags = MemoryAllocateFlags.AddressBit;
                allocFlagsInfo.PNext = allocInfo.PNext;
                allocInfo.PNext = &allocFlagsInfo;
            }
        }

        Allocation alloc = AllocateDedicatedMemoryPage(size, suballocType, memTypeIndex, in allocInfo, map, userData);

        //Register made allocations
        ref DedicatedAllocationHandler handler = ref DedicatedAllocations[memTypeIndex];

        handler.Mutex.EnterWriteLock();
        try {
            handler.Allocations.InsertSorted(alloc, (alloc1, alloc2) => alloc1.Offset.CompareTo(alloc2.Offset));
        }
        finally {
            handler.Mutex.ExitWriteLock();
        }

        return alloc;
    }

    private void FreeDedicatedMemory(DedicatedAllocation allocation) {
        ref DedicatedAllocationHandler handler = ref DedicatedAllocations[allocation.MemoryTypeIndex];

        handler.Mutex.EnterWriteLock();

        try {
            bool success = handler.Allocations.Remove(allocation);

            Debug.Assert(success);
        }
        finally {
            handler.Mutex.ExitWriteLock();
        }

        FreeVulkanMemory(allocation.MemoryTypeIndex, allocation.Size, allocation.DeviceMemory);
    }

    private uint CalculateGpuDefragmentationMemoryTypeBits() {
        throw new NotImplementedException();
    }

    private const uint AmdVendorId = 0x1002;

    private uint CalculateGlobalMemoryTypeBits() {
        Debug.Assert(MemoryTypeCount > 0);

        uint memoryTypeBits = uint.MaxValue;

        if (_physicalDeviceProperties.VendorID == AmdVendorId && !UseAmdDeviceCoherentMemory) {
            // Exclude memory types that have VK_MEMORY_PROPERTY_DEVICE_COHERENT_BIT_AMD.
            for (int index = 0; index < MemoryTypeCount; ++index) {
                if ((_memoryProperties.MemoryTypes[index].PropertyFlags &
                     MemoryPropertyFlags.DeviceCoherentBitAmd) != 0) {
                    memoryTypeBits &= ~(1u << index);
                }
            }
        }

        return memoryTypeBits;
    }

    private void UpdateVulkanBudget() {
        Debug.Assert(UseExtMemoryBudget);

        PhysicalDeviceMemoryBudgetPropertiesEXT
            budgetProps = new(StructureType.PhysicalDeviceMemoryBudgetPropertiesExt);

        PhysicalDeviceMemoryProperties2 memProps = new(StructureType.PhysicalDeviceMemoryProperties2, &budgetProps);

        VkApi.GetPhysicalDeviceMemoryProperties2(_physicalDevice, &memProps);

        Budget.BudgetMutex.EnterWriteLock();
        try {
            for (int i = 0; i < MemoryHeapCount; ++i) {
                ref CurrentBudgetData.InternalBudgetStruct data = ref Budget.BudgetData[i];

                data.VulkanUsage = (long)budgetProps.HeapUsage[i];
                data.VulkanBudget = (long)budgetProps.HeapBudget[i];

                data.BlockBytesAtBudgetFetch = data.BlockBytes;

                // Some bugged drivers return the budget incorrectly, e.g. 0 or much bigger than heap size.

                ref MemoryHeap heap = ref _memoryProperties.MemoryHeaps[i];

                if (data.VulkanBudget == 0) {
                    data.VulkanBudget = (long)(heap.Size * 8 / 10);
                } else if ((ulong)data.VulkanBudget > heap.Size) {
                    data.VulkanBudget = (long)heap.Size;
                }

                if (data.VulkanUsage == 0 && data.BlockBytesAtBudgetFetch > 0) {
                    data.VulkanUsage = data.BlockBytesAtBudgetFetch;
                }
            }

            Budget.OperationsSinceBudgetFetch = 0;
        }
        finally {
            Budget.BudgetMutex.ExitWriteLock();
        }
    }

    internal Result FlushOrInvalidateAllocation(Allocation allocation, long offset, long size, CacheOperation op) {
        int memTypeIndex = allocation.MemoryTypeIndex;
        if (size > 0 && IsMemoryTypeNonCoherent(memTypeIndex)) {
            long allocSize = allocation.Size;

            Debug.Assert((ulong)offset <= (ulong)allocSize);

            long nonCoherentAtomSize = (long)_physicalDeviceProperties.Limits.NonCoherentAtomSize;

            MappedMemoryRange memRange = new(memory: allocation.DeviceMemory);

            if (allocation is BlockAllocation blockAlloc) {
                memRange.Offset = (ulong)Helpers.AlignDown(offset, nonCoherentAtomSize);

                if (size == long.MaxValue) {
                    size = allocSize - offset;
                } else {
                    Debug.Assert(offset + size <= allocSize);
                }

                memRange.Size =
                    (ulong)Helpers.AlignUp(size + (offset - (long)memRange.Offset), nonCoherentAtomSize);

                long allocOffset = blockAlloc.Offset;

                Debug.Assert(allocOffset % nonCoherentAtomSize == 0);

                long blockSize = blockAlloc.Block.MetaData.Size;

                memRange.Offset += (ulong)allocOffset;
                memRange.Size = Math.Min(memRange.Size, (ulong)blockSize - memRange.Offset);
            } else if (allocation is DedicatedAllocation) {
                memRange.Offset = (ulong)Helpers.AlignDown(offset, nonCoherentAtomSize);

                if (size == long.MaxValue) {
                    memRange.Size = (ulong)allocSize - memRange.Offset;
                } else {
                    Debug.Assert(offset + size <= allocSize);

                    memRange.Size = (ulong)Helpers.AlignUp(size + (offset - (long)memRange.Offset),
                        nonCoherentAtomSize);
                }
            } else {
                Debug.Assert(false);
                throw new ArgumentException("allocation type is not BlockAllocation or DedicatedAllocation");
            }

            switch (op) {
                case CacheOperation.Flush:
                    return VkApi.FlushMappedMemoryRanges(Device, 1, &memRange);
                case CacheOperation.Invalidate:
                    return VkApi.InvalidateMappedMemoryRanges(Device, 1, &memRange);
                default:
                    Debug.Assert(false);
                    throw new ArgumentException("Invalid Cache Operation value", nameof(op));
            }
        }

        return Result.Success;
    }

    internal struct DedicatedAllocationHandler
    {
        public List<Allocation>     Allocations;
        public ReaderWriterLockSlim Mutex;
    }

    internal struct DedicatedAllocationInfo
    {
        public Buffer           DedicatedBuffer;
        public Image            DedicatedImage;
        public BufferUsageFlags DedicatedBufferUsage; //uint.MaxValue when unknown
        public bool             RequiresDedicatedAllocation;
        public bool             PrefersDedicatedAllocation;

        public static readonly DedicatedAllocationInfo Default = new() {
            DedicatedBufferUsage = unchecked((BufferUsageFlags)uint.MaxValue),
        };
    }
}