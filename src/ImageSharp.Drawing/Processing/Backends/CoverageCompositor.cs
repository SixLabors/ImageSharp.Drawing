// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Shared CPU compositing helpers for prepared coverage maps.
/// </summary>
internal static class CoverageCompositor
{
    public static bool TryGetCompositeRegions<TPixel, TCoverage>(
        Buffer2DRegion<TPixel> target,
        Buffer2D<TCoverage> sourceBuffer,
        Point sourceOffset,
        out Buffer2DRegion<TPixel> destinationRegion,
        out Buffer2DRegion<TCoverage> sourceRegion)
        where TPixel : unmanaged, IPixel<TPixel>
        where TCoverage : unmanaged
    {
        destinationRegion = default;
        sourceRegion = default;

        if (target.Width <= 0 || target.Height <= 0)
        {
            return false;
        }

        if ((uint)sourceOffset.X >= (uint)sourceBuffer.Width || (uint)sourceOffset.Y >= (uint)sourceBuffer.Height)
        {
            return false;
        }

        int compositeWidth = Math.Min(target.Width, sourceBuffer.Width - sourceOffset.X);
        int compositeHeight = Math.Min(target.Height, sourceBuffer.Height - sourceOffset.Y);
        if (compositeWidth <= 0 || compositeHeight <= 0)
        {
            return false;
        }

        sourceRegion = new Buffer2DRegion<TCoverage>(
            sourceBuffer,
            new Rectangle(sourceOffset.X, sourceOffset.Y, compositeWidth, compositeHeight));
        destinationRegion = target.GetSubRegion(0, 0, compositeWidth, compositeHeight);
        return true;
    }

    public static void CompositeFloatCoverage<TPixel>(
        Configuration configuration,
        Buffer2DRegion<TPixel> destinationRegion,
        Buffer2DRegion<float> sourceRegion,
        Brush brush,
        in GraphicsOptions graphicsOptions,
        Rectangle brushBounds)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using BrushApplicator<TPixel> applicator = brush.CreateApplicator(
            configuration,
            graphicsOptions,
            destinationRegion,
            brushBounds);

        int absoluteX = destinationRegion.Rectangle.X;
        int absoluteY = destinationRegion.Rectangle.Y;
        for (int row = 0; row < sourceRegion.Height; row++)
        {
            applicator.Apply(sourceRegion.DangerousGetRowSpan(row), absoluteX, absoluteY + row);
        }
    }
}
