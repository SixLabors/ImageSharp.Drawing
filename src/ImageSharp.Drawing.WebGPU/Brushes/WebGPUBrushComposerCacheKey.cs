// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;

/// <summary>
/// Batch-local brush composer cache key.
/// </summary>
internal readonly struct WebGPUBrushComposerCacheKey : IEquatable<WebGPUBrushComposerCacheKey>
{
    private readonly Brush brush;
    private readonly Rectangle brushBounds;
    private readonly bool includeBrushBounds;

    public WebGPUBrushComposerCacheKey(Brush brush, in Rectangle brushBounds, bool includeBrushBounds)
    {
        this.brush = brush;
        this.brushBounds = brushBounds;
        this.includeBrushBounds = includeBrushBounds;
    }

    public bool Equals(WebGPUBrushComposerCacheKey other)
    {
        if (!ReferenceEquals(this.brush, other.brush) ||
            this.includeBrushBounds != other.includeBrushBounds)
        {
            return false;
        }

        return !this.includeBrushBounds || this.brushBounds.Equals(other.brushBounds);
    }

    public override bool Equals(object? obj) => obj is WebGPUBrushComposerCacheKey other && this.Equals(other);

    public override int GetHashCode()
    {
        int brushHash = RuntimeHelpers.GetHashCode(this.brush);
        return this.includeBrushBounds
            ? HashCode.Combine(brushHash, this.brushBounds, this.includeBrushBounds)
            : HashCode.Combine(brushHash, this.includeBrushBounds);
    }
}
