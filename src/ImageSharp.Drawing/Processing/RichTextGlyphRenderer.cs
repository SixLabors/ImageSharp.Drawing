// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Text;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

/// <summary>
/// Identifies which renderer callback a cached glyph entry replays. <see cref="Layer"/> is
/// zero so pre-existing non-layered and ink-free entries keep their meaning under
/// <see langword="default"/> initialization.
/// </summary>
internal enum GlyphCacheEntryKind : byte
{
    /// <summary>
    /// A color layer, or the single entry of a non-layered glyph.
    /// </summary>
    Layer = 0,

    /// <summary>
    /// A marker opening an isolated group.
    /// </summary>
    BeginGroup = 1,

    /// <summary>
    /// A marker closing the current group.
    /// </summary>
    EndGroup = 2
}

/// <summary>
/// Allows the rendering of rich text configured via <see cref="RichTextOptions"/>.
/// </summary>
internal sealed partial class RichTextGlyphRenderer : BaseGlyphBuilder
{
    // --- Render-pass ordering constants ---
    // Within DrawTextOperations, operations are sorted first by RenderPass so that
    // fills paint beneath outlines, and outlines beneath decorations.

    /// <summary>
    /// Render pass for glyph fills; painted first (bottom).
    /// </summary>
    private const byte RenderOrderFill = 0;

    /// <summary>
    /// Render pass for glyph outlines; painted above fills.
    /// </summary>
    private const byte RenderOrderOutline = 1;

    /// <summary>
    /// Render pass for text decorations; painted last (top).
    /// </summary>
    private const byte RenderOrderDecoration = 2;

    /// <summary>
    /// The drawing options (transform, graphics options) supplied by the caller.
    /// </summary>
    private readonly DrawingOptions drawingOptions;

    /// <summary>
    /// The default pen supplied by the caller (e.g. from <c>DrawText(..., pen)</c>).
    /// </summary>
    private readonly Pen? defaultPen;

    /// <summary>
    /// The default brush supplied by the caller (e.g. from <c>DrawText(..., brush)</c>).
    /// </summary>
    private readonly Brush? defaultBrush;

    /// <summary>
    /// Set once this renderer's disposal has run; guards the override independently of the base guard.
    /// </summary>
    private bool isDisposed;

    // --- Per-glyph mutable state reset in BeginGlyph ---

    /// <summary>
    /// The <see cref="TextRun"/> (or <see cref="RichTextRun"/>) governing the current glyph.
    /// </summary>
    private TextRun? currentTextRun;

    /// <summary>
    /// Brush resolved from the current <see cref="RichTextRun"/>, or <see langword="null"/>.
    /// </summary>
    private Brush? currentBrush;

    /// <summary>
    /// Pen resolved from the current <see cref="RichTextRun"/>, or <see langword="null"/>.
    /// </summary>
    private Pen? currentPen;

    /// <summary>
    /// The fill rule for the current color layer (COLR).
    /// </summary>
    private FillRule currentFillRule;

    /// <summary>
    /// Alpha composition mode active for the current glyph/layer.
    /// </summary>
    private PixelAlphaCompositionMode currentCompositionMode;

    /// <summary>
    /// Color blending mode active for the current glyph/layer.
    /// </summary>
    private PixelColorBlendingMode currentBlendingMode;

    /// <summary>
    /// Set to <see langword="true"/> when <see cref="BeginLayer"/> is called, cleared in <see cref="EndGlyph"/>.
    /// </summary>
    private bool hasLayer;

    /// <summary>
    /// The transformed bounds used for isolated group layers in the current glyph.
    /// </summary>
    private Rectangle currentCompositeBounds;

    /// <summary>
    /// The nesting depth of open COLR groups. Zero identifies the outermost group and
    /// root-level content, which alone apply the caller's graphics options.
    /// </summary>
    private int groupDepth;

    /// <summary>
    /// The canvas-local rectangle from the font's clip bounds, or <see langword="null"/>
    /// when the current glyph has none. Stamped on every operation the glyph emits; the
    /// canvas narrows each operation's rasterizer interest to it.
    /// </summary>
    private RectangleF? currentGlyphClip;

    /// <summary>
    /// The paint of the current color layer when <see cref="BeginLayer"/> converted it to a
    /// brush, or <see langword="null"/> when the layer falls back to the run brush. Cached
    /// layered replays store this paint so brush conversion can re-run per draw: paint
    /// brushes bake device coordinates and cannot be reused across glyph positions.
    /// </summary>
    private Paint? currentLayerPaint;

    // --- Glyph outline cache ---
    // Glyphs that share the same CacheKey (same glyph id, size, pen reference, etc.) reuse
    // the anchored IPath from the first occurrence. This avoids re-building the full outline
    // for repeated characters.
    //
    // Position is intentionally absent from the key: cached paths are anchored at their exact
    // outline origin, and each emitted operation carries the integer location plus the
    // fractional remainder (DrawingOperation.SubPixelOffset). The backends apply the remainder
    // as a residual translation. Fonts supplies metric bounds at the same resolved origin used
    // for outline emission, including format-specific grid placement, so BoundsOffset maps every
    // cache hit back to the exact position a fresh outline would have produced.
    // The key's size component stays quantized (1/SizeAccuracyMultiple px) because transformed
    // bounds sizes pick up float noise under translation.

    /// <summary>
    /// The reciprocal of the quantization step applied to the cache key's size component.
    /// </summary>
    private const float SizeAccuracyMultiple = 8;

    /// <summary>
    /// Cache storing reusable glyph outline entries.
    /// </summary>
    private readonly DrawingTextCache glyphCache;

    /// <summary>
    /// Read cursor into the cached layer list for layered cache hits.
    /// </summary>
    private int cacheReadIndex;

    /// <summary>
    /// <see langword="true"/> when the current glyph is a cache miss and its outline
    /// must be fully rasterized; <see langword="false"/> on a cache hit (reuse path).
    /// </summary>
    private bool rasterizationRequired;

    /// <summary>
    /// <see langword="true"/> to disable the glyph cache entirely (e.g. path-based text
    /// where every glyph has a unique transform).
    /// </summary>
    private readonly bool noCache;

