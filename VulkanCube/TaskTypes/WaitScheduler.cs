using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Silk.NET.Vulkan;

namespace VulkanCube.TaskTypes;

public class WaitScheduler : IDisposable
{
    private static Vk VkApi => InstanceCreationExample.VkApi;

    private readonly ConcurrentQueue<(Fence, TaskCompletionSource)> fenceQueue = new();

    private readonly Thread schedulerThread;

    private readonly Device device;

    private volatile int state = 0;

    public WaitScheduler(Device device)
    {
        this.device = device;

        schedulerThread = new Thread(ScheduleThreadMethod);
        schedulerThread.Start();
    }

    public Task WaitForFenceAsync(Fence fence)
    {
        var res = VkApi.GetFenceStatus(device, fence);

        switch (res)
        {
            case Result.Success:
                return Task.CompletedTask;
            case Result.NotReady:
                CheckState();

                var tcs = new TaskCompletionSource();

                fenceQueue.Enqueue((fence, tcs));

                return tcs.Task;
            default:
                throw new Exception(res.ToString());
        }
    }

    private void CheckState()
    {
        var tmp = state;

        if (tmp != 0)
        {
            throw new InvalidOperationException($"{(Result)tmp}");
        }
    }

    private unsafe void ScheduleThreadMethod()
    {
        var list = new UnmanagedList<Fence>(64);
        var taskList = new List<TaskCompletionSource>(64);
        Result res;

        while (state == 0)
        {
            while (fenceQueue.TryDequeue(out var tuple))
            {
                var (fence, tcs) = tuple;

                res = VkApi.GetFenceStatus(device, fence);

                switch (res)
                {
                    case Result.Success:
                        tcs.SetResult();

                        break;
                    case Result.NotReady:
                        list.Add(fence);
                        taskList.Add(tcs);

                        break;
                    default:
                        tcs.SetException(new Exception("VkGetFenceStatus() returned " + res));

                        break;
                }
            }

            if (list.Count == 0)
            {
                Thread.Sleep(1);

                continue;
            }

            res = VkApi.WaitForFences(device, (uint)list.Count, list.BasePointer, false, 5 * 1_000_000);

            switch (res)
            {
                case Result.Timeout:
                    break;
                case Result.Success:

                    for (var i = 0; i < list.Count;)
                    {
                        var fence = list[i];
                        var source = taskList[i];

                        res = VkApi.GetFenceStatus(device, fence);

                        if (res != Result.NotReady)
                        {
                            list.RemoveAt(i);
                            taskList.RemoveAt(i);

                            if (res == Result.Success)
                            {
                                source.SetResult();
                            }
                            else
                            {
                                source.SetException(new Exception("VkGetFenceStatus() returned " + res));
                            }

                            continue; //Skip index increment
                        }

                        i += 1;
                    }

                    break;
                default:
                    state = (int)res;

                    var resMessage = "Unhandled Error: " + res;

                    foreach (var source in taskList)
                    {
                        source.SetException(new Exception(resMessage));
                    }

                    break;
            }
        }
    }

    public void Dispose()
    {
        state = 1;

        schedulerThread.Join();
    }

    private struct UnmanagedList<T> where T : unmanaged
    {
        private T[] array;

        public UnmanagedList(int capacity)
        {
            array = GC.AllocateArray<T>(capacity, true);
            Count = 0;
        }

        public int Count { get; private set; }

        public unsafe T* BasePointer => (T*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(array));

        public T this[int idx] => array[idx];

        public void Add(T item)
        {
            if (Count >= array.Length)
            {
                var newlen = array.Length * 2;

                var newArr = GC.AllocateArray<T>(newlen, true);

                Array.Copy(array, newArr, array.Length);

                array = newArr;
            }

            array[Count++] = item;
        }

        public void RemoveAt(int idx)
        {
            if ((uint)idx >= (uint)Count)
            {
                throw new IndexOutOfRangeException();
            }

            if (idx != Count - 1)
            {
                Array.Copy(array, idx + 1, array, idx, Count - idx - 1);
            }

            Count -= 1;
        }
    }
}
