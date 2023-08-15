// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Text;

/// <summary>
/// Defines a rendering surface that Fonts can use to generate Shapes.
/// </summary>
internal class BaseGlyphBuilder : IGlyphRenderer
{
    private readonly List<IPath> paths = new();
    private Vector2 currentPoint;
    private GlyphRendererParameters parameters;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseGlyphBuilder"/> class.
    /// </summary>
    public BaseGlyphBuilder() => this.Builder = new PathBuilder();

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseGlyphBuilder"/> class.
    /// </summary>
    /// <param name="transform">The default transform.</param>
    public BaseGlyphBuilder(Matrix3x2 transform) => this.Builder = new PathBuilder(transform);

    /// <summary>
    /// Gets the paths that have been rendered by the current instance.
    /// </summary>
    public IPathCollection Paths => new PathCollection(this.paths);

    /// <summary>
    /// Gets the path builder for the current instance.
    /// </summary>
    protected PathBuilder Builder { get; }

    /// <inheritdoc/>
    void IGlyphRenderer.EndText() => this.EndText();

    /// <inheritdoc/>
    void IGlyphRenderer.BeginText(in FontRectangle bounds) => this.BeginText(bounds);

    /// <inheritdoc/>
    bool IGlyphRenderer.BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        this.parameters = parameters;
        this.Builder.Clear();
        this.BeginGlyph(in bounds, in parameters);
        return true;
    }

    /// <summary>
    /// Begins the figure.
    /// </summary>
    void IGlyphRenderer.BeginFigure() => this.Builder.StartFigure();

    /// <summary>
    /// Draws a cubic bezier from the current point  to the <paramref name="point"/>
    /// </summary>
    /// <param name="secondControlPoint">The second control point.</param>
    /// <param name="thirdControlPoint">The third control point.</param>
    /// <param name="point">The point.</param>
    void IGlyphRenderer.CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
    {
        this.Builder.AddCubicBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);
        this.currentPoint = point;
    }

    /// <summary>
    /// Ends the glyph.
    /// </summary>
    void IGlyphRenderer.EndGlyph()
    {
        this.paths.Add(this.Builder.Build());
        this.EndGlyph();
    }

    /// <summary>
    /// Ends the figure.
    /// </summary>
    void IGlyphRenderer.EndFigure() => this.Builder.CloseFigure();

    /// <summary>
    /// Draws a line from the current point  to the <paramref name="point"/>.
    /// </summary>
    /// <param name="point">The point.</param>
    void IGlyphRenderer.LineTo(Vector2 point)
    {
        this.Builder.AddLine(this.currentPoint, point);
        this.currentPoint = point;
    }

    /// <summary>
    /// Moves to current point to the supplied vector.
    /// </summary>
    /// <param name="point">The point.</param>
    void IGlyphRenderer.MoveTo(Vector2 point)
    {
        this.Builder.StartFigure();
        this.currentPoint = point;
    }

    /// <summary>
    /// Draws a quadratics bezier from the current point  to the <paramref name="point"/>
    /// </summary>
    /// <param name="secondControlPoint">The second control point.</param>
    /// <param name="point">The point.</param>
    void IGlyphRenderer.QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
    {
        this.Builder.AddQuadraticBezier(this.currentPoint, secondControlPoint, point);
        this.currentPoint = point;
    }

    /// <summary>Called before any glyphs have been rendered.</summary>
    /// <param name="bounds">The bounds the text will be rendered at and at what size.</param>
    protected virtual void BeginText(in FontRectangle bounds)
    {
    }

    /// <inheritdoc cref="IGlyphRenderer.BeginGlyph(in FontRectangle, in GlyphRendererParameters)"/>
    protected virtual void BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
    }

    /// <inheritdoc cref="IGlyphRenderer.EndGlyph()"/>
    protected virtual void EndGlyph()
    {
    }

    /// <inheritdoc cref="IGlyphRenderer.EndText()"/>
    protected virtual void EndText()
    {
    }

    public virtual TextDecorations EnabledDecorations()
        => this.parameters.TextRun.TextDecorations;

    public virtual void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
        if (thickness == 0)
        {
            return;
        }

        thickness = MathF.Max(1F, (float)Math.Round(thickness));
        var renderer = (IGlyphRenderer)this;

        // Expand the points to create a rectangle centered around the line.
        bool rotated = this.parameters.LayoutMode is GlyphLayoutMode.Vertical or GlyphLayoutMode.VerticalRotated;
        Vector2 pad = rotated ? new(thickness * .5F, 0) : new(0, thickness * .5F);

        // Clamp the line to the pixel grid.
        start = ClampToPixel(start, (int)thickness, rotated);
        end = ClampToPixel(end, (int)thickness, rotated);

        // Offset to create the rectangle.
        Vector2 a = start - pad;
        Vector2 b = start + pad;
        Vector2 c = end + pad;
        Vector2 d = end - pad;

        // Drawing is always centered around the point so we need to offset by half.
        Vector2 offset = Vector2.Zero;
        if (textDecorations == TextDecorations.Overline)
        {
            // CSS overline is drawn above the position, so we need to move it up.
            offset = rotated ? new(thickness * .5F, 0) : new(0, -(thickness * .5F));
        }
        else if (textDecorations == TextDecorations.Underline)
        {
            // CSS underline is drawn below the position, so we need to move it down.
            offset = rotated ? new(-(thickness * .5F), 0) : new(0, thickness * .5F);
        }

        renderer.BeginFigure();

        // Now draw the rectangle clamped to the pixel grid.
        renderer.MoveTo(ClampToPixel(a + offset));
        renderer.LineTo(ClampToPixel(b + offset));
        renderer.LineTo(ClampToPixel(c + offset));
        renderer.LineTo(ClampToPixel(d + offset));
        renderer.EndFigure();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point ClampToPixel(PointF point) => Point.Truncate(point);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointF ClampToPixel(PointF point, int thickness, bool rotated)
    {
        // Even. Clamp to whole pixels.
        if ((thickness & 1) == 0)
        {
            return Point.Truncate(point);
        }

        // Odd. Clamp to half pixels.
        if (rotated)
        {
            return Point.Truncate(point) + new Vector2(.5F, 0);
        }

        return Point.Truncate(point) + new Vector2(0, .5F);
    }
}
