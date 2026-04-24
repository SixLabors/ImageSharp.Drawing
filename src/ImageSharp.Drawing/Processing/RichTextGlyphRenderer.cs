// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.Fonts.Unicode;
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
    private const byte RenderOrderFill = 0;
    private const byte RenderOrderOutline = 1;
    private const byte RenderOrderDecoration = 2;

    private readonly DrawingOptions drawingOptions;

    /// <summary>The default pen supplied by the caller (e.g. from <c>DrawText(..., pen)</c>).</summary>
    private readonly Pen? defaultPen;

    /// <summary>The default brush supplied by the caller (e.g. from <c>DrawText(..., brush)</c>).</summary>
    private readonly Brush? defaultBrush;

    /// <summary>
    /// When the text is laid out along a path, this holds the path internals
    /// for point-along-path queries. <see langword="null"/> for normal (linear) text.
    /// </summary>
    private readonly IPathInternals? path;
    private bool isDisposed;

    // --- Per-glyph mutable state reset in BeginGlyph ---

    /// <summary>The <see cref="TextRun"/> (or <see cref="RichTextRun"/>) governing the current glyph.</summary>
    private TextRun? currentTextRun;

    /// <summary>Brush resolved from the current <see cref="RichTextRun"/>, or <see langword="null"/>.</summary>
    private Brush? currentBrush;

    /// <summary>Pen resolved from the current <see cref="RichTextRun"/>, or <see langword="null"/>.</summary>
    private Pen? currentPen;

    /// <summary>The fill rule for the current color layer (COLR).</summary>
    private FillRule currentFillRule;

    /// <summary>Alpha composition mode active for the current glyph/layer.</summary>
    private PixelAlphaCompositionMode currentCompositionMode;

    /// <summary>Color blending mode active for the current glyph/layer.</summary>
    private PixelColorBlendingMode currentBlendingMode;

    /// <summary>Whether the current glyph uses vertical layout (affects decoration orientation).</summary>
    private bool currentDecorationIsVertical;

    /// <summary>Set to <see langword="true"/> when <see cref="BeginLayer"/> is called, cleared in <see cref="EndGlyph"/>.</summary>
    private bool hasLayer;

    // --- Glyph outline cache ---
    // Glyphs that share the same CacheKey (same glyph id, sub-pixel position quantized
    // to 1/AccuracyMultiple, pen reference, etc.) reuse the translated IPath from the
    // first occurrence. This avoids re-building the full outline for repeated characters.
    //
    // AccuracyMultiple = 8 means sub-pixel positions are quantized to 1/8 px steps.
    // Benchmarked to give <0.2% image difference vs. uncached, with >60% cache hit ratio.
    private const float AccuracyMultiple = 8;

    /// <summary>Maps cache keys to their list of <see cref="GlyphRenderData"/> entries (one per layer).</summary>
    private readonly Dictionary<CacheKey, List<GlyphRenderData>> glyphCache = [];

    /// <summary>Read cursor into the cached layer list for layered cache hits.</summary>
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

    /// <summary>The cache key computed for the current glyph in <see cref="BeginGlyph"/>.</summary>
    private CacheKey currentCacheKey;

    /// <summary>
    /// The transformed (post-<see cref="DrawingOptions.Transform"/>) bounding-box location
    /// of the current glyph. Stored so <see cref="EndGlyph"/> can compute
    /// <see cref="GlyphRenderData.BoundsOffset"/> for future cache-hit render location estimation.
    /// </summary>
    private PointF currentTransformedBoundsLocation;

    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextGlyphRenderer"/> class.
    /// </summary>
    /// <param name="textOptions">Rich text options that may include a layout path and text runs.</param>
    /// <param name="drawingOptions">Drawing options (transform, graphics options) for the text block.</param>
    /// <param name="pen">Default pen for outlined text, or <see langword="null"/> for fill-only.</param>
    /// <param name="brush">Default brush for filled text, or <see langword="null"/> for outline-only.</param>
    public RichTextGlyphRenderer(
        RichTextOptions textOptions,
        DrawingOptions drawingOptions,
        Pen? pen,
        Brush? brush)
        : base(drawingOptions.Transform)
    {
        this.drawingOptions = drawingOptions;
        this.defaultPen = pen;
        this.defaultBrush = brush;
        this.DrawingOperations = [];
        this.currentCompositionMode = drawingOptions.GraphicsOptions.AlphaCompositionMode;
        this.currentBlendingMode = drawingOptions.GraphicsOptions.ColorBlendingMode;

        IPath? path = textOptions.Path;
        if (path is not null)
        {
            // Path-based text: each glyph gets a unique per-position transform,
            // so cache hits are vanishingly rare; disable caching entirely.
            this.rasterizationRequired = true;
            this.noCache = true;
            if (path is IPathInternals internals)
            {
                this.path = internals;
            }
            else
            {
                this.path = new ComplexPolygon(path);
            }
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
        this.currentDecorationIsVertical = parameters.LayoutMode is GlyphLayoutMode.Vertical or GlyphLayoutMode.VerticalRotated;
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
            // Transform the font-metric bounds by the drawing transform so that the
            // sub-pixel position and size reflect the final screen coordinates.
            // Quantize to 1/AccuracyMultiple px steps for cache key comparison.
            RectangleF currentBounds = RectangleF.Transform(
                   new RectangleF(bounds.Location, new SizeF(bounds.Width, bounds.Height)),
                   this.drawingOptions.Transform);

            this.currentTransformedBoundsLocation = currentBounds.Location;

            PointF currentBoundsDelta = currentBounds.Location - ClampToPixel(currentBounds.Location);
            PointF subPixelLocation = new(
                MathF.Round(currentBoundsDelta.X * AccuracyMultiple) / AccuracyMultiple,
                MathF.Round(currentBoundsDelta.Y * AccuracyMultiple) / AccuracyMultiple);

            SizeF subPixelSize = new(
                MathF.Round(currentBounds.Width * AccuracyMultiple) / AccuracyMultiple,
                MathF.Round(currentBounds.Height * AccuracyMultiple) / AccuracyMultiple);

            this.currentCacheKey = CacheKey.FromParameters(
                parameters,
                new RectangleF(subPixelLocation, subPixelSize),
                this.currentPen ?? this.defaultPen);

            if (this.glyphCache.TryGetValue(this.currentCacheKey, out List<GlyphRenderData>? cachedEntries))
            {
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
        // Finalizes a color layer. On a cache miss, translates the built path to local
        // coordinates and stores it for future hits. On a cache hit, reads the stored
        // path and adjusts the render location using sub-pixel delta compensation.
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
        Point renderLocation = ClampToPixel(path.Bounds.Location);
        if (this.noCache || this.rasterizationRequired)
        {
            if (path.Bounds.Equals(RectangleF.Empty))
            {
                return;
            }

            if (renderFill)
            {
                renderData.FillPath = path.Translate(-renderLocation);
                fillPath = renderData.FillPath;
            }

            // Capture the delta between the location and the truncated render location.
            // We can use this to offset the render location on the next instance of this glyph.
            renderData.LocationDelta = (Vector2)(path.Bounds.Location - renderLocation);
            renderData.IsLayered = true;

            if (!this.noCache)
            {
                this.UpdateCache(renderData);
            }
        }
        else
        {
            renderData = this.glyphCache[this.currentCacheKey][this.cacheReadIndex++];

            // Offset the render location by the delta from the cached glyph and this one.
            Vector2 previousDelta = renderData.LocationDelta;
            Vector2 currentLocation = path.Bounds.Location;
            Vector2 currentDelta = path.Bounds.Location - ClampToPixel(path.Bounds.Location);

            if (previousDelta.Y > currentDelta.Y)
            {
                // Move the location down to match the previous location offset.
                currentLocation += new Vector2(0, previousDelta.Y - currentDelta.Y);
            }
            else if (previousDelta.Y < currentDelta.Y)
            {
                // Move the location up to match the previous location offset.
                currentLocation -= new Vector2(0, currentDelta.Y - previousDelta.Y);
            }
            else if (previousDelta.X > currentDelta.X)
            {
                // Move the location right to match the previous location offset.
                currentLocation += new Vector2(previousDelta.X - currentDelta.X, 0);
            }
            else if (previousDelta.X < currentDelta.X)
            {
                // Move the location left to match the previous location offset.
                currentLocation -= new Vector2(currentDelta.X - previousDelta.X, 0);
            }

            renderLocation = ClampToPixel(currentLocation);

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
        // Emits a DrawingOperation for a text decoration. Resolves the decoration pen
        // from the current RichTextRun, re-scales the base-class path when the pen's
        // stroke width differs from the font-metric thickness, and anchors the scaling
        // per decoration type (overline to bottom edge, underline to top edge, strikeout to center).
        // Decorations are not cached.
        if (thickness == 0)
        {
            return;
        }

        Brush? brush = null;
        Pen? pen = null;
        if (this.currentTextRun is RichTextRun drawingRun)
        {
            brush = drawingRun.Brush;

            if (textDecorations == TextDecorations.Strikeout)
            {
                pen = drawingRun.StrikeoutPen ?? pen;
            }
            else if (textDecorations == TextDecorations.Underline)
            {
                pen = drawingRun.UnderlinePen ?? pen;
            }
            else if (textDecorations == TextDecorations.Overline)
            {
                pen = drawingRun.OverlinePen;
            }
        }

        // Always respect the pen stroke width if explicitly set.
        float originalThickness = thickness;
        if (pen is not null)
        {
            // Clamp the thickness to whole pixels.
            thickness = MathF.Max(1F, (float)Math.Round(pen.StrokeWidth));
        }
        else
        {
            // The thickness of the line has already been clamped in the base class.
            pen = new SolidPen((brush ?? this.defaultBrush)!, thickness);
        }

        // Path has already been added to the collection via the base class.
        IPath path = this.CurrentPaths[^1];
        IPath outline = path;

        if (originalThickness != thickness)
        {
            // Respect edge anchoring per decoration type:
            // - Overline: keep the base edge fixed (bottom in horizontal; left in vertical)
            // - Underline: keep the top edge fixed (top in horizontal; right in vertical)
            // - Strikeout: keep the center fixed (default behavior)
            float ratio = thickness / originalThickness;
            if (ratio != 1f)
            {
                Vector2 scale = this.currentDecorationIsVertical
                    ? new Vector2(ratio, 1f)
                    : new Vector2(1f, ratio);

                RectangleF b = path.Bounds;
                Vector2 center = new(b.Left + (b.Width * 0.5f), b.Top + (b.Height * 0.5f));
                Vector2 anchor = center;

                if (textDecorations == TextDecorations.Overline)
                {
                    anchor = this.currentDecorationIsVertical
                        ? new Vector2(b.Left, center.Y) // vertical: anchor left edge
                        : new Vector2(center.X, b.Bottom); // horizontal: anchor bottom edge
                }
                else if (textDecorations == TextDecorations.Underline)
                {
                    anchor = this.currentDecorationIsVertical
                        ? new Vector2(b.Right, center.Y) // vertical: anchor right edge
                        : new Vector2(center.X, b.Top);  // horizontal: anchor top edge
                }

                // Scale about the chosen anchor so the fixed edge stays in place.
                outline = outline.Transform(Matrix4x4.CreateScale(scale.X, scale.Y, 1, new Vector3(anchor, 0)));
            }
        }

        // Render the path here. Decorations are un-cached.
        Point renderLocation = ClampToPixel(outline.Bounds.Location);
        IPath decorationPath = outline.Translate(-renderLocation);
        Brush decorationBrush = pen.StrokeFill;
        this.DrawingOperations.Add(new DrawingOperation
        {
            Kind = DrawingOperationKind.Fill,
            Path = decorationPath,
            RenderLocation = renderLocation,
            IntersectionRule = IntersectionRule.NonZero,
            Brush = decorationBrush,
            RenderPass = RenderOrderDecoration
        });
    }

    /// <inheritdoc/>
    protected override void EndGlyph()
    {
        // If hasLayer is set, layers were already handled by EndLayer; skip.
        // Otherwise, on a cache miss the built path is translated to local coordinates,
        // stored for future hits, and emitted as fill and/or outline DrawingOperations.
        // On a cache hit the stored path is reused with sub-pixel delta compensation.
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
        IPath path = this.CurrentPaths[^1];
        Point renderLocation = ClampToPixel(path.Bounds.Location);
        if (this.noCache || this.rasterizationRequired)
        {
            if (path.Bounds.Equals(RectangleF.Empty))
            {
                return;
            }

            IPath localPath = path.Translate(-renderLocation);
            if (renderFill || renderOutline)
            {
                renderData.FillPath = localPath;
                glyphPath = renderData.FillPath;
            }

            // Capture the delta between the location and the truncated render location.
            // We can use this to offset the render location on the next instance of this glyph.
            renderData.LocationDelta = (Vector2)(path.Bounds.Location - renderLocation);

            // Store the offset between outline bounds and font metric bounds so that
            // cache hits in BeginGlyph can accurately estimate the path location.
            renderData.BoundsOffset = (Vector2)(path.Bounds.Location - this.currentTransformedBoundsLocation);

            if (!this.noCache)
            {
                this.UpdateCache(renderData);
            }
        }
        else
        {
            renderData = this.glyphCache[this.currentCacheKey][this.cacheReadIndex++];

            // Offset the render location by the delta from the cached glyph and this one.
            Vector2 previousDelta = renderData.LocationDelta;
            Vector2 currentLocation = path.Bounds.Location;
            Vector2 currentDelta = path.Bounds.Location - ClampToPixel(path.Bounds.Location);

            if (previousDelta.Y > currentDelta.Y)
            {
                // Move the location down to match the previous location offset.
                currentLocation += new Vector2(0, previousDelta.Y - currentDelta.Y);
            }
            else if (previousDelta.Y < currentDelta.Y)
            {
                // Move the location up to match the previous location offset.
                currentLocation -= new Vector2(0, currentDelta.Y - previousDelta.Y);
            }
            else if (previousDelta.X > currentDelta.X)
            {
                // Move the location right to match the previous location offset.
                currentLocation += new Vector2(previousDelta.X - currentDelta.X, 0);
            }
            else if (previousDelta.X < currentDelta.X)
            {
                // Move the location left to match the previous location offset.
                currentLocation -= new Vector2(currentDelta.X - previousDelta.X, 0);
            }

            renderLocation = ClampToPixel(currentLocation);

            if (renderFill && renderData.FillPath is not null)
            {
                glyphPath = renderData.FillPath;
            }

            if (renderOutline && renderData.FillPath is not null)
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
        PointF estimatedPathLocation = new(
            currentBoundsLocation.X + renderData.BoundsOffset.X,
            currentBoundsLocation.Y + renderData.BoundsOffset.Y);
        Point renderLocation = ComputeCacheHitRenderLocation(estimatedPathLocation, renderData.LocationDelta);

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
                IntersectionRule = outlineRule,
                Pen = pen,
                RenderPass = RenderOrderOutline,
                PixelAlphaCompositionMode = this.currentCompositionMode,
                PixelColorBlendingMode = this.currentBlendingMode
            });
        }
    }

    /// <summary>
    /// Computes the pixel-snapped render location for a cache-hit glyph by compensating
    /// for the sub-pixel delta difference between the original cached glyph and the
    /// current instance. This keeps glyphs visually aligned even when their sub-pixel
    /// positions differ slightly.
    /// </summary>
    /// <param name="pathLocation">The estimated outline bounds origin for the current glyph.</param>
    /// <param name="previousDelta">The sub-pixel delta recorded when the path was first cached.</param>
    /// <returns>A pixel-snapped render location.</returns>
    private static Point ComputeCacheHitRenderLocation(PointF pathLocation, Vector2 previousDelta)
    {
        Vector2 currentLocation = (Vector2)pathLocation;
        Vector2 currentDelta = currentLocation - (Vector2)ClampToPixel(pathLocation);

        if (previousDelta.Y > currentDelta.Y)
        {
            currentLocation += new Vector2(0, previousDelta.Y - currentDelta.Y);
        }
        else if (previousDelta.Y < currentDelta.Y)
        {
            currentLocation -= new Vector2(0, currentDelta.Y - previousDelta.Y);
        }
        else if (previousDelta.X > currentDelta.X)
        {
            currentLocation += new Vector2(previousDelta.X - currentDelta.X, 0);
        }
        else if (previousDelta.X < currentDelta.X)
        {
            currentLocation -= new Vector2(currentDelta.X - previousDelta.X, 0);
        }

        return ClampToPixel(currentLocation);
    }

    /// <summary>
    /// Stores a <see cref="GlyphRenderData"/> entry in the glyph cache under the
    /// current key. Creates the cache list on first insertion for a given key.
    /// </summary>
    private void UpdateCache(GlyphRenderData renderData)
    {
        if (!this.glyphCache.TryGetValue(this.currentCacheKey, out List<GlyphRenderData>? _))
        {
            this.glyphCache[this.currentCacheKey] = [];
        }

        this.glyphCache[this.currentCacheKey].Add(renderData);
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TransformGlyph(in FontRectangle bounds)
        => this.Builder.SetTransform(this.ComputeTransform(in bounds));

    /// <summary>
    /// Computes the combined translation + rotation matrix that places a glyph
    /// along the text path. For linear text (no path), returns <see cref="Matrix4x4.Identity"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Matrix4x4 ComputeTransform(in FontRectangle bounds)
    {
        if (this.path is null)
        {
            return Matrix4x4.Identity;
        }

        // Find the point of this intersection along the given path.
        // We want to find the point on the path that is closest to the center-bottom side of the glyph.
        Vector2 half = new(bounds.Width * .5F, 0);
        SegmentInfo pathPoint = this.path.PointAlongPath(bounds.Left + half.X);

        // Now offset to our target point since we're aligning the top-left location of our glyph against the path.
        Vector2 translation = (Vector2)pathPoint.Point - bounds.Location - half + new Vector2(0, bounds.Top);
        return Matrix4x4.CreateTranslation(translation.X, translation.Y, 0)
            * new Matrix4x4(Matrix3x2.CreateRotation(pathPoint.Angle - MathF.PI, (Vector2)pathPoint.Point));
    }

    /// <summary>
    /// Releases managed resources (glyph cache and drawing operations list).
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release managed resources.</param>
    private void Dispose(bool disposing)
    {
        if (!this.isDisposed)
        {
            if (disposing)
            {
                this.glyphCache.Clear();
                this.DrawingOperations.Clear();
            }

            this.isDisposed = true;
        }
    }

    /// <summary>
    /// Per-layer cached data for a rasterized glyph. Stores the locally-translated
    /// path and the sub-pixel deltas needed to reposition the path at a different
    /// screen location on a cache hit.
    /// </summary>
    private struct GlyphRenderData
    {
        /// <summary>
        /// The fractional-pixel offset between the path's bounding-box origin
        /// and the truncated (pixel-snapped) render location. Used to compensate
        /// for sub-pixel position differences between cache hits.
        /// </summary>
        public Vector2 LocationDelta;

        /// <summary>
        /// The offset between the outline path's bounding-box origin and the
        /// font-metric bounds origin. Stored on first rasterization so that
        /// <see cref="EmitCachedGlyphOperations"/> can estimate the path location
        /// from only the font-metric bounds (which are available without outline data).
        /// </summary>
        public Vector2 BoundsOffset;

        /// <summary>
        /// The glyph outline path translated to local coordinates (origin at 0,0).
        /// Shared across all cache hits for the same <see cref="CacheKey"/>.
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
    /// sub-pixel position (quantized to <see cref="AccuracyMultiple"/>), and the pen reference
    /// (since stroke width affects the outline path).
    /// </summary>
    private readonly struct CacheKey : IEquatable<CacheKey>
    {
        /// <summary>Gets the font family name.</summary>
        public string Font { get; init; }

        /// <summary>Gets the glyph color variant (normal, COLR, etc.).</summary>
        public GlyphColor GlyphColor { get; init; }

        /// <summary>Gets the glyph type (simple, composite, etc.).</summary>
        public GlyphType GlyphType { get; init; }

        /// <summary>Gets the font style (regular, bold, italic, etc.).</summary>
        public FontStyle FontStyle { get; init; }

        /// <summary>Gets the glyph index within the font.</summary>
        public ushort GlyphId { get; init; }

        /// <summary>Gets the composite glyph parent index (0 for non-composite).</summary>
        public ushort CompositeGlyphId { get; init; }

        /// <summary>Gets the Unicode code point this glyph represents.</summary>
        public CodePoint CodePoint { get; init; }

        /// <summary>Gets the em-size at which the glyph is rendered.</summary>
        public float PointSize { get; init; }

        /// <summary>Gets the DPI used for rendering.</summary>
        public float Dpi { get; init; }

        /// <summary>Gets the layout mode (horizontal, vertical, vertical-rotated).</summary>
        public GlyphLayoutMode LayoutMode { get; init; }

        /// <summary>Gets any text attributes (e.g. superscript/subscript) that affect rendering.</summary>
        public TextAttributes TextAttributes { get; init; }

        /// <summary>Gets text decorations that may influence outline geometry.</summary>
        public TextDecorations TextDecorations { get; init; }

        /// <summary>Gets the quantized sub-pixel bounds used for position-sensitive cache lookup.</summary>
        public RectangleF Bounds { get; init; }

        /// <summary>
        /// Gets the pen reference used for outlined text. Compared by reference equality
        /// so that different pen instances (even with the same stroke width) produce
        /// separate cache entries; this is correct because pen identity affects stroke
        /// pattern and dash style.
        /// </summary>
        public Pen? PenReference { get; init; }

        public static bool operator ==(CacheKey left, CacheKey right) => left.Equals(right);

        public static bool operator !=(CacheKey left, CacheKey right) => !(left == right);

        /// <summary>
        /// Creates a <see cref="CacheKey"/> from glyph renderer parameters and quantized bounds.
        /// The grapheme index is intentionally excluded because it varies per glyph instance
        /// while the outline geometry remains the same for matching glyph+position.
        /// </summary>
        /// <param name="parameters">The glyph renderer parameters from the font engine.</param>
        /// <param name="bounds">Quantized sub-pixel bounds for position-sensitive lookup.</param>
        /// <param name="penReference">The pen reference for outlined text, or <see langword="null"/>.</param>
        /// <returns>A new cache key.</returns>
        public static CacheKey FromParameters(
            in GlyphRendererParameters parameters,
            RectangleF bounds,
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
                Bounds = bounds,
                PenReference = penReference
            };

        public override bool Equals(object? obj)
            => obj is CacheKey key && this.Equals(key);

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
            this.Bounds.Equals(other.Bounds) &&
            ReferenceEquals(this.PenReference, other.PenReference);

        public override int GetHashCode()
        {
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
            hash.Add(this.Bounds);
            hash.Add(this.PenReference is null ? 0 : RuntimeHelpers.GetHashCode(this.PenReference));
            return hash.ToHashCode();
        }
    }
}
