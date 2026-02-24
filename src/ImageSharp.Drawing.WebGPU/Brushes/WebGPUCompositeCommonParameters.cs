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

    public readonly int DestinationBufferWidth;

    public readonly int DestinationBufferHeight;

    public readonly int DestinationBufferOriginX;

    public readonly int DestinationBufferOriginY;

    public readonly float BlendPercentage;

    public readonly int ColorBlendingMode;

    public readonly int AlphaCompositionMode;

    public WebGPUCompositeCommonParameters(
        int sourceOffsetX,
        int sourceOffsetY,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight,
        int destinationBufferWidth,
        int destinationBufferHeight,
        int destinationBufferOriginX,
        int destinationBufferOriginY,
        float blendPercentage,
        int colorBlendingMode,
        int alphaCompositionMode)
    {
        this.SourceOffsetX = sourceOffsetX;
        this.SourceOffsetY = sourceOffsetY;
        this.DestinationX = destinationX;
        this.DestinationY = destinationY;
        this.DestinationWidth = destinationWidth;
        this.DestinationHeight = destinationHeight;
        this.DestinationBufferWidth = destinationBufferWidth;
        this.DestinationBufferHeight = destinationBufferHeight;
        this.DestinationBufferOriginX = destinationBufferOriginX;
        this.DestinationBufferOriginY = destinationBufferOriginY;
        this.BlendPercentage = blendPercentage;
        this.ColorBlendingMode = colorBlendingMode;
        this.AlphaCompositionMode = alphaCompositionMode;
    }
}