    /// <summary>
    /// The cache key computed for the current glyph in <see cref="BeginGlyph"/>.
    /// </summary>
    private CacheKey currentCacheKey;

    /// <summary>
    /// The cache entries for the current glyph key. Assigned on a cache hit and only read on
    /// hit paths; the miss path appends through the cache-owned list instead, so no per-glyph
    /// list is allocated here.
    /// </summary>
    private List<GlyphRenderData>? currentCacheEntries;

    /// <summary>
    /// The transformed (post-<see cref="DrawingOptions.Transform"/>) bounding-box location
    /// of the current glyph. Stored so <see cref="EndGlyph"/> can compute
    /// <see cref="GlyphRenderData.BoundsOffset"/> for future cache-hit render location estimation.
    /// </summary>
    private PointF currentTransformedBoundsLocation;

    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextGlyphRenderer"/> class.
    /// </summary>
    /// <param name="drawingOptions">Drawing options (transform, graphics options) for the text.</param>
    /// <param name="path">Optional path to draw the text along.</param>
    /// <param name="pen">Default pen for outlined text, or <see langword="null"/> for fill-only.</param>
    /// <param name="brush">Default brush for filled text, or <see langword="null"/> for outline-only.</param>
    /// <param name="glyphCache">Caller-owned glyph cache shared across renderer instances.</param>
    /// <param name="operations">
    /// The caller-owned operation list this renderer emits into. Passing the canvas's reusable
    /// list keeps its capacity across draws instead of regrowing a fresh list per call; it is
    /// cleared in <see cref="BeginText"/> and must not be shared by concurrently live renderers.
    /// </param>
    public RichTextGlyphRenderer(
        DrawingOptions drawingOptions,
        IPath? path,
        Pen? pen,
        Brush? brush,
        DrawingTextCache glyphCache,
        List<DrawingOperation> operations)
        : base(drawingOptions.Transform)
    {
        this.drawingOptions = drawingOptions;
        this.defaultPen = pen;
        this.defaultBrush = brush;
        this.glyphCache = glyphCache;
        this.DrawingOperations = operations;
        this.currentCompositionMode = drawingOptions.GraphicsOptions.AlphaCompositionMode;
        this.currentBlendingMode = drawingOptions.GraphicsOptions.ColorBlendingMode;

        if (path is not null)
        {
            // Path-based text gives each glyph a unique per-position transform,
            // so cache hits are vanishingly rare; disable caching entirely.
            this.rasterizationRequired = true;
            this.noCache = true;
            this.TextPath = path;
        }
        else if (!MatrixUtilities.IsAffine(drawingOptions.Transform))
        {
            // Projective transforms distort each glyph by its absolute position (a glyph left
            // of the vanishing point shears the opposite way to one on the right), so the
            // position-free cache key would share outlines between differently-distorted
            // instances. Build every outline from scratch instead.
            //
            // Potential upgrade if projective text ever profiles hot: key on a quantized local
            // distortion signature instead of disabling the cache - the 2x2 Jacobian of the
            // projective map evaluated at the glyph anchor (post perspective-divide). Instances
            // with the same quantized Jacobian share shape to first order, restoring hits for
            // same-row repeats with an error bounded by the quantization step.
            this.rasterizationRequired = true;
            this.noCache = true;
        }
    }

    /// <summary>
    /// Gets the list of <see cref="DrawingOperation"/> instances accumulated during text rendering.
    /// After <c>RenderText</c> completes, this list is consumed by
    /// <see cref="DrawingCanvas{TPixel}.DrawTextOperations"/> to build composition commands.
    /// </summary>
    public List<DrawingOperation> DrawingOperations { get; }

    /// <summary>
    /// Gets a value indicating whether per-grapheme glyph collections are aggregated.
    /// This renderer emits drawing operations directly and never reads
    /// <see cref="BaseGlyphBuilder.Glyphs"/>, so the aggregate would be one discarded builder
    /// and two lists per grapheme per draw.
    /// </summary>
    protected override bool CollectsGlyphPaths => false;

    /// <inheritdoc/>
    protected override void BeginText(in FontRectangle bounds) => this.DrawingOperations.Clear();

    /// <inheritdoc/>
    protected override bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        // Resolves the active brush/pen from the text run, computes the cache key,
        // and takes one of three paths:
        //   1. Non-layered cache hit without decorations: emit cached ops, return false (fast path).
        //   2. Layered or decorated cache hit: reuse cached path, return true for EndGlyph/SetDecoration.
        //   3. Cache miss: rasterize from scratch.
        this.cacheReadIndex = 0;
        this.currentCacheEntries = null;
        this.currentTextRun = parameters.TextRun;
        if (parameters.TextRun is RichTextRun drawingRun)
        {
            this.currentBrush = drawingRun.Brush;
            this.currentPen = drawingRun.Pen;
        }
        else
        {
            this.currentBrush = null;
            this.currentPen = null;
        }

        // Group layer bounds must land where the glyph geometry lands: the builder
        // transform carries any path-following placement composed onto the drawing
        // transform, and brushes inside the glyph use that same transform.
        RectangleF compositeBounds = RectangleF.Transform(
            new RectangleF(bounds.Location, new SizeF(bounds.Width, bounds.Height)),
            this.Builder.Transform);

        this.currentCompositeBounds = Rectangle.FromLTRB(
            (int)MathF.Floor(compositeBounds.Left),
            (int)MathF.Floor(compositeBounds.Top),
            (int)MathF.Ceiling(compositeBounds.Right),
            (int)MathF.Ceiling(compositeBounds.Bottom));

        this.groupDepth = 0;

