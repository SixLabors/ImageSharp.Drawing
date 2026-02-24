// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;

/// <summary>
/// Defines brush-specific GPU composition behavior.
/// </summary>
internal unsafe interface IWebGPUBrushComposer
{
    /// <summary>
    /// Gets the size in bytes of this composer's instance payload.
    /// </summary>
    public nuint InstanceDataSizeInBytes { get; }

    /// <summary>
    /// Gets or creates the compute pipeline required by this brush composer.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="pipeline">The created or cached compute pipeline.</param>
    /// <param name="error">The error message when pipeline acquisition fails.</param>
    /// <returns><see langword="true"/> if the pipeline is available; otherwise <see langword="false"/>.</returns>
    public bool TryGetOrCreatePipeline(
        WebGPUFlushContext flushContext,
        out ComputePipeline* pipeline,
        out string? error);

    /// <summary>
    /// Writes one brush-specific instance payload into <paramref name="destination"/>.
    /// </summary>
    /// <param name="common">The command values shared by every brush payload.</param>
    /// <param name="destination">The destination bytes for the payload.</param>
    public void WriteInstanceData(in WebGPUCompositeCommonParameters common, Span<byte> destination);

    /// <summary>
    /// Creates the bind group for this brush using the current coverage and destination buffers.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="coverageView">The coverage texture view for the current batch.</param>
    /// <param name="destinationPixelsBuffer">The storage buffer containing destination pixels.</param>
    /// <param name="destinationPixelsByteSize">The byte size of <paramref name="destinationPixelsBuffer"/>.</param>
    /// <param name="instanceOffset">The instance buffer offset.</param>
    /// <param name="instanceBytes">The bound instance byte length.</param>
    /// <returns>The created bind group.</returns>
    public BindGroup* CreateBindGroup(
        WebGPUFlushContext flushContext,
        TextureView* coverageView,
        WgpuBuffer* destinationPixelsBuffer,
        nuint destinationPixelsByteSize,
        nuint instanceOffset,
        nuint instanceBytes);
}
