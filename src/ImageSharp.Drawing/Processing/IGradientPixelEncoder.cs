// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Encodes an interpolated gradient color as a destination pixel.
/// </summary>
/// <typeparam name="TPixel">The destination pixel format.</typeparam>
internal interface IGradientPixelEncoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Converts a scaled vector to the destination pixel.
    /// </summary>
    /// <param name="color">The scaled vector, in the alpha representation of the encoder.</param>
    /// <returns>The destination pixel.</returns>
    public static abstract TPixel Encode(Vector4 color);
}

/// <summary>
/// Encodes a gradient color that has associated alpha.
/// </summary>
/// <typeparam name="TPixel">The destination pixel format.</typeparam>
internal readonly struct AssociatedGradientPixelEncoder<TPixel> : IGradientPixelEncoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <inheritdoc />
    public static TPixel Encode(Vector4 color) => TPixel.FromAssociatedScaledVector4(color);
}

/// <summary>
/// Encodes a gradient color that has unassociated alpha.
/// </summary>
/// <typeparam name="TPixel">The destination pixel format.</typeparam>
internal readonly struct UnassociatedGradientPixelEncoder<TPixel> : IGradientPixelEncoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <inheritdoc />
    public static TPixel Encode(Vector4 color) => TPixel.FromUnassociatedScaledVector4(color);
}
