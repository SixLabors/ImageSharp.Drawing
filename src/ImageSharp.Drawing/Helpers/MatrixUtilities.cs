// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Provides helper methods for extracting scale properties from transformation matrices.
/// </summary>
/// <remarks>
/// Scale extraction operates on the 2D linear part of the matrix (M11, M12, M21, M22).
/// Scale magnitudes are the lengths of the transformed X and Y basis vectors, so they are
/// rotation-invariant and always non-negative. Translation does not affect any result.
/// </remarks>
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
    /// Returns a value indicating whether the matrix is affine in 2D: no perspective terms,
    /// so every position is transformed by the same linear part and shapes are mapped
    /// identically wherever they sit. Projective matrices fail this test because their
    /// perspective divisor varies with the input position.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns><see langword="true"/> when the matrix contains no perspective terms; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAffine(in Matrix4x4 matrix)
        => matrix.M14 == 0F && matrix.M24 == 0F && matrix.M34 == 0F && matrix.M44 == 1F;

    /// <summary>
    /// Returns a value indicating whether the matrix is a pure 2D translation: an identity
    /// linear part with no perspective terms, so geometry moves by the M41 and M42 offsets
    /// without any scale, rotation, or skew.
    /// </summary>
    /// <param name="matrix">The transformation matrix.</param>
    /// <returns><see langword="true"/> when the matrix only translates; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTranslationOnly(in Matrix4x4 matrix)
        => matrix.M11 == 1F && matrix.M12 == 0F && matrix.M13 == 0F && matrix.M14 == 0F &&
           matrix.M21 == 0F && matrix.M22 == 1F && matrix.M23 == 0F && matrix.M24 == 0F &&
           matrix.M31 == 0F && matrix.M32 == 0F && matrix.M33 == 1F && matrix.M34 == 0F &&
           matrix.M44 == 1F;

    /// <summary>
    /// Returns a value indicating whether the matrix maps axis-aligned rectangles to axis-aligned rectangles.
    /// </summary>
    /// <remarks>
    /// Translation, scaling, reflection, and 90 degree rotations (axis swaps) preserve axis
    /// alignment. Skew and free rotation do not.
    /// </remarks>
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
    /// <remarks>
    /// The magnitudes are the lengths of the transformed X and Y basis vectors. They are
    /// always non-negative; reflection and rotation are not represented in the result.
    /// </remarks>
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
    /// <remarks>
    /// The invariant is <c>Matrix4x4.CreateScale(scale.X, scale.Y, 1) * residual == matrix</c>:
    /// applying the residual to geometry that has already been scaled reproduces the original
    /// transform. Rotation, reflection, skew, and translation all remain in the residual.
    /// </remarks>
    /// <param name="scale">The scale magnitudes baked into geometry. Components must be non-zero.</param>
    /// <param name="matrix">The original transformation matrix.</param>
    /// <returns>The residual transform.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Matrix4x4 GetResidual(Vector2 scale, Matrix4x4 matrix)
        => Matrix4x4.CreateScale(1F / scale.X, 1F / scale.Y, 1F) * matrix;
}
