// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Brushes;

/// <summary>
/// Creates brush composers for WebGPU composition commands.
/// </summary>
internal static class WebGPUBrushComposerFactory
{
    /// <summary>
    /// Returns whether WebGPU can compose <paramref name="brush"/> directly.
    /// </summary>
    public static bool IsSupportedBrush(Brush brush)
    {
        if (brush is SolidBrush)
        {
            return true;
        }

        return brush is ImageBrush;
    }

    /// <summary>
    /// Creates a brush composer for the given prepared command.
    /// </summary>
    /// <returns>The brush composer.</returns>
    public static IWebGPUBrushComposer Create<TPixel>(
        WebGPUFlushContext flushContext,
        in PreparedCompositionCommand command)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        Guard.NotNull(command.Brush, nameof(command.Brush));

        if (command.Brush is SolidBrush solidBrush)
        {
            return new WebGPUSolidBrushComposer(solidBrush);
        }

        if (command.Brush is ImageBrush imageBrush)
        {
            return WebGPUImageBrushComposer<TPixel>.Create(flushContext, imageBrush, command.BrushBounds);
        }

        throw new InvalidOperationException($"Unexpected brush type '{command.Brush.GetType().FullName}'.");
    }
}
