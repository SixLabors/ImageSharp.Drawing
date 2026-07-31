// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

/// <summary>
/// Owns the managed lifetime shared by asynchronous WebGPU callback thunks.
/// </summary>
internal abstract class WebGPUCallbackLifetime : IDisposable
{
    private readonly object sync = new();
    private GCHandle selfRoot;
    private int pendingInvocationCount;
    private bool disposed;

    /// <summary>
    /// Registers one invocation before its callback pointer is passed to native WebGPU.
    /// </summary>
    public void RegisterInvocation()
    {
        lock (this.sync)
        {
            // Marshal.GetFunctionPointerForDelegate does not root the delegate. Root the callback
            // owner before native code can invoke the pointer, and retain it until disposal and
            // every registered invocation have both completed.
            if (!this.selfRoot.IsAllocated)
            {
                this.selfRoot = GCHandle.Alloc(this);
            }

            this.pendingInvocationCount++;
        }
    }

    /// <summary>
    /// Cancels a registration when native WebGPU did not receive the callback pointer.
    /// </summary>
    public void CancelInvocation()
    {
        lock (this.sync)
        {
            this.pendingInvocationCount--;
            this.ReleaseRootIfRetired();
        }
    }

    /// <summary>
    /// Enters one native callback while excluding concurrent disposal.
    /// </summary>
    /// <returns><see langword="true"/> when the owning managed operation remains active.</returns>
    protected bool EnterInvocation()
    {
        Monitor.Enter(this.sync);
        return !this.disposed;
    }

    /// <summary>
    /// Completes one native callback and releases its self-root when the owner has retired.
    /// </summary>
    protected void ExitInvocation()
    {
        try
        {
            this.pendingInvocationCount--;
            this.ReleaseRootIfRetired();
        }
        finally
        {
            Monitor.Exit(this.sync);
        }
    }

    /// <summary>
    /// Retires the managed callback without invalidating native invocations that are still pending.
    /// </summary>
    public void Dispose()
    {
        lock (this.sync)
        {
            if (this.disposed)
            {
                return;
            }

            // Disposal prevents a late callback from touching state the owner is about to release.
            // The GC root remains until native WebGPU invokes every callback it already accepted.
            this.disposed = true;
            this.ReleaseRootIfRetired();
        }
    }

    /// <summary>
    /// Releases the self-root once neither managed nor native code can require this callback.
    /// </summary>
    private void ReleaseRootIfRetired()
    {
        if (this.disposed && this.pendingInvocationCount == 0 && this.selfRoot.IsAllocated)
        {
            this.selfRoot.Free();
        }
    }
}
