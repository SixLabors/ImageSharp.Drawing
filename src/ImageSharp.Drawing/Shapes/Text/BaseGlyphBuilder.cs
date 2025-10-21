// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Shapes.Text;

namespace SixLabors.ImageSharp.Drawing.Text;

/// <summary>
/// Defines a base rendering surface that Fonts can use to generate shapes.
/// </summary>
internal class BaseGlyphBuilder : IGlyphRenderer
{
    private Vector2 currentPoint;
    private GlyphRendererParameters parameters;

    // Tracks whether geometry was emitted inside BeginLayer/EndLayer pairs for this glyph.
    private bool usedLayers;

    // Tracks whether we are currently inside a layer block.
    private bool inLayer;

    // Per-GRAPHEME layered capture (aggregate multiple glyphs of the same grapheme, e.g. COLR v0 layers):
    private GlyphPathCollection.Builder? graphemeBuilder;
    private int graphemePathCount;
    private int currentGraphemeIndex = -1;
    private readonly List<GlyphPathCollection> currentGlyphs = [];
    private TextDecorationDetails? previousUnderlineTextDecoration;
    private TextDecorationDetails? previousOverlineTextDecoration;
    private TextDecorationDetails? previousStrikeoutTextDecoration;

    // Per-layer (within current grapheme) bookkeeping:
    private int layerStartIndex;
    private Paint? activeLayerPaint;
    private FillRule activeLayerFillRule;
    private FontRectangle? activeClipBounds;

    public BaseGlyphBuilder() => this.Builder = new PathBuilder();

    public BaseGlyphBuilder(Matrix3x2 transform) => this.Builder = new PathBuilder(transform);

    /// <summary>
    /// Gets the flattened paths captured for all glyphs/graphemes.
    /// </summary>
    public IPathCollection Paths => new PathCollection(this.CurrentPaths);

    /// <summary>
    /// Gets the layer-preserving collections captured per grapheme in rendering order.
    /// Each entry aggregates all glyph layers that belong to a single grapheme cluster.
    /// </summary>
    public IReadOnlyList<GlyphPathCollection> Glyphs => this.currentGlyphs;

    protected PathBuilder Builder { get; }

    /// <summary>
    /// Gets the paths captured for the current glyph/grapheme.
    /// </summary>
    protected List<IPath> CurrentPaths { get; } = [];

    void IGlyphRenderer.EndText()
    {
        // Finalize the last grapheme, if any:
        if (this.graphemeBuilder is not null && this.graphemePathCount > 0)
        {
            this.currentGlyphs.Add(this.graphemeBuilder.Build());
        }

        this.graphemeBuilder = null;
        this.graphemePathCount = 0;
        this.currentGraphemeIndex = -1;
        this.previousUnderlineTextDecoration = null;
        this.previousOverlineTextDecoration = null;
        this.previousStrikeoutTextDecoration = null;

        this.EndText();
    }

    void IGlyphRenderer.BeginText(in FontRectangle bounds) => this.BeginText(bounds);

    bool IGlyphRenderer.BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        // If grapheme changed, flush previous aggregate and start a new one:
        if (this.graphemeBuilder is not null && this.currentGraphemeIndex != parameters.GraphemeIndex)
        {
            if (this.graphemePathCount > 0)
            {
                this.currentGlyphs.Add(this.graphemeBuilder.Build());
            }

            this.graphemeBuilder = null;
            this.graphemePathCount = 0;
        }

        if (this.graphemeBuilder is null)
        {
            this.graphemeBuilder = new GlyphPathCollection.Builder();
            this.currentGraphemeIndex = parameters.GraphemeIndex;
            this.graphemePathCount = 0;
        }

        this.parameters = parameters;
        this.Builder.Clear();
        this.usedLayers = false;
        this.inLayer = false;

