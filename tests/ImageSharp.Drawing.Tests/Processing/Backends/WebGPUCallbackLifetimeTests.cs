// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public unsafe class WebGPUCallbackLifetimeTests
{
    [Fact]
    public void BufferMapCallback_RemainsRootedAndSuppressesManagedCallbackAfterDisposal()
    {
        StrongBox<int> callbackCount = new();
        nint pointerAddress = CreateRetiredBufferMapCallback(callbackCount, out WeakReference callbackReference);

        // The test intentionally retains only the unmanaged function pointer and a weak reference.
        // A collection here reproduces the native-timeout boundary where the managed owner has
        // returned but WebGPU still owns one pending callback invocation.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(callbackReference.IsAlive);

        delegate* unmanaged[Cdecl]<WGPUMapAsyncStatus, WGPUStringView, void*, void*, void> pointer =
            (delegate* unmanaged[Cdecl]<WGPUMapAsyncStatus, WGPUStringView, void*, void*, void>)pointerAddress;

        pointer(WGPUMapAsyncStatus.Success, default, null, null);

        Assert.Equal(0, callbackCount.Value);

        // The callback invocation retires native ownership. Once it returns, neither the managed
        // operation nor native WebGPU owns the wrapper, so its self-root must be released.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(callbackReference.IsAlive);
    }

    [Fact]
    public void Dispose_WaitsForAlreadyEnteredCallback()
    {
        using ManualResetEventSlim callbackEntered = new(false);
        using ManualResetEventSlim releaseCallback = new(false);
        using ManualResetEventSlim disposeCompleted = new(false);
        WebGPUQueueWorkDoneCallback callback = WebGPUQueueWorkDoneCallback.From((_, _) =>
        {
            callbackEntered.Set();
            releaseCallback.Wait();
        });

        callback.RegisterInvocation();
        Thread callbackThread = new(() => callback.Pointer(WGPUQueueWorkDoneStatus.Success, default, null, null)) { IsBackground = true };
        Thread disposeThread = new(() =>
        {
            callback.Dispose();
            disposeCompleted.Set();
        })
        {
            IsBackground = true
        };

        callbackThread.Start();
        bool disposeThreadStarted = false;

        try
        {
            Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)));

            disposeThread.Start();
            disposeThreadStarted = true;

            // EnterInvocation holds the callback's lifetime monitor through the managed callback.
            // Observing the disposal thread blocked on that monitor proves Dispose cannot return while
            // the callback may still be accessing its owner's event or captured state.
            Assert.True(SpinWait.SpinUntil(
                () => (disposeThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5)));

            Assert.False(disposeCompleted.IsSet);
            releaseCallback.Set();

            Assert.True(callbackThread.Join(TimeSpan.FromSeconds(5)));
            Assert.True(disposeThread.Join(TimeSpan.FromSeconds(5)));
            Assert.True(disposeCompleted.IsSet);
        }
        finally
        {
            // Failed assertions must not leave either background thread holding the callback root.
            releaseCallback.Set();
            _ = callbackThread.Join(TimeSpan.FromSeconds(5));

            if (disposeThreadStarted)
            {
                _ = disposeThread.Join(TimeSpan.FromSeconds(5));
            }
            else
            {
                callback.Dispose();
            }
        }
    }

    [Fact]
    public void QueueWorkDoneCallback_SuppressesManagedCallbackAfterDisposal()
    {
        int callbackCount = 0;
        WebGPUQueueWorkDoneCallback callback = WebGPUQueueWorkDoneCallback.From((_, _) => callbackCount++);
        callback.RegisterInvocation();
        callback.Dispose();

        callback.Pointer(WGPUQueueWorkDoneStatus.Success, default, null, null);

        Assert.Equal(0, callbackCount);
    }

    [Fact]
    public void RequestAdapterCallback_RoutesLateOwnedResultToAbandonmentCallback()
    {
        int callbackCount = 0;
        WGPUAdapterImpl* abandonedAdapter = null;
        WebGPURequestAdapterCallback callback = WebGPURequestAdapterCallback.From(
            (_, _, _, _) => callbackCount++,
            (_, adapter, _, _) => abandonedAdapter = adapter);

        callback.RegisterInvocation();
        callback.Dispose();

        WGPUAdapterImpl* expectedAdapter = (WGPUAdapterImpl*)1;
        callback.Pointer(WGPURequestAdapterStatus.Success, expectedAdapter, default, null, null);

        Assert.Equal(0, callbackCount);
        Assert.Equal((nint)expectedAdapter, (nint)abandonedAdapter);
    }

    [Fact]
    public void RequestDeviceCallback_RoutesLateOwnedResultToAbandonmentCallback()
    {
        int callbackCount = 0;
        WGPUDeviceImpl* abandonedDevice = null;
        WebGPURequestDeviceCallback callback = WebGPURequestDeviceCallback.From(
            (_, _, _, _) => callbackCount++,
            (_, device, _, _) => abandonedDevice = device);

        callback.RegisterInvocation();
        callback.Dispose();

        WGPUDeviceImpl* expectedDevice = (WGPUDeviceImpl*)1;
        callback.Pointer(WGPURequestDeviceStatus.Success, expectedDevice, default, null, null);

        Assert.Equal(0, callbackCount);
        Assert.Equal((nint)expectedDevice, (nint)abandonedDevice);
    }

    /// <summary>
    /// Creates a buffer-map callback whose managed owner has retired while one native invocation
    /// remains outstanding.
    /// </summary>
    /// <param name="callbackCount">Receives any incorrect managed invocation after retirement.</param>
    /// <param name="callbackReference">Receives a weak reference used to verify native ownership keeps the thunk rooted.</param>
    /// <returns>The unmanaged callback pointer retained by the simulated native operation.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nint CreateRetiredBufferMapCallback(StrongBox<int> callbackCount, out WeakReference callbackReference)
    {
        WebGPUBufferMapCallback callback = WebGPUBufferMapCallback.From((_, _) => callbackCount.Value++);
        callback.RegisterInvocation();
        callbackReference = new WeakReference(callback);
        nint pointer = (nint)callback.Pointer;
        callback.Dispose();
        return pointer;
    }
}
