// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Receives one emitted non-zero coverage span from the rasterizer.
/// </summary>
/// <remarks>
/// Implementations are value types invoked through a <c>struct</c> generic constraint so calls
/// devirtualize and inline on the hot path. The rasterizer executes row bands in parallel, but a
/// handler instance is only ever used by the single worker that owns it, so implementations do
/// not need to be thread safe.
/// </remarks>
internal interface IRasterizerCoverageRowHandler
{
    /// <summary>
    /// Handles one emitted non-zero coverage span.
    /// </summary>
    /// <param name="y">The destination y coordinate.</param>
    /// <param name="startX">The first x coordinate represented by <paramref name="coverage"/>.</param>
    /// <param name="coverage">
    /// Positive per-pixel coverage values starting at <paramref name="startX"/>. The span aliases
    /// reusable worker scratch and is only valid for the duration of the call.
    /// </param>
    public void Handle(int y, int startX, Span<float> coverage);
}
