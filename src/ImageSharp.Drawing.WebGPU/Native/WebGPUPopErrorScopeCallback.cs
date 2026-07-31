// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

/// <summary>
/// Keeps a managed error-scope callback rooted for every invocation accepted by native WebGPU.
/// </summary>
internal sealed unsafe class WebGPUPopErrorScopeCallback : WebGPUCallbackLifetime
{
    private readonly Callback callback;
    private readonly NativeCallback nativeCallback;
    private readonly nint pointer;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUPopErrorScopeCallback"/> class.
    /// </summary>
    /// <param name="callback">The managed callback to invoke while its owner remains active.</param>
    private WebGPUPopErrorScopeCallback(Callback callback)
    {
        this.callback = callback;
        this.nativeCallback = this.Invoke;
        this.pointer = Marshal.GetFunctionPointerForDelegate(this.nativeCallback);
    }

    /// <summary>
    /// Represents the managed error-scope completion callback.
    /// </summary>
    /// <param name="status">The error-scope request status.</param>
    /// <param name="errorType">The captured error type, or <see cref="WGPUErrorType.NoError"/>.</param>
    /// <param name="message">The borrowed native diagnostic message.</param>
    /// <param name="userData">The caller-provided context pointer.</param>
    public delegate void Callback(WGPUPopErrorScopeStatus status, WGPUErrorType errorType, WGPUStringView message, void* userData);

    /// <summary>
    /// Matches the complete native <c>WGPUPopErrorScopeCallback</c> ABI declared by webgpu.h.
    /// </summary>
    /// <param name="status">The error-scope request status.</param>
    /// <param name="errorType">The captured error type.</param>
    /// <param name="message">The borrowed native diagnostic message.</param>
    /// <param name="userdata1">The first caller-provided context pointer.</param>
    /// <param name="userdata2">The second caller-provided context pointer.</param>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeCallback(
        WGPUPopErrorScopeStatus status,
        WGPUErrorType errorType,
        WGPUStringView message,
        void* userdata1,
        void* userdata2);

    /// <summary>
    /// Gets the unmanaged callback pointer stored in a WebGPU callback-info structure.
    /// </summary>
    public delegate* unmanaged[Cdecl]<WGPUPopErrorScopeStatus, WGPUErrorType, WGPUStringView, void*, void*, void> Pointer
        => (delegate* unmanaged[Cdecl]<WGPUPopErrorScopeStatus, WGPUErrorType, WGPUStringView, void*, void*, void>)this.pointer;

    /// <summary>
    /// Creates a rooted callback thunk.
    /// </summary>
    /// <param name="callback">The managed callback to invoke.</param>
    /// <returns>The rooted callback thunk.</returns>
    public static WebGPUPopErrorScopeCallback From(Callback callback) => new(callback);

    /// <summary>
    /// Dispatches one native completion while preventing concurrent owner disposal.
    /// </summary>
    /// <param name="status">The error-scope request status.</param>
    /// <param name="errorType">The captured error type.</param>
    /// <param name="message">The borrowed native diagnostic message.</param>
    /// <param name="userdata1">The caller-provided context pointer.</param>
    /// <param name="userdata2">The second native context pointer, which this wrapper does not use.</param>
    private void Invoke(
        WGPUPopErrorScopeStatus status,
        WGPUErrorType errorType,
        WGPUStringView message,
        void* userdata1,
        void* userdata2)
    {
        _ = userdata2;
        bool invokeCallback = this.EnterInvocation();

        try
        {
            // The message string is borrowed for this invocation, so the managed callback must
            // copy it before returning to native WebGPU.
            if (invokeCallback)
            {
                this.callback(status, errorType, message, userdata1);
            }
        }
        finally
        {
            this.ExitInvocation();
        }
    }
}
