// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;

/// <summary>
/// Common per-command composition values shared by all brush composers.
/// </summary>
internal readonly struct WebGPUCompositeCommonParameters
{
    public readonly int SourceOffsetX;

    public readonly int SourceOffsetY;

    public readonly int DestinationX;

    public readonly int DestinationY;

    public readonly int DestinationWidth;

    public readonly int DestinationHeight;

    public readonly int TargetWidth;

    public readonly int TargetHeight;

    public readonly float BlendPercentage;

    public WebGPUCompositeCommonParameters(
        int sourceOffsetX,
        int sourceOffsetY,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        int targetWidth,
        int targetHeight,
        float blendPercentage)
    {
        this.SourceOffsetX = sourceOffsetX;
        this.SourceOffsetY = sourceOffsetY;
        this.DestinationX = destinationX;
        this.DestinationY = destinationY;
        this.DestinationWidth = destinationWidth;
        this.DestinationHeight = destinationHeight;
        this.TargetWidth = targetWidth;
        this.TargetHeight = targetHeight;
        this.BlendPercentage = blendPercentage;
    }
}