        // The font's clip bounds map to one device rectangle per glyph. Four corners cover
        // every transform; under rotation the min-max rectangle is the tilted shape's Bounds,
        // which keeps slightly too much area, and that area holds no paint. The rectangle is
        // recomputed per draw because the clip transform carries the glyph's placement.
        this.currentGlyphClip = null;
        if (parameters.ClipBounds.HasValue)
        {
            ClipBounds clip = parameters.ClipBounds.Value;
            Matrix4x4 clipTransform = new Matrix4x4(clip.Transform) * this.Builder.Transform;
            FontRectangle clipRect = clip.Bounds;

            Vector2 p0 = Vector2.Transform(new Vector2(clipRect.Left, clipRect.Top), clipTransform);
            Vector2 p1 = Vector2.Transform(new Vector2(clipRect.Right, clipRect.Top), clipTransform);
            Vector2 p2 = Vector2.Transform(new Vector2(clipRect.Right, clipRect.Bottom), clipTransform);
            Vector2 p3 = Vector2.Transform(new Vector2(clipRect.Left, clipRect.Bottom), clipTransform);

            Vector2 min = Vector2.Min(Vector2.Min(p0, p1), Vector2.Min(p2, p3));
            Vector2 max = Vector2.Max(Vector2.Max(p0, p1), Vector2.Max(p2, p3));
            this.currentGlyphClip = RectangleF.FromLTRB(min.X, min.Y, max.X, max.Y);
        }

        if (!this.noCache)
        {
            // Transform the font-metric bounds by the drawing transform so that the size
            // reflects the final screen coordinates. Only the quantized size enters the key:
            // cached paths are position independent, and quantizing absorbs the float noise
            // that transformed sizes pick up under translation.
            RectangleF currentBounds = RectangleF.Transform(
                new RectangleF(bounds.Location, new SizeF(bounds.Width, bounds.Height)),
                this.drawingOptions.Transform);

            this.currentTransformedBoundsLocation = currentBounds.Location;

            SizeF quantizedSize = new(
                MathF.Round(currentBounds.Width * SizeAccuracyMultiple) / SizeAccuracyMultiple,
                MathF.Round(currentBounds.Height * SizeAccuracyMultiple) / SizeAccuracyMultiple);

            this.currentCacheKey = CacheKey.FromParameters(
                parameters,
                quantizedSize,
                this.currentPen ?? this.defaultPen);

            if (this.glyphCache.TryGetValue(this.currentCacheKey, out List<GlyphRenderData>? cachedEntries))
            {
                this.currentCacheEntries = cachedEntries;

                if (cachedEntries.Count > 0 && this.EnabledDecorations() == TextDecorations.None)
                {
                    // Decoration-free cache hit: emit operations directly and tell the font
                    // engine to skip the glyph entirely (no decode, no MoveTo/LineTo, no
                    // layer or composite callbacks, no EndGlyph). Layered glyphs replay
                    // their stored entry sequence; non-layered glyphs replay one entry.
                    if (cachedEntries[0].IsLayered)
                    {
                        this.EmitCachedLayeredGlyphOperations(cachedEntries, currentBounds.Location);
                    }
                    else
                    {
                        this.EmitCachedGlyphOperations(cachedEntries[0], currentBounds.Location);
                    }

                    return false;
                }

                // Decorated cache hit: let the normal flow handle per-layer state and
                // decoration callbacks, consuming one stored entry per callback. For
                // decorated non-layered glyphs the decoded outline is not needed, because
                // the cached path and its stored anchor position the glyph; skipping the
                // build avoids a discarded path graph per glyph per draw. Decorated layered
                // glyphs keep building: each layer's exact built bounds anchor that layer.
                this.rasterizationRequired = false;
                this.OutlineBuildRequired = cachedEntries[0].IsLayered;
                return true;
            }
        }

