// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

/// <summary>
/// Keeps a managed adapter-request callback rooted for every invocation accepted by native WebGPU.
/// </summary>
internal sealed unsafe class WebGPURequestAdapterCallback : WebGPUCallbackLifetime
{
    private readonly Callback callback;
    private readonly Callback abandonedCallback;
    private readonly NativeCallback nativeCallback;
    private readonly nint pointer;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPURequestAdapterCallback"/> class.
    /// </summary>
    /// <param name="callback">The managed callback to invoke while its owner remains active.</param>
    /// <param name="abandonedCallback">The callback that releases a result arriving after owner retirement.</param>
    private WebGPURequestAdapterCallback(Callback callback, Callback abandonedCallback)
    {
        this.callback = callback;
        this.abandonedCallback = abandonedCallback;
        this.nativeCallback = this.Invoke;
        this.pointer = Marshal.GetFunctionPointerForDelegate(this.nativeCallback);
    }

    /// <summary>
    /// Represents the managed adapter-request completion callback.
    /// </summary>
    /// <param name="status">The request status.</param>
    /// <param name="adapter">The requested adapter, or <see langword="null"/> on failure.</param>
    /// <param name="message">The native diagnostic message.</param>
    /// <param name="userData">The caller-provided context pointer.</param>
    public delegate void Callback(
        WGPURequestAdapterStatus status,
        WGPUAdapterImpl* adapter,
        WGPUStringView message,
        void* userData);

    /// <summary>
    /// Matches the complete native <c>WGPURequestAdapterCallback</c> ABI declared by webgpu.h.
    /// </summary>
    /// <param name="status">The request status.</param>
    /// <param name="adapter">The returned adapter.</param>
    /// <param name="message">The native diagnostic message.</param>
    /// <param name="userdata1">The first caller-provided context pointer.</param>
    /// <param name="userdata2">The second caller-provided context pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeCallback(
        WGPURequestAdapterStatus status,
        WGPUAdapterImpl* adapter,
        WGPUStringView message,
        void* userdata1,
        void* userdata2);

    /// <summary>
    /// Gets the unmanaged callback pointer stored in a WebGPU callback-info structure.
    /// </summary>
    public delegate* unmanaged[Cdecl]<WGPURequestAdapterStatus, WGPUAdapterImpl*, WGPUStringView, void*, void*, void> Pointer
        => (delegate* unmanaged[Cdecl]<WGPURequestAdapterStatus, WGPUAdapterImpl*, WGPUStringView, void*, void*, void>)this.pointer;

    /// <summary>
    /// Creates a rooted callback thunk.
    /// </summary>
    /// <param name="callback">The managed callback to invoke.</param>
    /// <param name="abandonedCallback">The callback that releases a result arriving after managed retirement.</param>
    /// <returns>The rooted callback thunk.</returns>
    public static WebGPURequestAdapterCallback From(Callback callback, Callback abandonedCallback) => new(callback, abandonedCallback);

    /// <summary>
    /// Dispatches one native completion while preventing concurrent owner disposal.
    /// </summary>
    /// <param name="status">The request status.</param>
    /// <param name="adapter">The returned adapter.</param>
    /// <param name="message">The native diagnostic message.</param>
    /// <param name="userdata1">The caller-provided context pointer.</param>
    /// <param name="userdata2">The second native context pointer, which this wrapper does not use.</param>
    private void Invoke(
        WGPURequestAdapterStatus status,
        WGPUAdapterImpl* adapter,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        _ = userdata2;
        bool invokeCallback = this.EnterInvocation();

        try
        {
            if (invokeCallback)
            {
                this.callback(status, adapter, message, userdata1);
            }
            else
            {
                // Adapter results are returned with ownership. A request that completes after its
                // managed timeout has no caller left to receive that ownership, so the dedicated
                // abandonment path must release any returned handle.
                this.abandonedCallback(status, adapter, message, userdata1);
            }
        }
        finally
        {
            this.ExitInvocation();
        }
    }
}
