// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Provides helper methods for extracting properties from transformation matrices.
/// </summary>
public static class MatrixUtilities
{
    /// <summary>
    /// Extracts the average 2D scale factor from a <see cref="Matrix4x4"/>.
    /// This is the mean of the X and Y axis scale magnitudes, suitable for
    /// uniformly scaling radii under non-uniform or projective transforms.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>The average scale factor.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float GetAverageScale(in Matrix4x4 matrix)
    {
        float sx = MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12));
        float sy = MathF.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22));
        return (sx + sy) * 0.5f;
    }

    /// <summary>
    /// Returns a value indicating whether the matrix maps axis-aligned rectangles to axis-aligned rectangles.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns><see langword="true"/> when axis-aligned rectangles remain axis-aligned; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool PreservesAxisAlignedRectangles(in Matrix4x4 matrix) =>

        // Either each output axis depends on the matching input axis, or the axes are swapped.
        // Once both terms in an output axis are non-zero, rectangle edges become rotated or skewed.
        (matrix.M12 == 0 && matrix.M21 == 0) || (matrix.M11 == 0 && matrix.M22 == 0);

    /// <summary>
    /// Extracts the X and Y scale magnitudes from a 2D transform matrix.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns>The X and Y scale magnitudes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector2 GetScale(in Matrix4x4 matrix)
        => new(
            MathF.Sqrt((matrix.M11 * matrix.M11) + (matrix.M12 * matrix.M12)),
            MathF.Sqrt((matrix.M21 * matrix.M21) + (matrix.M22 * matrix.M22)));

    /// <summary>
    /// Computes the transform remaining after the X and Y scale magnitudes have been baked into geometry.
    /// </summary>
    /// <param name="scale">The scale magnitudes baked into geometry.</param>
    /// <param name="matrix">The original transformation matrix.</param>
    /// <returns>The residual transform.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 GetResidual(Vector2 scale, Matrix4x4 matrix)
        => Matrix4x4.CreateScale(1F / scale.X, 1F / scale.Y, 1F) * matrix;
}
