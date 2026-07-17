// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Encodes an associated gradient result in the destination pixel representation.
/// </summary>
/// <typeparam name="TPixel">The destination pixel format.</typeparam>
internal interface IGradientPixelEncoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <summary>
    /// Converts an associated scaled vector to the destination pixel representation.
    /// </summary>
    /// <param name="color">The associated scaled vector.</param>
    /// <returns>The destination pixel.</returns>
    public static abstract TPixel Encode(Vector4 color);
}

/// <summary>
/// Encodes associated gradient results for an associated-alpha destination.
/// </summary>
/// <typeparam name="TPixel">The destination pixel format.</typeparam>
internal readonly struct AssociatedGradientPixelEncoder<TPixel> : IGradientPixelEncoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <inheritdoc />
    public static TPixel Encode(Vector4 color) => TPixel.FromScaledVector4(color);
}

/// <summary>
/// Encodes associated gradient results for an unassociated-alpha destination.
/// </summary>
/// <typeparam name="TPixel">The destination pixel format.</typeparam>
internal readonly struct UnassociatedGradientPixelEncoder<TPixel> : IGradientPixelEncoder<TPixel>
    where TPixel : unmanaged, IPixel<TPixel>
{
    /// <inheritdoc />
    public static TPixel Encode(Vector4 color)
    {
        // CSS Color 4 undoes premultiplication only after interpolation. Straight targets require
        // that conversion here, while associated targets store the interpolated vector directly.
        // https://www.w3.org/TR/css-color-4/#interpolation-alpha
        float alpha = color.W;
        if (alpha > 0)
        {
            color /= alpha;
            color.W = alpha;
        }
        else
        {
            color = Vector4.Zero;
        }

        return TPixel.FromScaledVector4(color);
    }
}