        this.rasterizationRequired = true;
        return true;
    }

    /// <inheritdoc/>
    protected override void BeginLayer(Paint? paint, FillRule fillRule)
    {
        // Capture the color-layer paint, fill rule, and composite mode.
        // Setting hasLayer tells EndGlyph to skip its default single-layer path emission.
        this.hasLayer = true;
        this.currentFillRule = fillRule;
        this.currentLayerPaint = null;
        if (TryCreateBrush(paint, this.Builder.Transform, out Brush? brush))
        {
            this.currentBrush = brush;
            this.currentLayerPaint = paint;
            this.currentCompositionMode = TextUtilities.MapCompositionMode(paint.CompositeMode);
            this.currentBlendingMode = TextUtilities.MapBlendingMode(paint.CompositeMode);
        }
        else if (this.groupDepth > 0)
        {
            // A layer inside an isolated group must never composite with the caller's
            // modes: a destructive mode such as Src erases the group's accumulated
            // backdrop. When the paint has no brush conversion the modes stay at their
            // reset values, so pin plain source-over here.
            this.currentCompositionMode = PixelAlphaCompositionMode.SrcOver;
            this.currentBlendingMode = PixelColorBlendingMode.Normal;
        }
    }

    /// <inheritdoc/>
    protected override void EndLayer()
    {
        // Finalizes a color layer. On a cache miss, anchors the built path at its exact
        // outline origin and stores it for future hits. On a cache hit, reuses the stored
        // path; this instance's own outline origin positions it exactly.
        GlyphRenderData renderData = default;
        IPath? fillPath = null;

        // Fix up the text runs colors.
        // Only if both brush and pen is null do we fallback to the default value.
        if (this.currentBrush == null && this.currentPen == null)
        {
            this.currentBrush = this.defaultBrush;
            this.currentPen = this.defaultPen;
        }

        // When rendering layers we only fill them.
        // Any drawing of outlines is ignored as that doesn't really make sense.
        bool renderFill = this.currentBrush != null;

        // Path has already been added to the collection via the base class, with any COLR
        // clip box already intersected in, so the anchor below always describes the visible
        // geometry and cached replays inherit the clip for free. Layered glyphs always build
        // their decoded outline, on hits too: each layer is anchored by its own exact built
        // bounds, and COLR components interleave layers across glyph callbacks.
        IPath path = this.CurrentPaths[^1];

        PointF boundsLocation = path.Bounds.Location;
        Point renderLocation = ClampToPixel(boundsLocation);
        Vector2 subPixelOffset = (Vector2)(boundsLocation - renderLocation);
        if (this.noCache || this.rasterizationRequired)
        {
            // Capture everything a callback-free replay needs: the fill rule and paint are
            // font data and bake safely; run-brush layers leave Paint null so the replay
            // resolves the caller-dependent brush and modes live.
            renderData.IsLayered = true;
            renderData.LayerFillRule = this.currentFillRule;
            renderData.Paint = this.currentLayerPaint;
            renderData.CompositionMode = this.currentCompositionMode;
            renderData.BlendingMode = this.currentBlendingMode;

            if (path.Bounds.Equals(RectangleF.Empty))
            {
                // Empty layers still append a marker entry so hit replays consume exactly one
                // entry per layer callback and stay aligned with the font engine's sequence.
                if (!this.noCache)
                {
                    this.UpdateCache(renderData);
                }

                return;
            }

            if (renderFill)
            {
                renderData.FillPath = path.Translate(-boundsLocation.X, -boundsLocation.Y);
                fillPath = renderData.FillPath;
            }

            // The anchor offset lets replays estimate this layer's position from the fresh
            // font-metric bounds alone, exactly like the non-layered replay.
            renderData.BoundsOffset = (Vector2)(boundsLocation - this.currentTransformedBoundsLocation);

            if (!this.noCache)
            {
                this.UpdateCache(renderData);
            }
        }
        else
        {
            // Cache hit: the stored path is anchored at its exact origin, so this instance's
            // own outline origin (snapped location + fractional remainder) positions it
            // exactly; no sub-pixel compensation is required. The entries were assigned by the
            // hit in BeginGlyph.
            renderData = this.currentCacheEntries![this.cacheReadIndex++];

            if (renderFill && renderData.FillPath is not null)
            {
                fillPath = renderData.FillPath;
            }
        }

        if (fillPath is not null)
        {
            IntersectionRule fillRule = TextUtilities.MapFillRule(this.currentFillRule);
            this.DrawingOperations.Add(new DrawingOperation
            {
                Kind = DrawingOperationKind.Fill,
                Path = fillPath,
                RenderLocation = renderLocation,
                SubPixelOffset = subPixelOffset,
                GlyphKey = this.currentCacheKey,
                HasGlyphKey = !this.noCache,
                IntersectionRule = fillRule,
                Brush = this.currentBrush,
                RenderPass = RenderOrderFill,
                PixelAlphaCompositionMode = this.currentCompositionMode,
                PixelColorBlendingMode = this.currentBlendingMode,
                GlyphClip = this.currentGlyphClip
            });
        }

        this.currentFillRule = FillRule.NonZero;
        this.currentCompositionMode = this.drawingOptions.GraphicsOptions.AlphaCompositionMode;
        this.currentBlendingMode = this.drawingOptions.GraphicsOptions.ColorBlendingMode;
        this.currentLayerPaint = null;
    }

    /// <inheritdoc/>
    protected override void BeginGroup(CompositeMode mode)
    {
        this.DrawingOperations.Add(new DrawingOperation
        {
            Kind = DrawingOperationKind.BeginGroup,
            CompositeBounds = this.currentCompositeBounds,
            RenderPass = RenderOrderFill,
            ApplyDrawingOptions = this.groupDepth == 0,
            PixelAlphaCompositionMode = TextUtilities.MapCompositionMode(mode),
            PixelColorBlendingMode = TextUtilities.MapBlendingMode(mode)
        });

        this.RecordMarker(new GlyphRenderData
        {
            IsLayered = true,
            EntryKind = GlyphCacheEntryKind.BeginGroup,
            CompositionMode = TextUtilities.MapCompositionMode(mode),
            BlendingMode = TextUtilities.MapBlendingMode(mode)
        });

        this.groupDepth++;
    }

    /// <inheritdoc/>
    protected override void EndGroup()
    {
        this.groupDepth--;
        this.DrawingOperations.Add(new DrawingOperation
        {
            Kind = DrawingOperationKind.EndGroup,
            CompositeBounds = this.currentCompositeBounds,
            RenderPass = RenderOrderFill
        });

        this.RecordMarker(new GlyphRenderData
        {
            IsLayered = true,
            EntryKind = GlyphCacheEntryKind.EndGroup
        });
    }

    /// <inheritdoc/>
    public override TextDecorations EnabledDecorations()
    {
        // Returns the union of decorations from TextRun.TextDecorations and any
        // decoration pens set on the current RichTextRun. The font engine uses
        // this result to decide which SetDecoration calls to emit.
        TextRun? run = this.currentTextRun;
        TextDecorations decorations = run?.TextDecorations ?? TextDecorations.None;

        if (this.currentTextRun is RichTextRun drawingRun)
        {
            if (drawingRun.UnderlinePen != null)
            {
                decorations |= TextDecorations.Underline;
            }

            if (drawingRun.StrikeoutPen != null)
            {
                decorations |= TextDecorations.Strikeout;
            }

            if (drawingRun.OverlinePen != null)
            {
                decorations |= TextDecorations.Overline;
            }
        }

        return decorations;
    }

    /// <inheritdoc/>
    public override void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
        // Emits a DrawingOperation for one carved decoration segment. The base class has already
        // built the rectangle path at the drawn thickness; here we resolve the pen from the run
        // that was captured while the glyph was live (carving happens a glyph later) and fill the
        // path. Decorations are not cached.
        if (thickness == 0)
        {
            return;
        }

        Brush? brush = null;
        Pen? pen = null;
        if (this.CurrentDecorationRun is RichTextRun drawingRun)
        {
            brush = drawingRun.Brush;

            if (textDecorations == TextDecorations.Strikeout)
            {
                pen = drawingRun.StrikeoutPen;
            }
            else if (textDecorations == TextDecorations.Underline)
            {
                pen = drawingRun.UnderlinePen;
            }
            else if (textDecorations == TextDecorations.Overline)
            {
                pen = drawingRun.OverlinePen;
            }
        }

        // The stroke width is already reflected in the built path; only the fill is taken from the pen.
        pen ??= new SolidPen((brush ?? this.defaultBrush)!, thickness);

        // Path has already been added to the collection via the base class.
        IPath path = this.CurrentPaths[^1];
        Point renderLocation = ClampToPixel(path.Bounds.Location);
        IPath decorationPath = path.Translate(-renderLocation);
        this.DrawingOperations.Add(new DrawingOperation
        {
            Kind = DrawingOperationKind.Fill,
            Path = decorationPath,
            RenderLocation = renderLocation,
            IntersectionRule = IntersectionRule.NonZero,
            Brush = pen.StrokeFill,
            RenderPass = RenderOrderDecoration
        });
    }

    /// <inheritdoc/>
    protected override void EndGlyph()
    {
        // If hasLayer is set, layers were already handled by EndLayer; skip.
        // Otherwise, on a cache miss the built path is anchored at its exact outline origin,
        // stored for future hits, and emitted as fill and/or outline DrawingOperations.
        // On a cache hit the stored path is reused; this instance's own outline origin
        // positions it exactly.
        if (this.hasLayer)
        {
            // The layer has already been rendered.
            this.hasLayer = false;
            return;
        }

        GlyphRenderData renderData = default;
        IPath? glyphPath = null;

        // Fix up the text runs colors.
        // Only if both brush and pen is null do we fallback to the default value.
        if (this.currentBrush == null && this.currentPen == null)
        {
            this.currentBrush = this.defaultBrush;
            this.currentPen = this.defaultPen;
        }

        bool renderFill = false;
        bool renderOutline = false;

        // If we are using the fonts color layers we ignore the request to draw an outline only
        // because that won't really work. Instead we force drawing using fill with the requested color.
        if (this.currentBrush != null)
        {
            renderFill = true;
        }

        if (this.currentPen != null)
        {
            renderOutline = true;
        }

        Point renderLocation;
        Vector2 subPixelOffset;
        if (this.noCache || this.rasterizationRequired)
        {
            // Path has already been added to the collection via the base class.
            // The path is anchored at its exact outline origin so one cache entry serves every
            // position; the pixel-snapped location and the fractional remainder are carried on
            // the emitted operations and reapplied by the backends.
            IPath path = this.CurrentPaths[^1];
            PointF boundsLocation = path.Bounds.Location;
            renderLocation = ClampToPixel(boundsLocation);
            subPixelOffset = (Vector2)(boundsLocation - renderLocation);

            if (path.Bounds.Equals(RectangleF.Empty))
            {
                // Ink-free glyphs (spaces, control glyphs) emit nothing, but they must still
                // populate the cache: an unpopulated key takes the full transform-and-decode
                // miss path again on every later draw of the same text. The default marker has
                // no fill path, so the cached hit paths recognize it and emit nothing.
                if (!this.noCache)
                {
                    this.UpdateCache(renderData);
                }

                return;
            }

            IPath localPath = path.Translate(-boundsLocation.X, -boundsLocation.Y);
            if (renderFill || renderOutline)
            {
                renderData.FillPath = localPath;
                glyphPath = renderData.FillPath;
            }

            // Store the offset between outline bounds and font metric bounds so that
            // cache hits can accurately estimate the path location.
            renderData.BoundsOffset = (Vector2)(boundsLocation - this.currentTransformedBoundsLocation);

            if (!this.noCache)
            {
                this.UpdateCache(renderData);
            }
        }
        else
        {
            // Cache hit: the base class skipped building the decoded outline, so derive the
            // position estimate from the anchor stored with the cached entry, exactly as the
            // fast path does. The stored path is anchored at its exact origin, so the integer
            // component plus its fractional remainder positions it exactly. The entries were
            // assigned by the hit in BeginGlyph.
            renderData = this.currentCacheEntries![this.cacheReadIndex++];
            PointF estimatedPathLocation = new(
                this.currentTransformedBoundsLocation.X + renderData.BoundsOffset.X,
                this.currentTransformedBoundsLocation.Y + renderData.BoundsOffset.Y);

            renderLocation = ClampToPixel(estimatedPathLocation);
            subPixelOffset = (Vector2)(estimatedPathLocation - renderLocation);

            if ((renderFill || renderOutline) && renderData.FillPath is not null)
            {
                glyphPath = renderData.FillPath;
            }
        }

        if (renderFill && glyphPath is not null)
        {
            IntersectionRule fillRule = TextUtilities.MapFillRule(this.currentFillRule);
            this.DrawingOperations.Add(new DrawingOperation
            {
                Kind = DrawingOperationKind.Fill,
                Path = glyphPath,
                RenderLocation = renderLocation,
                SubPixelOffset = subPixelOffset,
                GlyphKey = this.currentCacheKey,
                HasGlyphKey = !this.noCache,
                IntersectionRule = fillRule,
                Brush = this.currentBrush,
                RenderPass = RenderOrderFill,
                PixelAlphaCompositionMode = this.currentCompositionMode,
                PixelColorBlendingMode = this.currentBlendingMode,
                GlyphClip = this.currentGlyphClip
            });
        }

        if (renderOutline && glyphPath is not null)
        {
            IntersectionRule outlineRule = TextUtilities.MapFillRule(this.currentFillRule);
            this.DrawingOperations.Add(new DrawingOperation
            {
                Kind = DrawingOperationKind.Draw,
                Path = glyphPath,
                RenderLocation = renderLocation,
                SubPixelOffset = subPixelOffset,
                GlyphKey = this.currentCacheKey,
                HasGlyphKey = !this.noCache,
                IntersectionRule = outlineRule,
                Pen = this.currentPen,
                RenderPass = RenderOrderOutline,
                PixelAlphaCompositionMode = this.currentCompositionMode,
                PixelColorBlendingMode = this.currentBlendingMode,
                GlyphClip = this.currentGlyphClip
            });
        }
    }

    /// <summary>
    /// Emits fill and/or outline <see cref="DrawingOperation"/>s from a cached
    /// <see cref="GlyphRenderData"/> entry. Called from <see cref="BeginGlyph"/> on a
    /// non-layered, decoration-free cache hit when the font engine is told to skip
    /// the outline entirely (returns <see langword="false"/>).
    /// </summary>
    /// <param name="renderData">The cached render data containing the translated path and location delta.</param>
    /// <param name="currentBoundsLocation">The transformed bounding-box origin for the current glyph instance.</param>
    private void EmitCachedGlyphOperations(GlyphRenderData renderData, PointF currentBoundsLocation)
    {
        // Estimate the outline bounds location using the stored offset between
        // the outline bounds and the font metric bounds from the original glyph.
        // The cached path is anchored at its exact outline origin, so the integer
        // component plus its fractional remainder positions it exactly.
        PointF estimatedPathLocation = new(
            currentBoundsLocation.X + renderData.BoundsOffset.X,
            currentBoundsLocation.Y + renderData.BoundsOffset.Y);

        Point renderLocation = ClampToPixel(estimatedPathLocation);
        Vector2 subPixelOffset = (Vector2)(estimatedPathLocation - renderLocation);

        // Fix up the text runs colors.
        Brush? brush = this.currentBrush;
        Pen? pen = this.currentPen;
        if (brush == null && pen == null)
        {
            brush = this.defaultBrush;
            pen = this.defaultPen;
        }

        IPath? glyphPath = renderData.FillPath;
        if (glyphPath is null)
        {
            return;
        }

        if (brush != null)
        {
            IntersectionRule fillRule = TextUtilities.MapFillRule(this.currentFillRule);
            this.DrawingOperations.Add(new DrawingOperation
            {
                Kind = DrawingOperationKind.Fill,
                Path = glyphPath,
                RenderLocation = renderLocation,
                SubPixelOffset = subPixelOffset,
                GlyphKey = this.currentCacheKey,
                HasGlyphKey = !this.noCache,
                IntersectionRule = fillRule,
                Brush = brush,
                RenderPass = RenderOrderFill,
                PixelAlphaCompositionMode = this.currentCompositionMode,
                PixelColorBlendingMode = this.currentBlendingMode,
                GlyphClip = this.currentGlyphClip
            });
        }

        if (pen != null)
        {
            IntersectionRule outlineRule = TextUtilities.MapFillRule(this.currentFillRule);
            this.DrawingOperations.Add(new DrawingOperation
            {
                Kind = DrawingOperationKind.Draw,
                Path = glyphPath,
                RenderLocation = renderLocation,
                SubPixelOffset = subPixelOffset,
                GlyphKey = this.currentCacheKey,
                HasGlyphKey = !this.noCache,
                IntersectionRule = outlineRule,
                Pen = pen,
                RenderPass = RenderOrderOutline,
                PixelAlphaCompositionMode = this.currentCompositionMode,
                PixelColorBlendingMode = this.currentBlendingMode,
                GlyphClip = this.currentGlyphClip
            });
        }
    }

    /// <summary>
    /// Emits the complete <see cref="DrawingOperation"/> sequence for a layered (COLR) glyph
    /// from its cached entry stream. Called from <see cref="BeginGlyph"/> on a
    /// decoration-free cache hit when the font engine is told to skip the glyph entirely,
    /// so no outline is decoded and no path graph is built. Geometry replays from the
    /// anchored per-layer paths, group bounds and the glyph clip are recomputed per draw,
    /// and paint brushes re-convert with the glyph's positional delta appended to the
    /// drawing transform, because converted brushes bake device coordinates.
    /// </summary>
    /// <param name="entries">The cached entry stream recorded by the build draw.</param>
    /// <param name="currentBoundsLocation">The transformed bounding-box origin for the current glyph instance.</param>
    private void EmitCachedLayeredGlyphOperations(List<GlyphRenderData> entries, PointF currentBoundsLocation)
    {
        Vector2 currentOrigin = currentBoundsLocation;
        Vector2 delta = currentOrigin - entries[0].SourceOrigin;
        Matrix4x4 paintTransform = this.drawingOptions.Transform * Matrix4x4.CreateTranslation(delta.X, delta.Y, 0F);
        int replayDepth = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            GlyphRenderData entry = entries[i];
            switch (entry.EntryKind)
            {
                case GlyphCacheEntryKind.BeginGroup:
                    this.DrawingOperations.Add(new DrawingOperation
                    {
                        Kind = DrawingOperationKind.BeginGroup,
                        CompositeBounds = this.currentCompositeBounds,
                        RenderPass = RenderOrderFill,
                        ApplyDrawingOptions = replayDepth == 0,
                        PixelAlphaCompositionMode = entry.CompositionMode,
                        PixelColorBlendingMode = entry.BlendingMode
                    });

                    replayDepth++;
                    break;

                case GlyphCacheEntryKind.EndGroup:
                    this.DrawingOperations.Add(new DrawingOperation
                    {
                        Kind = DrawingOperationKind.EndGroup,
                        CompositeBounds = this.currentCompositeBounds,
                        RenderPass = RenderOrderFill
                    });

                    replayDepth--;
                    break;

                default:
                    this.EmitCachedLayerFill(entry, currentBoundsLocation, paintTransform, replayDepth);
                    break;
            }
        }
    }

    /// <summary>
    /// Emits one cached color layer at the current glyph position. Brush and mode
    /// resolution mirrors <see cref="BeginLayer"/> and <see cref="EndLayer"/>: paint layers
    /// re-convert their paint and use its baked modes, and run-brush layers resolve the
    /// caller's brush and modes live, with group content pinned to plain source-over.
    /// </summary>
    /// <param name="entry">The cached layer entry.</param>
    /// <param name="currentBoundsLocation">The transformed bounding-box origin for the current glyph instance.</param>
    /// <param name="paintTransform">The drawing transform with the glyph's positional delta appended.</param>
    /// <param name="replayDepth">The current group nesting depth.</param>
    private void EmitCachedLayerFill(GlyphRenderData entry, PointF currentBoundsLocation, Matrix4x4 paintTransform, int replayDepth)
    {
        if (entry.FillPath is null)
        {
            // Ink-free layer marker: the build emitted nothing for it either.
            return;
        }

        PointF estimatedPathLocation = new(
            currentBoundsLocation.X + entry.BoundsOffset.X,
            currentBoundsLocation.Y + entry.BoundsOffset.Y);

        Point renderLocation = ClampToPixel(estimatedPathLocation);
        Vector2 subPixelOffset = (Vector2)(estimatedPathLocation - renderLocation);

        Brush? brush;
        PixelAlphaCompositionMode compositionMode;
        PixelColorBlendingMode blendingMode;
        if (entry.Paint is not null && TryCreateBrush(entry.Paint, paintTransform, out Brush? paintBrush))
        {
            brush = paintBrush;
            compositionMode = entry.CompositionMode;
            blendingMode = entry.BlendingMode;
        }
        else
        {
            // Same fallback ladder as EndLayer: the run brush, then the draw default when no
            // pen claims the glyph, with group content pinned to plain source-over so
            // destructive caller modes cannot erase the isolated backdrop.
            brush = this.currentBrush;
            if (brush is null && this.currentPen is null)
            {
                brush = this.defaultBrush;
            }

            if (replayDepth > 0)
            {
                compositionMode = PixelAlphaCompositionMode.SrcOver;
                blendingMode = PixelColorBlendingMode.Normal;
            }
            else
            {
                compositionMode = this.drawingOptions.GraphicsOptions.AlphaCompositionMode;
                blendingMode = this.drawingOptions.GraphicsOptions.ColorBlendingMode;
            }
        }

        if (brush is null)
        {
            return;
        }

        this.DrawingOperations.Add(new DrawingOperation
        {
            Kind = DrawingOperationKind.Fill,
            Path = entry.FillPath,
            RenderLocation = renderLocation,
            SubPixelOffset = subPixelOffset,
            GlyphKey = this.currentCacheKey,
            HasGlyphKey = true,
            IntersectionRule = TextUtilities.MapFillRule(entry.LayerFillRule),
            Brush = brush,
            RenderPass = RenderOrderFill,
            PixelAlphaCompositionMode = compositionMode,
            PixelColorBlendingMode = blendingMode,
            GlyphClip = this.currentGlyphClip
        });
    }

    /// <summary>
    /// Records one non-layer callback in the glyph cache stream. On a build this appends
    /// the marker entry; on a decorated cache hit it consumes the matching stored entry so
    /// the per-callback read index stays aligned with the font engine's sequence.
    /// </summary>
    /// <param name="entry">The marker entry to append on a build.</param>
    private void RecordMarker(GlyphRenderData entry)
    {
        if (this.noCache)
        {
            return;
        }

        if (this.rasterizationRequired)
        {
            this.UpdateCache(entry);
        }
        else if (this.currentCacheEntries is not null)
        {
            this.cacheReadIndex++;
        }
    }

    /// <summary>
    /// Stores a <see cref="GlyphRenderData"/> entry in the glyph cache under the
    /// current key. Creates the cache list on first insertion for a given key. Every entry
    /// is stamped with the glyph's build-time transformed metric origin so layered replays
    /// can derive the positional delta for paint brushes.
    /// </summary>
    /// <param name="renderData">The render data to append to the current key's entry list.</param>
    private void UpdateCache(GlyphRenderData renderData)
    {
        renderData.SourceOrigin = this.currentTransformedBoundsLocation;
        this.glyphCache.GetOrAdd(this.currentCacheKey).Add(renderData);
    }

    /// <summary>
    /// Truncates a floating-point position to the nearest whole pixel toward negative infinity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point ClampToPixel(PointF point) => Point.Truncate(point);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
        if (disposing)
        {
            // The glyph cache is owned outside this renderer and outlives this draw call.
            this.DrawingOperations.Clear();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Per-layer cached data for a rasterized glyph. Stores the path anchored at its exact
    /// outline origin; cache hits position it via their own integer location and fractional
    /// remainder, so no per-hit compensation state is required.
    /// </summary>
    internal struct GlyphRenderData
    {
        /// <summary>
        /// The offset between the outline path's bounding-box origin and the
        /// font-metric bounds origin. Stored on first rasterization so that
        /// <see cref="EmitCachedGlyphOperations"/> can estimate the path location
        /// from only the font-metric bounds (which are available without outline data).
        /// </summary>
        public Vector2 BoundsOffset;

        /// <summary>
        /// The glyph outline path anchored at its exact outline origin (origin at 0,0 with
        /// no baked sub-pixel fraction). Shared across all cache hits for the same
        /// <see cref="CacheKey"/> regardless of position.
        /// </summary>
        public IPath? FillPath;

        /// <summary>
        /// <see langword="true"/> if this entry belongs to a multi-layer (COLR) glyph.
        /// Non-layered cache hits with no decorations can skip the outline entirely
        /// (return <see langword="false"/> from <see cref="BeginGlyph"/>); layered hits
        /// replay the stored entry sequence, and decorated hits still walk the callbacks
        /// consuming one entry per callback.
        /// </summary>
        public bool IsLayered;

        /// <summary>
        /// Identifies which callback this entry replays. Layer entries carry geometry;
        /// marker entries reproduce the composite group structure.
        /// </summary>
        public GlyphCacheEntryKind EntryKind;

        /// <summary>
        /// The fill rule captured for a layer entry.
        /// </summary>
        public FillRule LayerFillRule;

        /// <summary>
        /// The COLR paint for a layer entry, or <see langword="null"/> when the layer uses
        /// the run brush. Paints are re-converted to brushes on every replay because
        /// converted brushes bake device coordinates for the build position.
        /// </summary>
        public Paint? Paint;

        /// <summary>
        /// The alpha composition mode baked from font data. Valid for layer entries whose
        /// <see cref="Paint"/> is set and for <see cref="GlyphCacheEntryKind.BeginGroup"/>
        /// markers; all other entries resolve caller-dependent modes live at replay.
        /// </summary>
        public PixelAlphaCompositionMode CompositionMode;

        /// <summary>
        /// The color blending mode baked from font data, paired with <see cref="CompositionMode"/>.
        /// </summary>
        public PixelColorBlendingMode BlendingMode;

        /// <summary>
        /// The glyph's transformed metric-bounds origin at build time. Replays subtract this
        /// from the current origin to translate paint brushes to the new position.
        /// </summary>
        public Vector2 SourceOrigin;
    }

    /// <summary>
    /// Identifies a unique glyph variant for caching purposes. Two glyphs with the same
    /// <see cref="CacheKey"/> share identical outline geometry and can reuse the same
    /// <see cref="GlyphRenderData.FillPath"/>. The key includes the glyph id, font metrics,
    /// the transformed size (quantized to <see cref="SizeAccuracyMultiple"/>), and the pen
    /// reference (since stroke width affects the outline path). Position is intentionally
    /// excluded: cached paths are anchored at their exact outline origin and repositioned
    /// per operation.
    /// </summary>
    internal readonly struct CacheKey : IEquatable<CacheKey>
    {
        /// <summary>
        /// Gets the font family name.
        /// </summary>
        public string Font { get; init; }

        /// <summary>
        /// Gets the glyph color variant (normal, COLR, etc.).
        /// </summary>
        public GlyphColor GlyphColor { get; init; }

        /// <summary>
        /// Gets the glyph type (simple, composite, etc.).
        /// </summary>
        public GlyphType GlyphType { get; init; }

        /// <summary>
        /// Gets the font style (regular, bold, italic, etc.).
        /// </summary>
        public FontStyle FontStyle { get; init; }

        /// <summary>
        /// Gets the glyph index within the font.
        /// </summary>
        public ushort GlyphId { get; init; }

        /// <summary>
        /// Gets the composite glyph parent index (0 for non-composite).
        /// </summary>
        public ushort CompositeGlyphId { get; init; }

        /// <summary>
        /// Gets the Unicode code point this glyph represents.
        /// </summary>
        public CodePoint CodePoint { get; init; }

        /// <summary>
        /// Gets the em-size at which the glyph is rendered.
        /// </summary>
        public float PointSize { get; init; }

        /// <summary>
        /// Gets the DPI used for rendering.
        /// </summary>
        public float Dpi { get; init; }

        /// <summary>
        /// Gets the layout mode (horizontal, vertical, vertical-rotated).
        /// </summary>
        public GlyphLayoutMode LayoutMode { get; init; }

        /// <summary>
        /// Gets any text attributes (e.g. superscript/subscript) that affect rendering.
        /// </summary>
        public TextAttributes TextAttributes { get; init; }

        /// <summary>
        /// Gets text decorations that may influence outline geometry.
        /// </summary>
        public TextDecorations TextDecorations { get; init; }

        /// <summary>
        /// Gets the quantized transformed size. Distinguishes scale variants of the same glyph
        /// while quantization absorbs the float noise transformed sizes pick up under translation.
        /// </summary>
        public SizeF Size { get; init; }

        /// <summary>
        /// Gets the pen reference used for outlined text. Compared by reference equality
        /// so that different pen instances (even with the same stroke width) produce
        /// separate cache entries; this is correct because pen identity affects stroke
        /// pattern and dash style.
        /// </summary>
        public Pen? PenReference { get; init; }

        /// <summary>
        /// Gets the color palette selection the glyph's colors were resolved with, or
        /// <see langword="null"/> when the glyph resolves no palette colors. The selection
        /// changes the cached layer paints, so palette variants of one glyph must occupy
        /// separate cache entries.
        /// </summary>
        public FontPalette? FontPalette { get; init; }

        /// <summary>
        /// Determines whether two <see cref="CacheKey"/> instances are equal.
        /// </summary>
        /// <param name="left">The first key to compare.</param>
        /// <param name="right">The second key to compare.</param>
        /// <returns>
        /// <see langword="true"/> if the keys are equal; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool operator ==(CacheKey left, CacheKey right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="CacheKey"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first key to compare.</param>
        /// <param name="right">The second key to compare.</param>
        /// <returns>
        /// <see langword="true"/> if the keys differ; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool operator !=(CacheKey left, CacheKey right) => !(left == right);

        /// <summary>
        /// Creates a <see cref="CacheKey"/> from glyph renderer parameters and the quantized
        /// transformed size. The grapheme index is intentionally excluded because it varies per
        /// glyph instance while the outline geometry remains the same for matching glyphs.
        /// </summary>
        /// <param name="parameters">The glyph renderer parameters from the font engine.</param>
        /// <param name="size">The quantized transformed size distinguishing scale variants.</param>
        /// <param name="penReference">The pen reference for outlined text, or <see langword="null"/>.</param>
        /// <returns>
        /// A new cache key.
        /// </returns>
        public static CacheKey FromParameters(
            in GlyphRendererParameters parameters,
            SizeF size,
            Pen? penReference)
            => new()
            {
                // Do not include the grapheme index as that will
                // always vary per glyph instance.
                Font = parameters.Font,
                GlyphType = parameters.GlyphType,
                FontStyle = parameters.FontStyle,
                GlyphId = parameters.GlyphId,
                CompositeGlyphId = parameters.CompositeGlyphId,
                CodePoint = parameters.CodePoint,
                PointSize = parameters.PointSize,
                Dpi = parameters.Dpi,
                LayoutMode = parameters.LayoutMode,
                TextAttributes = parameters.TextRun.TextAttributes,
                TextDecorations = parameters.TextRun.TextDecorations,
                Size = size,
                PenReference = penReference,
                FontPalette = parameters.FontPalette
            };

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is CacheKey key && this.Equals(key);

        /// <inheritdoc/>
        public bool Equals(CacheKey other)
            => this.Font == other.Font &&
            this.GlyphColor.Equals(other.GlyphColor) &&
            this.GlyphType == other.GlyphType &&
            this.FontStyle == other.FontStyle &&
            this.GlyphId == other.GlyphId &&
            this.CompositeGlyphId == other.CompositeGlyphId &&
            this.CodePoint.Equals(other.CodePoint) &&
            this.PointSize == other.PointSize &&
            this.Dpi == other.Dpi &&
            this.LayoutMode == other.LayoutMode &&
            this.TextAttributes == other.TextAttributes &&
            this.TextDecorations == other.TextDecorations &&
            this.Size.Equals(other.Size) &&
            ReferenceEquals(this.PenReference, other.PenReference) &&
            Equals(this.FontPalette, other.FontPalette);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // Must match Equals: the pen is hashed by reference identity, not value.
            HashCode hash = default;
            hash.Add(this.Font);
            hash.Add(this.GlyphColor);
            hash.Add(this.GlyphType);
            hash.Add(this.FontStyle);
            hash.Add(this.GlyphId);
            hash.Add(this.CompositeGlyphId);
            hash.Add(this.CodePoint);
            hash.Add(this.PointSize);
            hash.Add(this.Dpi);
            hash.Add(this.LayoutMode);
            hash.Add(this.TextAttributes);
            hash.Add(this.TextDecorations);
            hash.Add(this.Size);
            hash.Add(this.PenReference is null ? 0 : RuntimeHelpers.GetHashCode(this.PenReference));
            hash.Add(this.FontPalette);
            return hash.ToHashCode();
        }
    }
}
