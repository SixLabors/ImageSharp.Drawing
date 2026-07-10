// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
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

    /// <summary>
    /// Tracks whether geometry was emitted inside <c>BeginLayer</c>/<c>EndLayer</c> pairs for
    /// this glyph. When <see langword="true"/>, <c>EndGlyph</c> skips its default single-layer
    /// path capture because layers already contributed their paths individually.
    /// </summary>
    private bool usedLayers;

    /// <summary>
    /// Tracks whether we are currently inside a layer block.
    /// Guards against unbalanced <c>EndLayer</c> calls.
    /// </summary>
    private bool inLayer;

    // --- Per-GRAPHEME layered capture ---
    // A grapheme cluster (e.g. a base glyph + COLR v0 color layers) may span
    // multiple BeginGlyph/EndGlyph calls. These fields aggregate all layers
    // belonging to the same grapheme into a single GlyphPathCollection.

    /// <summary>
    /// Accumulates paths and layer descriptors for the grapheme currently being rendered;
    /// <see langword="null"/> when no grapheme is in progress.
    /// </summary>
    private GlyphPathCollection.Builder? graphemeBuilder;

    /// <summary>
    /// The number of paths added to <see cref="graphemeBuilder"/> so far.
    /// The next layer span starts at this index.
    /// </summary>
    private int graphemePathCount;

    /// <summary>
    /// The grapheme index of the aggregate in progress, or -1 when none.
    /// A change in index during <c>BeginGlyph</c> flushes the previous aggregate.
    /// </summary>
    private int currentGraphemeIndex = -1;

    /// <summary>
    /// Completed per-grapheme collections in rendering order; exposed via <see cref="Glyphs"/>.
    /// </summary>
    private readonly List<GlyphPathCollection> currentGlyphs = [];

    // Skip-ink is carved here, across glyphs, rather than in the font engine, because a gap must be
    // widened by the drawn thickness and can extend past a glyph boundary into its neighbours. Each
    // decoration type keeps a two-glyph window: the middle glyph is carved against both neighbours
    // once the following glyph arrives, and the last glyph is flushed in EndText.

    /// <summary>
    /// The underline window (previous and current glyph).
    /// </summary>
    private DecorationLane underlineLane;

    /// <summary>
    /// The overline window (previous and current glyph).
    /// </summary>
    private DecorationLane overlineLane;

    /// <summary>
    /// The strikeout window (previous and current glyph).
    /// </summary>
    private DecorationLane strikeoutLane;

    /// <summary>
    /// Reusable buffer of widened ink gaps gathered while carving one glyph's decoration.
    /// </summary>
    private readonly List<(float Start, float End)> decorationGaps = [];

    // Per-layer (within current grapheme) bookkeeping:

    /// <summary>
    /// Index into the grapheme path list where the current layer's span begins.
    /// </summary>
    private int layerStartIndex;

    /// <summary>
    /// Paint supplied by <c>BeginLayer</c>; <see langword="null"/> means the default foreground.
    /// </summary>
    private Paint? currentLayerPaint;

    /// <summary>
    /// Fill rule supplied by <c>BeginLayer</c> for the current layer.
    /// </summary>
    private FillRule currentLayerFillRule;

    /// <summary>
    /// Clip quad supplied by <c>BeginLayer</c> (COLR v1); applied in <c>EndLayer</c>.
    /// </summary>
    private ClipQuad? currentClipBounds;

    /// <summary>
    /// Dedicated builder for carved decoration segments. The sliding window emits a glyph's
    /// segments after the next glyph has already retargeted <see cref="Builder"/> (path text
    /// sets a per-glyph transform), so decoration geometry is built here under the transform
    /// captured with the owning glyph instead of whatever <see cref="Builder"/> currently holds.
    /// </summary>
    private readonly PathBuilder decorationBuilder = new();

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
    /// Gets the text run of the glyph whose decoration segments are currently being emitted. Carving
    /// happens one glyph after the run was seen, so subclasses that resolve a pen from the run must
    /// read this rather than the live glyph's run.
    /// </summary>
    protected TextRun? CurrentDecorationRun { get; private set; }

    /// <summary>
    /// Called by the font engine after all glyphs in the text block have been rendered.
    /// Flushes any in-progress grapheme aggregate and resets per-text-block state.
    /// </summary>
    void IGlyphRenderer.EndText()
    {
        // Flush the last held glyph of each decoration window before the grapheme is finalized, so
        // its carved segments are captured in the grapheme aggregate.
        this.FlushDecorationLane(TextDecorations.Underline, ref this.underlineLane);
        this.FlushDecorationLane(TextDecorations.Overline, ref this.overlineLane);
        this.FlushDecorationLane(TextDecorations.Strikeout, ref this.strikeoutLane);

        // Finalize the last grapheme, if any:
        if (this.graphemeBuilder is not null && this.graphemePathCount > 0)
        {
            this.currentGlyphs.Add(this.graphemeBuilder.Build());
        }

        this.graphemeBuilder = null;
        this.graphemePathCount = 0;
        this.currentGraphemeIndex = -1;

        this.EndText();
    }

    /// <summary>
    /// Called by the font engine before any glyphs are rendered for a text block.
    /// Forwards to the protected <see cref="BeginText(in FontRectangle)"/> override point.
    /// </summary>
    /// <param name="bounds">The layout bounds of the entire text block.</param>
    void IGlyphRenderer.BeginText(in FontRectangle bounds) => this.BeginText(bounds);

    /// <summary>
    /// Called by the font engine before emitting outline data for a single glyph.
    /// Manages grapheme-cluster transitions and resets per-glyph state.
    /// </summary>
    /// <param name="bounds">The font-metric bounding rectangle of the glyph.</param>
    /// <param name="parameters">Identifies the glyph (id, font, layout mode, text run, etc.).</param>
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
    /// <param name="paint">The paint for this color layer, or <see langword="null"/> for the default foreground.</param>
    /// <param name="fillRule">The fill rule to use when rasterizing this layer.</param>
    /// <param name="clipBounds">Optional clip quad constraining the layer region.</param>
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

            path = path.Clip(BooleanOperation.Intersection, TextUtilities.MapFillRule(this.currentLayerFillRule), polygon);
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
    /// Called by the font engine to report a text decoration (underline, strikeout, or overline)
    /// for the current glyph as the full, untrimmed line plus the intervals where the glyph's ink
    /// crosses it. The line is not drawn immediately: it enters a per-type two-glyph window so the
    /// previous glyph can be carved around its own ink and both neighbours' ink, letting a skip-ink
    /// gap widen by the drawn thickness and reach across a glyph boundary. The middle glyph is
    /// emitted once the following glyph arrives; the last is flushed in <c>EndText</c>.
    /// </summary>
    /// <param name="textDecorations">The type of decoration (underline, strikeout, or overline).</param>
    /// <param name="start">The start position of the full decoration line.</param>
    /// <param name="end">The end position of the full decoration line.</param>
    /// <param name="thickness">The thickness of the decoration line in pixels.</param>
    /// <param name="intersections">The along-line intervals where the glyph's ink crosses the band.</param>
    void IGlyphRenderer.SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness, ReadOnlyMemory<float> intersections)
    {
        if (thickness == 0)
        {
            return;
        }

        ref DecorationLane lane = ref this.LaneFor(textDecorations);

        // Stacked colour-layer glyphs repeat the same decoration once per component; the window would
        // otherwise treat the repeat as the next glyph. Ignore an exact repeat of the held line.
        if (lane.Current.HasValue && lane.Current.Start == start && lane.Current.End == end)
        {
            return;
        }

        // PendingDecoration takes a pooled copy of the ink; the font engine reuses its buffer per
        // glyph. The composed builder transform is captured now because path text retargets the
        // builder per glyph and this decoration is not emitted until the window advances.
        bool rotated = this.parameters.LayoutMode is GlyphLayoutMode.Vertical or GlyphLayoutMode.VerticalRotated;
        PendingDecoration incoming = new(start, end, thickness, intersections, this.parameters.TextRun, rotated, this.Builder.Transform);

        // The held glyph is now flanked on both sides, so carve and emit it, then retire the glyph
        // that leaves the window and dispose its pooled buffer.
        if (lane.Current.HasValue)
        {
            this.CarveDecoration(textDecorations, in lane.Previous, in lane.Current, in incoming);
            lane.Previous.Dispose();
            lane.Previous = lane.Current;
        }

        lane.Current = incoming;
    }

    /// <summary>
    /// Emits the last held glyph of a decoration window, carved against its previous neighbour and
    /// no following neighbour, then clears the window.
    /// </summary>
    /// <param name="textDecorations">The decoration type of the window.</param>
    /// <param name="lane">The window to flush.</param>
    private void FlushDecorationLane(TextDecorations textDecorations, ref DecorationLane lane)
    {
        if (lane.Current.HasValue)
        {
            this.CarveDecoration(textDecorations, in lane.Previous, in lane.Current, in PendingDecoration.None);
            lane.Previous.Dispose();
            lane.Current.Dispose();
        }

        lane.Previous = default;
        lane.Current = default;
    }

    /// <summary>
    /// Carves the middle glyph's decoration line around its own ink and the ink of its previous and
    /// next neighbours, each widened by that glyph's thickness, and emits the pieces that survive.
    /// </summary>
    /// <param name="textDecorations">The decoration type.</param>
    /// <param name="previous">The glyph before the one being carved, if any.</param>
    /// <param name="current">The glyph being carved.</param>
    /// <param name="next">The glyph after the one being carved, if any.</param>
    private void CarveDecoration(
        TextDecorations textDecorations,
        in PendingDecoration previous,
        in PendingDecoration current,
        in PendingDecoration next)
    {
        bool rotated = current.Rotated;
        float lineStart = rotated ? Math.Min(current.Start.Y, current.End.Y) : Math.Min(current.Start.X, current.End.X);
        float lineEnd = rotated ? Math.Max(current.Start.Y, current.End.Y) : Math.Max(current.Start.X, current.End.X);

        // Gather the widened ink gaps that fall within this glyph's extent: its own ink, plus the
        // trailing reach of the previous glyph and the leading reach of the next, each haloed by
        // that glyph's own thickness.
        List<(float Start, float End)> gaps = this.decorationGaps;
        gaps.Clear();
        AddGaps(gaps, in current, lineStart, lineEnd);
        if (previous.HasValue)
        {
            AddGaps(gaps, in previous, lineStart, lineEnd);
        }

        if (next.HasValue)
        {
            AddGaps(gaps, in next, lineStart, lineEnd);
        }

        gaps.Sort(static (x, y) => x.Start.CompareTo(y.Start));

        // Emit the solid pieces between the merged gaps.
        float cursor = lineStart;
        float gapStart = 0F;
        float gapEnd = float.MinValue;
        bool hasGap = false;
        for (int i = 0; i < gaps.Count; i++)
        {
            (float start, float end) = gaps[i];
            if (hasGap && start <= gapEnd)
            {
                gapEnd = Math.Max(gapEnd, end);
                continue;
            }

            if (hasGap)
            {
                this.EmitCarvedSegment(textDecorations, in current, rotated, cursor, Math.Max(cursor, gapStart));
                cursor = Math.Max(cursor, gapEnd);
            }

            gapStart = start;
            gapEnd = end;
            hasGap = true;
        }

        if (hasGap)
        {
            this.EmitCarvedSegment(textDecorations, in current, rotated, cursor, Math.Max(cursor, gapStart));
            cursor = Math.Max(cursor, gapEnd);
        }

        this.EmitCarvedSegment(textDecorations, in current, rotated, cursor, lineEnd);

        // Widens each ink interval of a glyph by its own thickness and clips it to the carved
        // glyph's extent, so only the parts that reach into it open a gap.
        static void AddGaps(List<(float Start, float End)> gaps, in PendingDecoration glyph, float lineStart, float lineEnd)
        {
            ReadOnlySpan<float> ink = glyph.Ink;
            float halo = glyph.Thickness;
            for (int i = 0; i < ink.Length; i += 2)
            {
                float from = Math.Max(ink[i] - halo, lineStart);
                float to = Math.Min(ink[i + 1] + halo, lineEnd);
                if (to > from)
                {
                    gaps.Add((from, to));
                }
            }
        }
    }

    /// <summary>
    /// Maps a carved along-line piece back to line endpoints and emits it as a rectangle path.
    /// </summary>
    private void EmitCarvedSegment(TextDecorations textDecorations, in PendingDecoration current, bool rotated, float from, float to)
    {
        if (to <= from)
        {
            return;
        }

        Vector2 segmentStart = rotated ? new Vector2(current.Start.X, from) : new Vector2(from, current.Start.Y);
        Vector2 segmentEnd = rotated ? new Vector2(current.End.X, to) : new Vector2(to, current.End.Y);
        this.EmitDecorationSegment(textDecorations, segmentStart, segmentEnd, current.Thickness, rotated, current.Run, in current);
    }

    /// <summary>
    /// Builds a filled rectangle path for one carved decoration segment, registers it as a
    /// <see cref="GlyphLayerKind.Decoration"/> layer, and hands it to the drawing override.
    /// The path is built on the dedicated decoration builder under the owning glyph's captured
    /// transform: the window emits a glyph's segments after the shared builder has already been
    /// retargeted to the next glyph, which rotates per glyph when text follows a path.
    /// </summary>
    private void EmitDecorationSegment(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness, bool rotated, TextRun? run, in PendingDecoration owner)
    {
        // Clamp the thickness to whole pixels.
        thickness = MathF.Max(1F, (float)Math.Round(thickness));

        Vector2 pad = rotated ? new Vector2(thickness * .5F, 0) : new Vector2(0, thickness * .5F);

        start = ClampToPixel(start, (int)thickness, rotated);
        end = ClampToPixel(end, (int)thickness, rotated);

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

        // Snap the corners to whole pixels on the cross axis only, keeping the stroke's edges crisp;
        // the along-line coordinates stay exact so skip-ink gap boundaries land symmetrically.
        this.decorationBuilder.Clear();
        this.decorationBuilder.SetTransform(owner.Transform);
        this.decorationBuilder.StartFigure();
        this.decorationBuilder.AddLine(SnapCross(a + offset, rotated), SnapCross(b + offset, rotated));
        this.decorationBuilder.AddLine(SnapCross(b + offset, rotated), SnapCross(c + offset, rotated));
        this.decorationBuilder.AddLine(SnapCross(c + offset, rotated), SnapCross(d + offset, rotated));
        this.decorationBuilder.CloseFigure();

        IPath path = this.decorationBuilder.Build();

        // If the path is degenerate (e.g. zero width line) we just skip it
        // and return. This might happen when clamping moves the points.
        if (path.Bounds.IsEmpty)
        {
            this.decorationBuilder.Clear();
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

        this.decorationBuilder.Clear();
        this.CurrentDecorationRun = run;
        this.SetDecoration(textDecorations, start, end, thickness);
    }

    /// <summary>
    /// Returns the carving window for the given decoration type by reference.
    /// </summary>
    /// <param name="textDecorations">The decoration type.</param>
    /// <returns>The window for the type.</returns>
    private ref DecorationLane LaneFor(TextDecorations textDecorations)
    {
        if (textDecorations == TextDecorations.Underline)
        {
            return ref this.underlineLane;
        }

        if (textDecorations == TextDecorations.Overline)
        {
            return ref this.overlineLane;
        }

        return ref this.strikeoutLane;
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
    /// <returns>
    /// A flags enum of the active text decorations.
    /// </returns>
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
    /// Snaps a decoration endpoint to the pixel grid, taking stroke thickness and orientation
    /// into account. On the cross axis, even-thickness lines snap to whole pixels and
    /// odd-thickness lines snap to half pixels so the 1px-wide center row/column aligns with
    /// physical pixels. The along-line coordinate is ROUNDED to the nearest pixel rather than
    /// floored: rounding keeps abutting segments seamless (two anti-aliased edges composited
    /// separately do not sum to opaque) and keeps short skip-ink pieces crisp, while its
    /// zero-mean error avoids the systematic one-directional gap bias flooring produced.
    /// </summary>
    /// <param name="point">The decoration endpoint to snap.</param>
    /// <param name="thickness">The decoration stroke thickness in whole pixels.</param>
    /// <param name="rotated">
    /// <see langword="true"/> when the glyph uses vertical layout, in which case the
    /// perpendicular (snap) axis is X rather than Y.
    /// </param>
    /// <returns>
    /// The snapped endpoint.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointF ClampToPixel(PointF point, int thickness, bool rotated)
    {
        float half = (thickness & 1) == 0 ? 0F : .5F;

        return rotated
            ? new PointF(MathF.Floor(point.X) + half, MathF.Round(point.Y))
            : new PointF(MathF.Round(point.X), MathF.Floor(point.Y) + half);
    }

    /// <summary>
    /// Snaps a decoration corner to the pixel grid on the cross axis only, so the stroke's
    /// long edges stay crisp while its ends keep their exact along-line positions.
    /// </summary>
    /// <param name="point">The corner to snap.</param>
    /// <param name="rotated">
    /// <see langword="true"/> when the glyph uses vertical layout, in which case the
    /// cross (snap) axis is X rather than Y.
    /// </param>
    /// <returns>
    /// The snapped corner.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static PointF SnapCross(PointF point, bool rotated)
        => rotated
            ? new PointF(MathF.Floor(point.X), point.Y)
            : new PointF(point.X, MathF.Floor(point.Y));

    /// <summary>
    /// One glyph's untrimmed decoration line and the ink it must skip, held in the carving window
    /// until its neighbours are known.
    /// </summary>
    private readonly struct PendingDecoration : IDisposable
    {
        /// <summary>
        /// A pending decoration that is not present (an absent neighbour at a line end).
        /// </summary>
        public static readonly PendingDecoration None;

        /// <summary>
        /// The pooled buffer holding the ink, owned by this instance and handed back by
        /// <see cref="Dispose"/>. Null when the glyph has no ink.
        /// </summary>
        private readonly float[]? rented;

        /// <summary>
        /// The number of valid entries in <see cref="rented"/>.
        /// </summary>
        private readonly int count;

        /// <summary>
        /// Initializes a new instance of the <see cref="PendingDecoration"/> struct, taking a pooled
        /// copy of the ink so it survives after the font engine reuses its per-glyph buffer.
        /// </summary>
        /// <param name="start">The start of the full, untrimmed decoration line.</param>
        /// <param name="end">The end of the full, untrimmed decoration line.</param>
        /// <param name="thickness">The decoration thickness in pixels (drawn width and skip-ink halo).</param>
        /// <param name="intersections">The along-line ink intervals to copy, as <c>[start, end]</c> pairs.</param>
        /// <param name="run">The text run styling the glyph, captured while it was live.</param>
        /// <param name="rotated">Whether the glyph uses vertical layout.</param>
        /// <param name="transform">The composed builder transform of the owning glyph, captured while it was live.</param>
        public PendingDecoration(Vector2 start, Vector2 end, float thickness, ReadOnlyMemory<float> intersections, TextRun? run, bool rotated, Matrix4x4 transform)
        {
            this.HasValue = true;
            this.Start = start;
            this.End = end;
            this.Thickness = thickness;
            this.Run = run;
            this.Rotated = rotated;
            this.Transform = transform;

            if (intersections.IsEmpty)
            {
                this.rented = null;
                this.count = 0;
            }
            else
            {
                // Favor ArrayPool over MemoryAllocator here, as it's faster and the buffers are small and short-lived.
                // The font engine reuses its own buffer per glyph, so we must take a copy to survive until the glyph is carved.
                this.rented = ArrayPool<float>.Shared.Rent(intersections.Length);
                intersections.Span.CopyTo(this.rented);
                this.count = intersections.Length;
            }
        }

        /// <summary>
        /// Gets a value indicating whether this slot holds a glyph.
        /// </summary>
        public bool HasValue { get; }

        /// <summary>
        /// Gets the start of the full, untrimmed decoration line.
        /// </summary>
        public Vector2 Start { get; }

        /// <summary>
        /// Gets the end of the full, untrimmed decoration line.
        /// </summary>
        public Vector2 End { get; }

        /// <summary>
        /// Gets the decoration thickness in pixels, used both as the drawn width and as the
        /// skip-ink halo.
        /// </summary>
        public float Thickness { get; }

        /// <summary>
        /// Gets the along-line ink intervals for this glyph, as <c>[start, end]</c> pairs.
        /// </summary>
        public ReadOnlySpan<float> Ink => new(this.rented, 0, this.count);

        /// <summary>
        /// Gets the text run styling this glyph, captured while it was live so the pen can still be
        /// resolved when the glyph is carved a step later.
        /// </summary>
        public TextRun? Run { get; }

        /// <summary>
        /// Gets a value indicating whether the glyph uses vertical layout.
        /// </summary>
        public bool Rotated { get; }

        /// <summary>
        /// Gets the composed builder transform of the owning glyph. Carved segments are emitted
        /// a window step later, after the shared builder has been retargeted to the next glyph,
        /// so each glyph's decoration geometry must be built under this captured value.
        /// </summary>
        public Matrix4x4 Transform { get; }

        /// <summary>
        /// Returns the pooled ink buffer this decoration owns, if any, to the shared pool.
        /// </summary>
        public void Dispose()
        {
            if (this.rented != null)
            {
                ArrayPool<float>.Shared.Return(this.rented);
            }
        }
    }

    /// <summary>
    /// A two-glyph carving window for one decoration type.
    /// </summary>
    private struct DecorationLane
    {
        /// <summary>
        /// The glyph before the one currently held.
        /// </summary>
        public PendingDecoration Previous;

        /// <summary>
        /// The most recently reported glyph, awaiting its following neighbour before it is carved.
        /// </summary>
        public PendingDecoration Current;
    }
}
