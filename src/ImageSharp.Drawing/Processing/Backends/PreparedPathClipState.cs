// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Holds flush-scoped raster data for path clip descriptors.
/// </summary>
internal sealed class PreparedPathClipState : IDisposable
{
    private readonly DefaultRasterizer.RasterizableGeometry?[] rasterizables;

    private PreparedPathClipState(DefaultRasterizer.RasterizableGeometry?[] rasterizables)
        => this.rasterizables = rasterizables;

    /// <summary>
    /// Creates retained path clip raster data for the path descriptors in a clip state.
    /// </summary>
    /// <param name="clipState">The command clip state.</param>
    /// <param name="targetBounds">The absolute command target bounds.</param>
    /// <param name="destinationOffset">The destination offset used to place clip descriptors.</param>
    /// <param name="allocator">The allocator used for retained raster storage.</param>
    /// <param name="prepared">The prepared path clip state, or <see langword="null"/> when the clip state has no path clips.</param>
    /// <returns><see langword="true"/> when the command can still draw after preparing clips; otherwise <see langword="false"/>.</returns>
    public static bool TryCreate(
        DrawingClipState clipState,
        Rectangle targetBounds,
        Point destinationOffset,
        MemoryAllocator allocator,
        out PreparedPathClipState? prepared)
    {
        prepared = null;
        int count = clipState.Count;
        int pathCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (clipState.GetDescriptor(i).Kind == DrawingClipKind.Path)
            {
                pathCount++;
            }
        }

        if (pathCount == 0)
        {
            return true;
        }

        DefaultRasterizer.RasterizableGeometry?[] rasterizables = new DefaultRasterizer.RasterizableGeometry?[count];
        try
        {
            for (int i = 0; i < count; i++)
            {
                DrawingClipDescriptor descriptor = clipState.GetDescriptor(i);
                if (descriptor.Kind != DrawingClipKind.Path)
                {
                    continue;
                }

                LinearGeometry geometry = descriptor.ToLinearGeometry(out Matrix4x4 residual);
                RasterizationMode mode = descriptor.EdgeMode == DrawingClipEdgeMode.Hard
                    ? RasterizationMode.Aliased
                    : RasterizationMode.Antialiased;

                RasterizerOptions options = new(
                    targetBounds,
                    descriptor.IntersectionRule,
                    mode,
                    descriptor.AntialiasThreshold);

                DefaultRasterizer.RasterizableGeometry? rasterizable = DefaultRasterizer.CreateRasterizableGeometry(
                    geometry,
                    residual,
                    destinationOffset.X,
                    destinationOffset.Y,
                    options,
                    allocator);

                // An empty intersecting path clip makes the whole command empty. An empty
                // difference clip is a no-op, so it remains represented by a null rasterizable.
                if (rasterizable is null && descriptor.Operation == ClipOperation.Intersection)
                {
                    return false;
                }

                rasterizables[i] = rasterizable;
            }

            prepared = new PreparedPathClipState(rasterizables);
            rasterizables = [];
            return true;
        }
        finally
        {
            for (int i = 0; i < rasterizables.Length; i++)
            {
                rasterizables[i]?.Dispose();
            }
        }
    }

    /// <summary>
    /// Gets retained raster data for the path descriptor at the clip index.
    /// </summary>
    /// <param name="clipIndex">The clip descriptor index.</param>
    /// <returns>The retained path raster data, or <see langword="null"/> when the descriptor is an empty difference clip.</returns>
    public DefaultRasterizer.RasterizableGeometry? GetRasterizable(int clipIndex)
        => this.rasterizables[clipIndex];

    /// <inheritdoc/>
    public void Dispose()
    {
        for (int i = 0; i < this.rasterizables.Length; i++)
        {
            this.rasterizables[i]?.Dispose();
        }
    }
}
