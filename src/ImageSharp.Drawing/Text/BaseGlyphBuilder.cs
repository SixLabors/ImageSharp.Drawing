// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Text;

/// <summary>
/// Defines a base rendering surface that Fonts can use to generate shapes.
/// </summary>
internal class BaseGlyphBuilder : IGlyphRenderer
{
    /// <summary>
    /// The last point emitted by <c>MoveTo</c> / <c>LineTo</c> / curve commands.
    /// Used as the implicit start of the next segment.
    /// </summary>
    private Vector2 currentPoint;

    /// <summary>
    /// Snapshot of the <see cref="GlyphRendererParameters"/> for the glyph currently
    /// being processed. Set at the start of each <c>BeginGlyph</c> call and read by
    /// <c>SetDecoration</c> to determine layout orientation.
    /// </summary>
    private GlyphRendererParameters parameters;

    // Tracks whether geometry was emitted inside BeginLayer/EndLayer pairs for this glyph.
    // When true, EndGlyph skips its default single-layer path capture because layers
    // already contributed their paths individually.
    private bool usedLayers;

    // Tracks whether we are currently inside a layer block.
    // Guards against unbalanced EndLayer calls.
    private bool inLayer;

    // --- Per-GRAPHEME layered capture ---
    // A grapheme cluster (e.g. a base glyph + COLR v0 color layers) may span
    // multiple BeginGlyph/EndGlyph calls. These fields aggregate all layers
    // belonging to the same grapheme into a single GlyphPathCollection.
    private GlyphPathCollection.Builder? graphemeBuilder;
    private int graphemePathCount;
    private int currentGraphemeIndex = -1;
    private readonly List<GlyphPathCollection> currentGlyphs = [];

    // Previous decoration details per decoration type, used to stitch adjacent
    // decorations together and eliminate sub-pixel gaps between glyphs.
    private TextDecorationDetails? previousUnderlineTextDecoration;
    private TextDecorationDetails? previousOverlineTextDecoration;
    private TextDecorationDetails? previousStrikeoutTextDecoration;

    // Per-layer (within current grapheme) bookkeeping:
    private int layerStartIndex;
    private Paint? currentLayerPaint;
    private FillRule currentLayerFillRule;
    private ClipQuad? currentClipBounds;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseGlyphBuilder"/> class
    /// with an identity transform.
    /// </summary>
    public BaseGlyphBuilder() => this.Builder = new PathBuilder();

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseGlyphBuilder"/> class
    /// with the specified transform applied to all incoming glyph geometry.
    /// </summary>
    /// <param name="transform">A matrix transform applied to every point received from the font engine.</param>
    public BaseGlyphBuilder(Matrix4x4 transform) => this.Builder = new PathBuilder(transform);

    /// <summary>
    /// Gets the flattened paths captured for all glyphs/graphemes.
    /// </summary>
    public IPathCollection Paths => new PathCollection(this.CurrentPaths);

    /// <summary>
    /// Gets the layer-preserving collections captured per grapheme in rendering order.
    /// Each entry aggregates all glyph layers that belong to a single grapheme cluster.
    /// </summary>
    public IReadOnlyList<GlyphPathCollection> Glyphs => this.currentGlyphs;

    /// <summary>
    /// Gets the <see cref="PathBuilder"/> used to accumulate outline segments
    /// (<c>MoveTo</c>, <c>LineTo</c>, curves) for the current glyph or layer.
    /// The builder is cleared between glyphs / layers.
    /// </summary>
    protected PathBuilder Builder { get; }

    /// <summary>
    /// Gets the running list of all <see cref="IPath"/> instances produced so far
    /// (glyph outlines, layer outlines, and decoration rectangles). Subclasses
    /// read from the end of this list (e.g. <c>CurrentPaths[^1]</c>) to obtain
    /// the most recently built path.
    /// </summary>
    protected List<IPath> CurrentPaths { get; } = [];

    /// <summary>
    /// Called by the font engine after all glyphs in the text block have been rendered.
    /// Flushes any in-progress grapheme aggregate and resets per-text-block state.
    /// </summary>
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

