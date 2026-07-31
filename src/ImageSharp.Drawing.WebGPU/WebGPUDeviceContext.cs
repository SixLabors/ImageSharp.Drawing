// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Owns the WebGPU device-scoped drawing state shared by render targets, surfaces, shader programs, and pipelines.
/// </summary>
public sealed class WebGPUDeviceContext
{
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext"/> class.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    internal WebGPUDeviceContext(Configuration configuration)
    {
        Guard.NotNull(configuration, nameof(configuration));

        this.Backend = new WebGPUDrawingBackend();

        try
        {
            if (!WebGPURuntime.TryGetOrCreateDevice(
                    out WebGPUDeviceHandle? deviceHandle,
                    out WebGPUQueueHandle? queueHandle,
                    out WebGPUEnvironmentError errorCode)
                || deviceHandle is null
                || queueHandle is null)
            {
                throw new InvalidOperationException(WebGPURuntime.CreateEnvironmentExceptionMessage(errorCode));
            }

            this.DeviceHandle = deviceHandle;
            this.QueueHandle = queueHandle;
            this.Configuration = configuration;

            // Device-scoped shared state owns the uncaptured-error callback. Install it now,
            // matching the wrapped-handle constructor, so GPU errors raised before the first
            // flush or readback are still reported instead of silently dropped.
            _ = WebGPURuntime.GetOrCreateDeviceState(WebGPURuntime.GetApi(), deviceHandle);
        }
        catch
        {
            this.Backend.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDeviceContext"/> class over already-wrapped device and queue handles.
    /// </summary>
    /// <param name="configuration">The configuration instance to bind to the created backend.</param>
    /// <param name="deviceHandle">The wrapped WebGPU device handle.</param>
    /// <param name="queueHandle">The wrapped WebGPU queue handle.</param>
    /// <remarks>
    /// The context stores the handles without taking ownership; native lifetime is controlled
    /// by the handle wrappers themselves.
    /// </remarks>
    internal WebGPUDeviceContext(Configuration configuration, WebGPUDeviceHandle deviceHandle, WebGPUQueueHandle queueHandle)
    {
        Guard.NotNull(configuration, nameof(configuration));
        Guard.NotNull(deviceHandle, nameof(deviceHandle));
        Guard.NotNull(queueHandle, nameof(queueHandle));

        this.DeviceHandle = deviceHandle;
        this.QueueHandle = queueHandle;

        // Device-scoped shared state owns the uncaptured-error callback, so create it
        // before any later surface or render-target work can report native validation errors.
        _ = WebGPURuntime.GetOrCreateDeviceState(WebGPURuntime.GetApi(), deviceHandle);

        this.Backend = new WebGPUDrawingBackend();
        this.Configuration = configuration;
    }

    /// <summary>
    /// Gets the configuration provided when the context was created.
    /// </summary>
    public Configuration Configuration { get; }

    /// <summary>
    /// Gets the WebGPU drawing backend owned by this context.
    /// Use this to inspect per-flush diagnostics for chunked rendering.
    /// </summary>
    public WebGPUDrawingBackend Backend { get; }

    /// <summary>
    /// Gets the wrapped WebGPU device handle used by frames, canvases, and render-target allocation created from this context.
    /// </summary>
    internal WebGPUDeviceHandle DeviceHandle { get; }

    /// <summary>
    /// Gets the wrapped WebGPU queue handle paired with <see cref="DeviceHandle"/> for uploads, readback, and command submission.
    /// </summary>
    internal WebGPUQueueHandle QueueHandle { get; }

    /// <summary>
    /// Compiles and caches every pipeline used by a shader effect on this WebGPU device.
    /// </summary>
    /// <param name="effect">The shader effect to compile.</param>
    /// <remarks>
    /// Call this before the effect's first rendered frame when shader compilation latency must not occur during presentation.
    /// </remarks>
    public void Precompile(IWebGPUShaderEffect effect)
    {
        this.ThrowIfDisposed();
        Guard.NotNull(effect, nameof(effect));

        if (effect is not IWebGPUShaderEffectSource effectSource)
        {
            throw new ArgumentException(
                "Shader effects must derive from WebGPUShaderLayerEffect or WebGPUBackdropShaderLayerEffect.",
                nameof(effect));
        }

        WebGPURuntime.DeviceSharedState deviceState = WebGPURuntime.GetOrCreateDeviceState(WebGPURuntime.GetApi(), this.DeviceHandle);

        // Programs are specialized only for the source's numeric encoding and alpha association.
        // Precompile all four semantic combinations once so this device-scoped operation is valid
        // for every offscreen or presentation target without exposing internal texture descriptors.
        ReadOnlySpan<WebGPUTargetDescriptor> sourceDescriptors =
        [
            new(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.Unit),
            new(WebGPUTextureFormat.Rgba8Unorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.Unit),
            new(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Unassociated, WebGPUTargetNumericEncoding.SignedUnit),
            new(WebGPUTextureFormat.Rgba8Snorm, PixelAlphaRepresentation.Associated, WebGPUTargetNumericEncoding.SignedUnit)
        ];

        foreach (WebGPUTargetDescriptor sourceDescriptor in sourceDescriptors)
        {
            deviceState.PrecompileEffect(effectSource, sourceDescriptor);
        }
    }

    /// <summary>
    /// Disposes the drawing backend owned by this context.
    /// </summary>
    internal void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.Backend.Dispose();
        this.isDisposed = true;
    }

    /// <summary>
    /// Throws when the context is disposed.
    /// </summary>
    internal void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(this.isDisposed, this);
}
