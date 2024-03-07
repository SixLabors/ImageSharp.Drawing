// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

/// <summary>
/// Allows the rendering of rich text configured via <see cref="RichTextOptions"/>.
/// </summary>
internal sealed class RichTextGlyphRenderer : BaseGlyphBuilder, IColorGlyphRenderer, IDisposable
{
    private const byte RenderOrderFill = 0;
    private const byte RenderOrderOutline = 1;
    private const byte RenderOrderDecoration = 2;

    private readonly DrawingOptions drawingOptions;
    private readonly MemoryAllocator memoryAllocator;
    private readonly Pen? defaultPen;
    private readonly Brush? defaultBrush;
    private readonly IPathInternals? path;
    private bool isDisposed;

    private readonly Dictionary<Color, Brush> brushLookup = new();
    private TextRun? currentTextRun;
    private Brush? currentBrush;
    private Pen? currentPen;
    private Color? currentColor;
    private TextDecorationDetails? currentUnderline;
    private TextDecorationDetails? currentStrikeout;
    private TextDecorationDetails? currentOverline;
    private bool currentDecorationIsVertical;

    // Just enough accuracy to allow for 1/8 px differences which later are accumulated while rendering,
    // but do not grow into full px offsets.
    // The value 8 is benchmarked to:
    // - Provide a good accuracy (smaller than 0.2% image difference compared to the non-caching variant)
    // - Cache hit ratio above 60%
    private const float AccuracyMultiple = 8;
    private readonly Dictionary<(GlyphRendererParameters Glyph, RectangleF Bounds), GlyphRenderData> glyphData = new();
    private bool rasterizationRequired;
    private readonly bool noCache;
    private (GlyphRendererParameters Glyph, RectangleF Bounds) currentCacheKey;

