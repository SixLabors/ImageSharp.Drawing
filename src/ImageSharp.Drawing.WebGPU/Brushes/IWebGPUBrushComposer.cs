// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;

/// <summary>
/// Defines brush-specific GPU composition behavior.
/// </summary>
internal unsafe interface IWebGPUBrushComposer
{
    /// <summary>
    /// Gets or creates the render pipeline required by this brush composer.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="pipeline">The created or cached render pipeline.</param>
    /// <param name="error">The error message when pipeline acquisition fails.</param>
    /// <returns><see langword="true"/> if the pipeline is available; otherwise <see langword="false"/>.</returns>
    public bool TryGetOrCreatePipeline(
        WebGPUFlushContext flushContext,
        out RenderPipeline* pipeline,
        out string? error);

    /// <summary>
    /// Populates brush-specific fields in the shared composite instance payload.
    /// </summary>
    /// <param name="instance">The instance payload to update.</param>
    public void PopulateInstanceData(ref WebGPUCompositeInstanceData instance);

    /// <summary>
    /// Creates the bind group for this brush using the current coverage and instance buffers.
    /// </summary>
    /// <param name="flushContext">The active WebGPU flush context.</param>
    /// <param name="coverageView">The coverage texture view for the current batch.</param>
    /// <param name="instanceOffset">The instance buffer offset.</param>
    /// <param name="instanceBytes">The bound instance byte length.</param>
    /// <returns>The created bind group.</returns>
    public BindGroup* CreateBindGroup(
        WebGPUFlushContext flushContext,
        TextureView* coverageView,
        nuint instanceOffset,
        nuint instanceBytes);
}
