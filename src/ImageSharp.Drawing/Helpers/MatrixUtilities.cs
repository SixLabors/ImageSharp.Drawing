// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Provides helper methods for extracting properties from transformation matrices.
/// </summary>
internal static class MatrixUtilities
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
}