    public RichTextGlyphRenderer(
        RichTextOptions textOptions,
        DrawingOptions drawingOptions,
        MemoryAllocator memoryAllocator,
        Pen? pen,
        Brush? brush)
        : base(drawingOptions.Transform)
    {
        this.drawingOptions = drawingOptions;
        this.memoryAllocator = memoryAllocator;
        this.defaultPen = pen;
        this.defaultBrush = brush;
        this.DrawingOperations = new List<DrawingOperation>();

        IPath? path = textOptions.Path;
        if (path is not null)
        {
            // Turn of caching. The chances of a hit are near-zero.
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

    public List<DrawingOperation> DrawingOperations { get; }

    /// <inheritdoc/>
    protected override void BeginText(in FontRectangle bounds)
    {
        foreach (DrawingOperation operation in this.DrawingOperations)
        {
            operation.Map.Dispose();
        }

        this.DrawingOperations.Clear();
    }

    /// <inheritdoc/>
    protected override void BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
    {
        this.currentColor = null;
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
            // Create a cache entry for the glyph.
            // We need to apply the default transform to the bounds to get the correct size
            // for comparison with future glyphs. We can use this cached glyph anywhere in the text block.
            RectangleF currentBounds = RectangleF.Transform(
                   new RectangleF(bounds.Location, new(bounds.Width, bounds.Height)),
                   this.drawingOptions.Transform);

            PointF currentBoundsDelta = currentBounds.Location - ClampToPixel(currentBounds.Location);
            PointF subPixelLocation = new(
                MathF.Round(currentBoundsDelta.X * AccuracyMultiple) / AccuracyMultiple,
                MathF.Round(currentBoundsDelta.Y * AccuracyMultiple) / AccuracyMultiple);

            SizeF subPixelSize = new(
                MathF.Round(currentBounds.Width * AccuracyMultiple) / AccuracyMultiple,
                MathF.Round(currentBounds.Height * AccuracyMultiple) / AccuracyMultiple);

            this.currentCacheKey = (parameters, new RectangleF(subPixelLocation, subPixelSize));
            if (this.glyphData.ContainsKey(this.currentCacheKey))
            {
                // We have already drawn the glyph vectors.
                this.rasterizationRequired = false;
                return;
            }
        }

        // Transform the glyph vectors using the original bounds
        // The default transform will automatically be applied.
        this.TransformGlyph(in bounds);
        this.rasterizationRequired = true;
    }

    /// <inheritdoc/>
    public void SetColor(GlyphColor color)
        => this.currentColor = Color.FromPixel(new Rgba32(color.Red, color.Green, color.Blue, color.Alpha));

    public override TextDecorations EnabledDecorations()
    {
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

    public override void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
        if (thickness == 0)
        {
            return;
        }

        ref TextDecorationDetails? targetDecoration = ref this.currentStrikeout;
        if (textDecorations == TextDecorations.Strikeout)
        {
            targetDecoration = ref this.currentStrikeout;
        }
        else if (textDecorations == TextDecorations.Underline)
        {
            targetDecoration = ref this.currentUnderline;
        }
        else if (textDecorations == TextDecorations.Overline)
        {
            targetDecoration = ref this.currentOverline;
        }
        else
        {
            return;
        }

        Pen? pen = null;
        if (this.currentTextRun is RichTextRun drawingRun)
        {
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
        if (pen is not null)
        {
            thickness = pen.StrokeWidth;
        }
        else
        {
            // Clamp the thickness to whole pixels.
            // Brush cannot be null if pen is null.
            thickness = MathF.Max(1F, MathF.Round(thickness));
            pen = new SolidPen((this.currentBrush ?? this.defaultBrush)!, thickness);
        }

        // Drawing is always centered around the point so we need to offset by half.
        Vector2 offset = Vector2.Zero;
        bool rotated = this.currentDecorationIsVertical;
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

        // We clamp the start and end points to the pixel grid to avoid anti-aliasing.
        this.AppendDecoration(
            ref targetDecoration,
            ClampToPixel(start + offset, (int)thickness, rotated),
            ClampToPixel(end + offset, (int)thickness, rotated),
            pen,
            thickness,
            rotated);
    }

    protected override void EndGlyph()
    {
        GlyphRenderData renderData = default;

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
        // If color fonts are disabled then this.currentColor will always be null.
        if (this.currentBrush != null || this.currentColor != null)
        {
            renderFill = true;
            if (this.currentColor.HasValue)
            {
                if (this.brushLookup.TryGetValue(this.currentColor.Value, out Brush? brush))
                {
                    this.currentBrush = brush;
                }
                else
                {
                    this.currentBrush = new SolidBrush(this.currentColor.Value);
                    this.brushLookup[this.currentColor.Value] = this.currentBrush;
                }
            }
        }

        if (this.currentPen != null && this.currentColor == null)
        {
            renderOutline = true;
        }

        // Path has already been added to the collection via the base class.
        IPath path = this.Paths.Last();
        Point renderLocation = ClampToPixel(path.Bounds.Location);
        if (this.noCache || this.rasterizationRequired)
        {
            if (path.Bounds.Equals(RectangleF.Empty))
            {
                return;
            }

            if (renderFill)
            {
                renderData.FillMap = this.Render(path);
            }

            if (renderOutline)
            {
                path = this.currentPen!.GeneratePath(path);
                renderData.OutlineMap = this.Render(path);
            }

            // Capture the delta between the location and the truncated render location.
            // We can use this to offset the render location on the next instance of this glyph.
            renderData.LocationDelta = (Vector2)(path.Bounds.Location - renderLocation);

            if (!this.noCache)
            {
                this.glyphData[this.currentCacheKey] = renderData;
            }
        }
        else
        {
            renderData = this.glyphData[this.currentCacheKey];

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
        }

        if (renderData.FillMap != null)
        {
            this.DrawingOperations.Add(new DrawingOperation
            {
                RenderLocation = renderLocation,
                Map = renderData.FillMap,
                Brush = this.currentBrush!,
                RenderPass = RenderOrderFill
            });
        }

        if (renderData.OutlineMap != null)
        {
            this.DrawingOperations.Add(new DrawingOperation
            {
                RenderLocation = renderLocation,
                Map = renderData.OutlineMap,
                Brush = this.currentPen?.StrokeFill ?? this.currentBrush!,
                RenderPass = RenderOrderOutline
            });
        }
    }

    protected override void EndText()
    {
        // Ensure we have captured the last overline/underline/strikeout path
        this.FinalizeDecoration(ref this.currentOverline);
        this.FinalizeDecoration(ref this.currentUnderline);
        this.FinalizeDecoration(ref this.currentStrikeout);
    }

    public void Dispose() => this.Dispose(true);

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

    // Point.Truncate(point);
    private void FinalizeDecoration(ref TextDecorationDetails? decoration)
    {
        if (decoration != null)
        {
            // TODO: If the path is curved a line segment does not work well.
            // What would be great would be if we could take a slice of a path given start and end positions.
            IPath path = new Path(new LinearLineSegment(decoration.Value.Start, decoration.Value.End));
            IPath outline = decoration.Value.Pen.GeneratePath(path, decoration.Value.Thickness);

            // Calculate the transform for this path.
            // We cannot use the path builder transform as this path is rendered independently.
            FontRectangle rectangle = new(outline.Bounds.Location, new(outline.Bounds.Width, outline.Bounds.Height));
            Matrix3x2 pathTransform = this.ComputeTransform(in rectangle);
            Matrix3x2 defaultTransform = this.drawingOptions.Transform;
            outline = outline.Transform(pathTransform * defaultTransform);

            if (outline.Bounds.Width != 0 && outline.Bounds.Height != 0)
            {
                // Render the path here. Decorations are un-cached.
                this.DrawingOperations.Add(new DrawingOperation
                {
                    Brush = decoration.Value.Pen.StrokeFill,
                    RenderLocation = ClampToPixel(outline.Bounds.Location),
                    Map = this.Render(outline),
                    RenderPass = RenderOrderDecoration
                });
            }

            decoration = null;
        }
    }

    private void AppendDecoration(
        ref TextDecorationDetails? decoration,
        Vector2 start,
        Vector2 end,
        Pen pen,
        float thickness,
        bool rotated)
    {
        if (decoration != null)
        {
            // TODO: This only works well if we are not trying to follow a path.
            if (this.path is null)
            {
                // Let's try and expand it first.
                if (rotated)
                {
                    if (thickness == decoration.Value.Thickness
                    && decoration.Value.End.Y + 1 >= start.Y
                    && decoration.Value.End.X == start.X
                    && decoration.Value.Pen.Equals(pen))
                    {
                        // Expand the line
                        start = decoration.Value.Start;

                        // If this is null finalize does nothing.
                        decoration = null;
                    }
                }
                else if (thickness == decoration.Value.Thickness
                     && decoration.Value.End.Y == start.Y
                     && decoration.Value.End.X + 1 >= start.X
                     && decoration.Value.Pen.Equals(pen))
                {
                    // Expand the line
                    start = decoration.Value.Start;

                    // If this is null finalize does nothing.
                    decoration = null;
                }
            }
        }

        this.FinalizeDecoration(ref decoration);
        decoration = new TextDecorationDetails
        {
            Start = start,
            End = end,
            Pen = pen,
            Thickness = thickness
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void TransformGlyph(in FontRectangle bounds)
        => this.Builder.SetTransform(this.ComputeTransform(in bounds));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Matrix3x2 ComputeTransform(in FontRectangle bounds)
    {
        if (this.path is null)
        {
            return Matrix3x2.Identity;
        }

        // Find the point of this intersection along the given path.
        // We want to find the point on the path that is closest to the center-bottom side of the glyph.
        Vector2 half = new(bounds.Width * .5F, 0);
        SegmentInfo pathPoint = this.path.PointAlongPath(bounds.Left + half.X);

        // Now offset to our target point since we're aligning the top-left location of our glyph against the path.
        Vector2 translation = (Vector2)pathPoint.Point - bounds.Location - half + new Vector2(0, bounds.Top);
        return Matrix3x2.CreateTranslation(translation) * Matrix3x2.CreateRotation(pathPoint.Angle - MathF.PI, (Vector2)pathPoint.Point);
    }

    private Buffer2D<float> Render(IPath path)
    {
        // We need to offset the path now by the difference between the clamped location and the
        // path location.
        IPath offsetPath = path.Translate(-ClampToPixel(path.Bounds.Location));
        Size size = Rectangle.Ceiling(offsetPath.Bounds).Size;

        // Pad to prevent edge clipping.
        size += new Size(2, 2);

        int subpixelCount = FillPathProcessor.MinimumSubpixelCount;
        float xOffset = .5F;
        GraphicsOptions graphicsOptions = this.drawingOptions.GraphicsOptions;
        if (graphicsOptions.Antialias)
        {
            xOffset = 0F; // We are antialiasing. Skip offsetting as real antialiasing should take care of offset.
            subpixelCount = Math.Max(subpixelCount, graphicsOptions.AntialiasSubpixelDepth);
        }

        // Take the path inside the path builder, scan thing and generate a Buffer2D representing the glyph.
        Buffer2D<float> buffer = this.memoryAllocator.Allocate2D<float>(size.Width, size.Height, AllocationOptions.Clean);

        PolygonScanner scanner = PolygonScanner.Create(
            offsetPath,
            0,
            size.Height,
            subpixelCount,
            IntersectionRule.NonZero,
            this.memoryAllocator);

        try
        {
            while (scanner.MoveToNextPixelLine())
            {
                Span<float> scanline = buffer.DangerousGetRowSpan(scanner.PixelLineY);
                bool scanlineDirty = scanner.ScanCurrentPixelLineInto(0, xOffset, scanline);

                if (scanlineDirty && !graphicsOptions.Antialias)
                {
                    for (int x = 0; x < size.Width; x++)
                    {
                        if (scanline[x] >= 0.5)
                        {
                            scanline[x] = 1;
                        }
                        else
                        {
                            scanline[x] = 0;
                        }
                    }
                }
            }
        }
        finally
        {
            // Can't use ref struct as a 'ref' or 'out' value when 'using' so as it is readonly explicitly dispose.
            scanner.Dispose();
        }

        return buffer;
    }

    private void Dispose(bool disposing)
    {
        if (!this.isDisposed)
        {
            if (disposing)
            {
                foreach (KeyValuePair<(GlyphRendererParameters Glyph, RectangleF Bounds), GlyphRenderData> kv in this.glyphData)
                {
                    kv.Value.Dispose();
                }

                this.glyphData.Clear();

                foreach (DrawingOperation operation in this.DrawingOperations)
                {
                    operation.Map.Dispose();
                }

                this.DrawingOperations.Clear();
            }

            this.isDisposed = true;
        }
    }

    private struct GlyphRenderData : IDisposable
    {
        public Vector2 LocationDelta;

        public Buffer2D<float> FillMap;

        public Buffer2D<float> OutlineMap;

        public readonly void Dispose()
        {
            this.FillMap?.Dispose();
            this.OutlineMap?.Dispose();
        }
    }

    private struct TextDecorationDetails
    {
        public Vector2 Start { get; set; }

        public Vector2 End { get; set; }

        public Pen Pen { get; set; }

        public float Thickness { get; internal set; }
    }
}
