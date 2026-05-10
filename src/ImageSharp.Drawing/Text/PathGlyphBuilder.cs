// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;

namespace SixLabors.ImageSharp.Drawing.Text;

/// <summary>
/// A rendering surface that Fonts can use to generate shapes by following a path.
/// Each glyph is positioned along the path and rotated to match the path tangent
/// at the glyph's horizontal center.
/// </summary>
internal sealed class PathGlyphBuilder : GlyphBuilder
{
    /// <summary>
    /// The path that glyphs are laid out along. Exposed as <see cref="IPathInternals"/>
    /// to access the <see cref="IPathInternals.PointAlongPath"/> method for efficient
    /// position + tangent queries.
    /// </summary>
    private readonly IPathInternals path;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathGlyphBuilder"/> class.
    /// </summary>
    /// <param name="path">The path to render the glyphs along.</param>
    public PathGlyphBuilder(IPath path)
    {
        if (path is IPathInternals internals)
        {
            this.path = internals;
        }
        else
        {
            // Wrap in ComplexPolygon to gain IPathInternals.
            this.path = new ComplexPolygon(path);
        }
    }

    /// <inheritdoc/>
    protected override bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        // Translate + rotate the glyph to follow the path. Always returns true because
        // path-based glyphs are never cached (each has a unique per-position transform).
        this.TransformGlyph(in bounds);
        return true;
    }

    /// <summary>
    /// Computes the translation + rotation matrix that places a glyph along the path.
    /// The glyph's horizontal center is mapped to the path distance, and the glyph
    /// is rotated to match the path tangent at that point.
    /// </summary>
    /// <param name="bounds">The font-metric bounding rectangle of the glyph.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TransformGlyph(in FontRectangle bounds)
    {
        // Query the path at the glyph's horizontal center.
        Vector2 half = new(bounds.Width * .5F, 0);
        SegmentInfo pathPoint = this.path.PointAlongPath(bounds.Left + half.X);

        // Translate so the glyph's top-left aligns with the path point,
        // then rotate around the path point to follow the tangent.
        Vector2 translation = (Vector2)pathPoint.Point - bounds.Location - half + new Vector2(0, bounds.Top);
        Matrix4x4 matrix = Matrix4x4.CreateTranslation(translation.X, translation.Y, 0) * new Matrix4x4(Matrix3x2.CreateRotation(pathPoint.Angle - MathF.PI, (Vector2)pathPoint.Point));

        this.Builder.SetTransform(matrix);
    }
}
