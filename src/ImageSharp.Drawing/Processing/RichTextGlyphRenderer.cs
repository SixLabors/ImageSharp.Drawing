// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Text;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

/// <summary>
/// Allows the rendering of rich text configured via <see cref="RichTextOptions"/>.
/// </summary>
internal sealed partial class RichTextGlyphRenderer : BaseGlyphBuilder, IDisposable
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
    /// When the text is laid out along a path, this holds the path
    /// for point-along-path queries. <see langword="null"/> for normal (linear) text.
    /// </summary>
    private readonly IPath? path;

    /// <summary>
    /// Set once <see cref="Dispose()"/> has run; guards against double disposal.
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

    // --- Glyph outline cache ---
    // Glyphs that share the same CacheKey (same glyph id, size, pen reference, etc.) reuse
    // the anchored IPath from the first occurrence. This avoids re-building the full outline
    // for repeated characters.
    //
    // Position is intentionally absent from the key: cached paths are anchored at their exact
    // outline origin, and each emitted operation carries the pixel-snapped location plus the
    // fractional remainder (DrawingOperation.SubPixelOffset). The backends apply the remainder
    // as a residual translation, so one cache entry renders exactly at every position.
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
    /// The cache entries for the current glyph key.
    /// </summary>
    private List<GlyphRenderData> currentCacheEntries = [];

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
    public RichTextGlyphRenderer(
        DrawingOptions drawingOptions,
        IPath? path,
        Pen? pen,
        Brush? brush,
        DrawingTextCache glyphCache)
        : base(drawingOptions.Transform)
    {
        this.drawingOptions = drawingOptions;
        this.defaultPen = pen;
        this.defaultBrush = brush;
        this.glyphCache = glyphCache;
        this.DrawingOperations = [];
        this.currentCompositionMode = drawingOptions.GraphicsOptions.AlphaCompositionMode;
        this.currentBlendingMode = drawingOptions.GraphicsOptions.ColorBlendingMode;

        if (path is not null)
        {
            // Path-based text gives each glyph a unique per-position transform,
            // so cache hits are vanishingly rare; disable caching entirely.
            this.rasterizationRequired = true;
            this.noCache = true;
            this.path = path;
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
        this.currentCacheEntries = [];
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

                if (cachedEntries.Count > 0 && !cachedEntries[0].IsLayered
                    && this.EnabledDecorations() == TextDecorations.None)
                {
                    // Non-layered cache hit without decorations: emit operations directly
                    // and tell the font engine to skip the outline entirely
                    // (no MoveTo/LineTo/SetDecoration/EndGlyph).
                    this.EmitCachedGlyphOperations(cachedEntries[0], currentBounds.Location);
                    return false;
                }

                // Layered or decorated cache hit: let the normal flow handle
                // per-layer state and decoration callbacks.
                this.rasterizationRequired = false;
                return true;
            }
        }

        // Transform the glyph vectors using the original bounds
        // The default transform will automatically be applied.
        this.TransformGlyph(in bounds);
        this.rasterizationRequired = true;
        return true;
    }

    /// <inheritdoc/>
    protected override void BeginLayer(Paint? paint, FillRule fillRule, ClipQuad? clipBounds)
    {
        // Capture the color-layer paint, fill rule, and composite mode.
        // Setting hasLayer tells EndGlyph to skip its default single-layer path emission.
        this.hasLayer = true;
        this.currentFillRule = fillRule;
        if (TryCreateBrush(paint, this.Builder.Transform, out Brush? brush))
        {
            this.currentBrush = brush;
            this.currentCompositionMode = TextUtilities.MapCompositionMode(paint.CompositeMode);
            this.currentBlendingMode = TextUtilities.MapBlendingMode(paint.CompositeMode);
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

        // Path has already been added to the collection via the base class.
        IPath path = this.CurrentPaths[^1];
        PointF boundsLocation = path.Bounds.Location;
        Point renderLocation = ClampToPixel(boundsLocation);
        Vector2 subPixelOffset = (Vector2)(boundsLocation - renderLocation);
        if (this.noCache || this.rasterizationRequired)
        {
            if (path.Bounds.Equals(RectangleF.Empty))
            {
                return;
            }

            if (renderFill)
            {
                renderData.FillPath = path.Translate(-boundsLocation.X, -boundsLocation.Y);
                fillPath = renderData.FillPath;
            }

            renderData.IsLayered = true;

            if (!this.noCache)
            {
                this.UpdateCache(renderData);
            }
        }
        else
        {
            renderData = this.currentCacheEntries[this.cacheReadIndex++];

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
                PixelColorBlendingMode = this.currentBlendingMode
            });
        }

        this.currentFillRule = FillRule.NonZero;
        this.currentCompositionMode = this.drawingOptions.GraphicsOptions.AlphaCompositionMode;
        this.currentBlendingMode = this.drawingOptions.GraphicsOptions.ColorBlendingMode;
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
        // (snapped location + fractional remainder) positions it exactly.
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

        // Path has already been added to the collection via the base class.
        // The path is anchored at its exact outline origin so one cache entry serves every
        // position; the pixel-snapped location and the fractional remainder are carried on
        // the emitted operations and reapplied by the backends.
        IPath path = this.CurrentPaths[^1];
        PointF boundsLocation = path.Bounds.Location;
        Point renderLocation = ClampToPixel(boundsLocation);
        Vector2 subPixelOffset = (Vector2)(boundsLocation - renderLocation);
        if (this.noCache || this.rasterizationRequired)
        {
            if (path.Bounds.Equals(RectangleF.Empty))
            {
                return;
            }

            IPath localPath = path.Translate(-boundsLocation.X, -boundsLocation.Y);
            if (renderFill || renderOutline)
            {
                renderData.FillPath = localPath;
                glyphPath = renderData.FillPath;
            }

            // Store the offset between outline bounds and font metric bounds so that
            // cache hits in BeginGlyph can accurately estimate the path location.
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
            // exactly; no sub-pixel compensation is required.
            renderData = this.currentCacheEntries[this.cacheReadIndex++];

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
                PixelColorBlendingMode = this.currentBlendingMode
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
                PixelColorBlendingMode = this.currentBlendingMode
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
        // The cached path is anchored at its exact outline origin, so the snapped
        // estimate plus its fractional remainder positions it exactly.
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
                PixelColorBlendingMode = this.currentBlendingMode
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
                PixelColorBlendingMode = this.currentBlendingMode
            });
        }
    }

    /// <summary>
    /// Stores a <see cref="GlyphRenderData"/> entry in the glyph cache under the
    /// current key. Creates the cache list on first insertion for a given key.
    /// </summary>
    /// <param name="renderData">The render data to append to the current key's entry list.</param>
    private void UpdateCache(GlyphRenderData renderData)
    {
        this.glyphCache.GetOrAdd(this.currentCacheKey).Add(renderData);
    }

    /// <inheritdoc />
    public void Dispose() => this.Dispose(true);

    /// <summary>
    /// Truncates a floating-point position to the nearest whole pixel toward negative infinity.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Point ClampToPixel(PointF point) => Point.Truncate(point);

    /// <summary>
    /// Applies the path-based transform to the <see cref="BaseGlyphBuilder.Builder"/>
    /// for the current glyph, positioning it along the text path (if any) or
    /// leaving the identity transform for linear text.
    /// </summary>
    /// <param name="bounds">The font-metric bounding rectangle of the glyph.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TransformGlyph(in FontRectangle bounds)
        => this.Builder.SetTransform(this.ComputeTransform(in bounds));

    /// <summary>
    /// Computes the combined translation + rotation matrix that places a glyph
    /// along the text path. For linear text (no path), returns <see cref="Matrix4x4.Identity"/>.
    /// </summary>
    /// <param name="bounds">The font-metric bounding rectangle of the glyph.</param>
    /// <returns>
    /// The glyph placement matrix.
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Matrix4x4 ComputeTransform(in FontRectangle bounds)
    {
        if (this.path is null)
        {
            return Matrix4x4.Identity;
        }

        // Find the point of this intersection along the given path.
        // We want to find the point on the path that is closest to the center-bottom side of the glyph.
        // Aligned text can overflow the path on either side, so overflowing glyphs extrapolate
        // along the boundary tangents.
        Vector2 half = new(bounds.Width * .5F, 0);
        if (!this.path.TryGetPathPointAtDistanceUnbounded(bounds.Left + half.X, out PathPoint pathPoint))
        {
            return Matrix4x4.Identity;
        }

        float angle = GeometryUtilities.DegreeToRadian(pathPoint.Angle);

        // Now offset to our target point since we're aligning the top-left location of our glyph against the path.
        Vector2 translation = (Vector2)pathPoint.Point - bounds.Location - half + new Vector2(0, bounds.Top);
        return Matrix4x4.CreateTranslation(translation.X, translation.Y, 0)
            * new Matrix4x4(Matrix3x2.CreateRotation(angle, (Vector2)pathPoint.Point));
    }

    /// <summary>
    /// Releases managed resources owned by this renderer.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release managed resources.</param>
    private void Dispose(bool disposing)
    {
        if (!this.isDisposed)
        {
            if (disposing)
            {
                // The glyph cache is owned outside this renderer and outlives this draw call.
                this.DrawingOperations.Clear();
            }

            this.isDisposed = true;
        }
    }

    /// <summary>
    /// Per-layer cached data for a rasterized glyph. Stores the path anchored at its exact
    /// outline origin; cache hits position it via their own snapped location and fractional
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
        /// still need the per-layer <c>BeginLayer</c>/<c>EndLayer</c> callbacks.
        /// </summary>
        public bool IsLayered;
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
                PenReference = penReference
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
            ReferenceEquals(this.PenReference, other.PenReference);

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
            return hash.ToHashCode();
        }
    }
}