    /// <summary>
    /// Called by the font engine before emitting outline data for a single glyph.
    /// Manages grapheme-cluster transitions and resets per-glyph state.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> to have the font engine emit the full outline
    /// (MoveTo/LineTo/curves/EndGlyph); <see langword="false"/> to skip it entirely,
    /// which is used by caching subclasses when the glyph path is already available.
    /// </returns>
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
        this.currentLayerPaint = null;
        this.currentLayerFillRule = FillRule.NonZero;
        this.currentClipBounds = null;
        return this.BeginGlyph(in bounds, in parameters);
    }

    /// <inheritdoc/>
    void IGlyphRenderer.BeginFigure() => this.Builder.StartFigure();

    /// <inheritdoc/>
    void IGlyphRenderer.CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
    {
        this.Builder.AddCubicBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);
        this.currentPoint = point;
    }

    /// <summary>
    /// Called by the font engine after the outline for a single glyph has been fully emitted.
    /// Builds the accumulated path and registers it as a grapheme layer unless explicit
    /// <c>BeginLayer</c>/<c>EndLayer</c> pairs already handled layer registration.
    /// </summary>
    void IGlyphRenderer.EndGlyph()
    {
        // If the glyph did not open any explicit layer, treat its geometry as a single
        // implicit layer so that non-color glyphs still produce a GlyphPathCollection entry.
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

    /// <summary>
    /// Called by the font engine to begin a color layer within a COLR v0/v1 glyph.
    /// Each layer receives its own paint, fill rule, and optional clip bounds.
    /// </summary>
    void IGlyphRenderer.BeginLayer(Paint? paint, FillRule fillRule, ClipQuad? clipBounds)
    {
        this.usedLayers = true;
        this.inLayer = true;
        this.layerStartIndex = this.graphemePathCount;
        this.currentLayerPaint = paint;
        this.currentLayerFillRule = fillRule;
        this.currentClipBounds = clipBounds;

        this.Builder.Clear();
        this.BeginLayer(paint, fillRule, clipBounds);
    }

    /// <summary>
    /// Called by the font engine to close a color layer opened by <c>BeginLayer</c>.
    /// Builds the layer path, applies any clip quad, and registers the result
    /// as a painted layer in the current grapheme aggregate.
    /// </summary>
    void IGlyphRenderer.EndLayer()
    {
        if (!this.inLayer)
        {
            return;
        }

        IPath path = this.Builder.Build();

        // If the layer defines a clip quad (e.g. from COLR v1), intersect the
        // built path with the quad polygon to constrain rendering.
        if (this.currentClipBounds is not null)
        {
            ClipQuad clip = this.currentClipBounds.Value;
            PointF[] points = [clip.TopLeft, clip.TopRight, clip.BottomRight, clip.BottomLeft];
            LinearLineSegment segment = new(points);
            Polygon polygon = new(segment);

            ShapeOptions options = new()
            {
                BooleanOperation = BooleanOperation.Intersection,
                IntersectionRule = TextUtilities.MapFillRule(this.currentLayerFillRule)
            };

            path = path.Clip(options, polygon);
        }

        this.CurrentPaths.Add(path);

        if (this.graphemeBuilder is not null)
        {
            this.graphemeBuilder.AddPath(path);
            this.graphemeBuilder.AddLayer(
                startIndex: this.layerStartIndex,
                count: 1,
                paint: this.currentLayerPaint,
                fillRule: this.currentLayerFillRule,
                bounds: path.Bounds,
                kind: GlyphLayerKind.Painted);

            this.graphemePathCount++;
        }

        this.Builder.Clear();
        this.inLayer = false;
        this.currentLayerPaint = null;
        this.currentLayerFillRule = FillRule.NonZero;
        this.currentClipBounds = null;
        this.EndLayer();
    }

    /// <summary>
    /// Called by the font engine to emit a text decoration (underline, strikeout, or overline)
    /// for the current glyph. Builds a filled rectangle path from the start/end positions and
    /// thickness, then registers it as a <see cref="GlyphLayerKind.Decoration"/> layer.
    /// Adjacent decorations are stitched together using the previous decoration details to
    /// eliminate sub-pixel gaps caused by font metric rounding.
    /// </summary>
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
            // Use a 2 pixel threshold to account for anti-aliasing gaps.
            if (rotated)
            {
                if (thickness == prevThickness
                && prevEnd.Y + 2 >= start.Y
                && prevEnd.X == start.X)
                {
                    start = prevEnd;
                }
            }
            else if (thickness == prevThickness
                 && prevEnd.Y == start.Y
                 && prevEnd.X + 2 >= start.X)
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
            // Decorations are emitted as independent paths; each layer must point
            // at the path index appended for this specific decoration.
            this.graphemeBuilder.AddPath(path);
            this.graphemeBuilder.AddLayer(
                startIndex: this.graphemePathCount,
                count: 1,
                paint: this.currentLayerPaint,
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

    /// <summary>
    /// Called after base-class bookkeeping in <c>IGlyphRenderer.BeginGlyph</c>.
    /// Subclasses override this to apply transforms, consult caches, or opt out of
    /// outline emission by returning <see langword="false"/>.
    /// </summary>
    /// <param name="bounds">The font-metric bounding rectangle of the glyph.</param>
    /// <param name="parameters">Identifies the glyph (id, font, layout mode, text run, etc.).</param>
    /// <returns>
    /// <see langword="true"/> to receive outline data and an <c>EndGlyph</c> call;
    /// <see langword="false"/> to skip outline emission for this glyph entirely.
    /// </returns>
    protected virtual bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
        => true;

    /// <summary>
    /// Called after the base class has built and registered the glyph path.
    /// Subclasses override this to emit drawing operations from the captured path.
    /// </summary>
    protected virtual void EndGlyph()
    {
    }

    /// <summary>
    /// Called after the base class has flushed all grapheme aggregates.
    /// Subclasses override this for any per-text-block finalization.
    /// </summary>
    protected virtual void EndText()
    {
    }

    /// <summary>
    /// Called when a COLR color layer begins. Subclasses override this to
    /// capture the layer's paint and composite mode.
    /// </summary>
    /// <param name="paint">The paint for this color layer, or <see langword="null"/> for the default foreground.</param>
    /// <param name="fillRule">The fill rule to use when rasterizing this layer.</param>
    /// <param name="clipBounds">Optional clip quad constraining the layer region.</param>
    protected virtual void BeginLayer(Paint? paint, FillRule fillRule, ClipQuad? clipBounds)
    {
    }

    /// <summary>
    /// Called when a COLR color layer ends. Subclasses override this to
    /// emit the layer as a drawing operation.
    /// </summary>
    protected virtual void EndLayer()
    {
    }

    /// <summary>
    /// Returns the set of text decorations enabled for the current glyph.
    /// The font engine calls this to decide which <c>SetDecoration</c> callbacks to emit.
    /// Subclasses override this to include decorations implied by rich-text pens
    /// (e.g. <see cref="RichTextRun.UnderlinePen"/>).
    /// </summary>
    /// <returns>A flags enum of the active text decorations.</returns>
    public virtual TextDecorations EnabledDecorations()
        => this.parameters.TextRun.TextDecorations;

    /// <summary>
    /// Override point for subclasses to emit decoration drawing operations.
    /// Called after the base class has built and registered the decoration path
    /// in <see cref="CurrentPaths"/>.
    /// </summary>
    /// <param name="textDecorations">The type of decoration (underline, strikeout, or overline).</param>
    /// <param name="start">The start position of the decoration line.</param>
    /// <param name="end">The end position of the decoration line.</param>
    /// <param name="thickness">The thickness of the decoration line in pixels.</param>
    public virtual void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
    }

    /// <summary>
    /// Truncates a floating-point position to the nearest whole pixel toward negative infinity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point ClampToPixel(PointF point) => Point.Truncate(point);

    /// <summary>
    /// Snaps a decoration endpoint to the pixel grid, taking stroke thickness and
    /// orientation into account. Even-thickness lines snap to whole pixels; odd-thickness
    /// lines snap to half pixels so the stroke center lands on a pixel boundary.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointF ClampToPixel(PointF point, int thickness, bool rotated)
    {
        // Even thickness: snap to whole pixels.
        if ((thickness & 1) == 0)
        {
            return Point.Truncate(point);
        }

        // Odd thickness: snap to half pixels along the perpendicular axis
        // so the 1px-wide center row/column aligns with physical pixels.
        if (rotated)
        {
            return Point.Truncate(point) + new Vector2(.5F, 0);
        }

        return Point.Truncate(point) + new Vector2(0, .5F);
    }

    /// <summary>
    /// Records the start, end, and thickness of a previously emitted decoration line
    /// so that the next adjacent decoration can be stitched seamlessly.
    /// </summary>
    private struct TextDecorationDetails
    {
        /// <summary>Gets or sets the start position of the decoration.</summary>
        public Vector2 Start { get; set; }

        /// <summary>Gets or sets the end position of the decoration.</summary>
        public Vector2 End { get; set; }

        /// <summary>Gets or sets the decoration thickness in pixels.</summary>
        public float Thickness { get; internal set; }
    }
}