        this.layerStartIndex = this.graphemePathCount;
        this.activeLayerPaint = null;
        this.activeLayerFillRule = FillRule.NonZero;
        this.activeClipBounds = null;
        this.BeginGlyph(in bounds, in parameters);
        return true;
    }

    /// <inheritdoc/>
    void IGlyphRenderer.BeginFigure() => this.Builder.StartFigure();

    /// <inheritdoc/>
    void IGlyphRenderer.CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
    {
        this.Builder.AddCubicBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);
        this.currentPoint = point;
    }

    /// <inheritdoc/>
    void IGlyphRenderer.EndGlyph()
    {
        // If the glyph did not open any explicit layer, treat its geometry as a single layer in the current grapheme:
        if (!this.usedLayers)
        {
            IPath path = this.Builder.Build();

            this.CurrentPaths.Add(path);

            if (this.graphemeBuilder is not null)
            {
                this.graphemeBuilder.AddPath(path);
                this.graphemeBuilder.AddLayer(
                    startIndex: this.graphemePathCount,
                    count: 1,
                    paint: null,
                    fillRule: FillRule.NonZero,
                    bounds: path.Bounds,
                    kind: GlyphLayerKind.Glyph);

                this.graphemePathCount++;
            }
        }

        this.EndGlyph();
        this.Builder.Clear();
        this.inLayer = false;
        this.usedLayers = false;
        this.layerStartIndex = this.graphemePathCount;
    }

    /// <inheritdoc/>
    void IGlyphRenderer.EndFigure() => this.Builder.CloseFigure();

    /// <inheritdoc/>
    void IGlyphRenderer.LineTo(Vector2 point)
    {
        this.Builder.AddLine(this.currentPoint, point);
        this.currentPoint = point;
    }

    /// <inheritdoc/>
    void IGlyphRenderer.MoveTo(Vector2 point)
    {
        this.Builder.StartFigure();
        this.currentPoint = point;
    }

    /// <inheritdoc/>
    void IGlyphRenderer.ArcTo(float radiusX, float radiusY, float rotation, bool largeArc, bool sweep, Vector2 point)
    {
        this.Builder.AddArc(this.currentPoint, radiusX, radiusY, rotation, largeArc, sweep, point);
        this.currentPoint = point;
    }

    /// <inheritdoc/>
    void IGlyphRenderer.QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
    {
        this.Builder.AddQuadraticBezier(this.currentPoint, secondControlPoint, point);
        this.currentPoint = point;
    }

    /// <inheritdoc/>
    void IGlyphRenderer.BeginLayer(Paint? paint, FillRule fillRule, in FontRectangle? clipBounds)
    {
        this.usedLayers = true;
        this.inLayer = true;
        this.layerStartIndex = this.graphemePathCount;
        this.activeLayerPaint = paint;
        this.activeLayerFillRule = fillRule;
        this.activeClipBounds = clipBounds;

        this.Builder.Clear();
        this.BeginLayer(paint, fillRule, clipBounds);
    }

    /// <inheritdoc/>
    void IGlyphRenderer.EndLayer()
    {
        if (!this.inLayer)
        {
            return;
        }

        IPath path = this.Builder.Build();

        // TODO: We need to clip the path by activeClipBounds if set.
        this.CurrentPaths.Add(path);

        if (this.graphemeBuilder is not null)
        {
            this.graphemeBuilder.AddPath(path);
            this.graphemeBuilder.AddLayer(
                startIndex: this.layerStartIndex,
                count: 1,
                paint: this.activeLayerPaint,
                fillRule: this.activeLayerFillRule,
                bounds: path.Bounds,
                kind: GlyphLayerKind.Painted);

            this.graphemePathCount++;
        }

        this.Builder.Clear();
        this.inLayer = false;
        this.activeLayerPaint = null;
        this.activeLayerFillRule = FillRule.NonZero;
        this.activeClipBounds = null;
        this.EndLayer();
    }

    /// <inheritdoc/>
    void IGlyphRenderer.SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
        if (thickness == 0)
        {
            return;
        }

        // Clamp the thickness to whole pixels.
        thickness = MathF.Max(1F, (float)Math.Round(thickness));
        IGlyphRenderer renderer = this;

        bool rotated = this.parameters.LayoutMode is GlyphLayoutMode.Vertical or GlyphLayoutMode.VerticalRotated;
        Vector2 pad = rotated ? new Vector2(thickness * .5F, 0) : new Vector2(0, thickness * .5F);

        start = ClampToPixel(start, (int)thickness, rotated);
        end = ClampToPixel(end, (int)thickness, rotated);

        // Sometimes the start and end points do not align properly leaving pixel sized gaps
        // so we need to adjust them. Use any previous decoration to try and continue the line.
        TextDecorationDetails? previous = textDecorations switch
        {
            TextDecorations.Underline => this.previousUnderlineTextDecoration,
            TextDecorations.Overline => this.previousOverlineTextDecoration,
            TextDecorations.Strikeout => this.previousStrikeoutTextDecoration,
            _ => null
        };

        if (previous != null)
        {
            float prevThickness = previous.Value.Thickness;
            Vector2 prevStart = previous.Value.Start;
            Vector2 prevEnd = previous.Value.End;

            // If the previous line is identical to the new one ignore it.
            // This can happen when multiple glyph layers are used.
            if (prevStart == start && prevEnd == end)
            {
                return;
            }

            // Align the new line with the previous one if they are close enough.
            if (rotated)
            {
                if (thickness == prevThickness
                && prevEnd.Y + 1 >= start.Y
                && prevEnd.X == start.X)
                {
                    start = prevEnd;
                }
            }
            else if (thickness == prevThickness
                 && prevEnd.Y == start.Y
                 && prevEnd.X + 1 >= start.X)
            {
                start = prevEnd;
            }
        }

        TextDecorationDetails current = new()
        {
            Start = start,
            End = end,
            Thickness = thickness
        };

        switch (textDecorations)
        {
            case TextDecorations.Underline:
                this.previousUnderlineTextDecoration = current;
                break;
            case TextDecorations.Strikeout:
                this.previousStrikeoutTextDecoration = current;
                break;
            case TextDecorations.Overline:
                this.previousOverlineTextDecoration = current;
                break;
        }

        Vector2 a = start - pad;
        Vector2 b = start + pad;
        Vector2 c = end + pad;
        Vector2 d = end - pad;

        // Drawing is always centered around the point so we need to offset by half.
        Vector2 offset = Vector2.Zero;
        if (textDecorations == TextDecorations.Overline)
        {
            // CSS overline is drawn above the position, so we need to move it up.
            offset = rotated ? new Vector2(thickness * .5F, 0) : new Vector2(0, -(thickness * .5F));
        }
        else if (textDecorations == TextDecorations.Underline)
        {
            // CSS underline is drawn below the position, so we need to move it down.
            offset = rotated ? new Vector2(-(thickness * .5F), 0) : new Vector2(0, thickness * .5F);
        }

        // We clamp the start and end points to the pixel grid to avoid anti-aliasing
        // when there is no transform.
        renderer.BeginFigure();
        renderer.MoveTo(ClampToPixel(a + offset));
        renderer.LineTo(ClampToPixel(b + offset));
        renderer.LineTo(ClampToPixel(c + offset));
        renderer.LineTo(ClampToPixel(d + offset));
        renderer.EndFigure();

        IPath path = this.Builder.Build();

        // If the path is degenerate (e.g. zero width line) we just skip it
        // and return. This might happen when clamping moves the points.
        if (path.Bounds.IsEmpty)
        {
            this.Builder.Clear();
            return;
        }

        this.CurrentPaths.Add(path);
        if (this.graphemeBuilder is not null)
        {
            this.graphemeBuilder.AddPath(path);
            this.graphemeBuilder.AddLayer(
                startIndex: this.layerStartIndex,
                count: 1,
                paint: this.activeLayerPaint,
                fillRule: FillRule.NonZero,
                bounds: path.Bounds,
                kind: GlyphLayerKind.Decoration);

            this.graphemePathCount++;
        }

        this.Builder.Clear();
        this.SetDecoration(textDecorations, start, end, thickness);
    }

    /// <inheritdoc cref="IGlyphRenderer.BeginText(in FontRectangle)"/>
    protected virtual void BeginText(in FontRectangle bounds)
    {
    }

    /// <inheritdoc cref="IGlyphRenderer.BeginGlyph(in FontRectangle, in GlyphRendererParameters)"/>
    protected virtual void BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
    }

    /// <inheritdoc cref="IGlyphRenderer.EndGlyph"/>
    protected virtual void EndGlyph()
    {
    }

    /// <inheritdoc cref="IGlyphRenderer.EndText"/>
    protected virtual void EndText()
    {
    }

    /// <inheritdoc cref="IGlyphRenderer.BeginLayer(Paint?, FillRule, in FontRectangle?)"/>
    protected virtual void BeginLayer(Paint? paint, FillRule fillRule, in FontRectangle? clipBounds)
    {
    }

    /// <inheritdoc cref="IGlyphRenderer.EndLayer"/>
    protected virtual void EndLayer()
    {
    }

    public virtual TextDecorations EnabledDecorations()
        => this.parameters.TextRun.TextDecorations;

    /// <inheritdoc cref="IGlyphRenderer.SetDecoration(TextDecorations, Vector2, Vector2, float)"/>
    public virtual void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
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

    private struct TextDecorationDetails
    {
        public Vector2 Start { get; set; }

        public Vector2 End { get; set; }

        public float Thickness { get; internal set; }
    }
}
