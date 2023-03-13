// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text
{
    internal class CachingGlyphRenderer : IColorGlyphRenderer, IDisposable
    {
        // Just enough accuracy to allow for 1/8 pixel differences which
        // later are accumulated while rendering, but do not grow into full pixel offsets
        // The value 8 is benchmarked to:
        // - Provide a good accuracy (smaller than 0.2% image difference compared to the non-caching variant)
        // - Cache hit ratio above 60%
        private const float AccuracyMultiple = 8;
        private readonly Matrix3x2 transform;
        private readonly PathBuilder builder;
        private readonly Dictionary<Color, Brush> brushLookup = new();

        private Point currentRenderPosition;
        private (GlyphRendererParameters Glyph, PointF SubPixelOffset) currentGlyphRenderParams;
        private readonly int offset;
        private PointF currentPoint;
        private Color? currentColor;
        private TextRun currentTextRun;
        private Brush currentBrush;
        private Pen currentPen;

        private TextDecorationDetails? currentUnderline = null;
        private TextDecorationDetails? currentStrikout = null;
        private TextDecorationDetails? currentOverline = null;

        private readonly Dictionary<(GlyphRendererParameters Glyph, PointF SubPixelOffset), GlyphRenderData> glyphData = new();

        private bool rasterizationRequired;

        public CachingGlyphRenderer(
            MemoryAllocator memoryAllocator,
            int size,
            TextDrawingOptions textOptions,
            Pen pen,
            Brush brush,
            Matrix3x2 transform)
        {
            this.MemoryAllocator = memoryAllocator;
            this.currentRenderPosition = default;
            this.Pen = pen;
            this.Brush = brush;
            this.offset = (int)textOptions.Font.Size;
            this.DrawingOperations = new List<DrawingOperation>(size);
            this.transform = transform;
            this.builder = new PathBuilder();
        }

        public List<DrawingOperation> DrawingOperations { get; }

        public MemoryAllocator MemoryAllocator { get; internal set; }

        public Pen Pen { get; internal set; }

        public Brush Brush { get; internal set; }

        public DrawingOptions Options { get; internal set; }

        protected void SetLayerColor(Color color) => this.currentColor = color;

        public void SetColor(GlyphColor color) => this.SetLayerColor(new Color(new Rgba32(color.Red, color.Green, color.Blue, color.Alpha)));

        public void BeginFigure() => this.builder.StartFigure();

        public TextDecorations EnabledDecorations()
        {
            TextDecorations decorations = this.currentTextRun.TextDecorations;
            if (this.currentTextRun is TextDrawingRun drawingRun)
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

        public void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
        {
            if (thickness == 0)
            {
                return;
            }

            ref TextDecorationDetails? targetDecoration = ref this.currentStrikout;
            if (textDecorations == TextDecorations.Strikeout)
            {
                targetDecoration = ref this.currentStrikout;
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

            Pen pen = null;
            if (this.currentTextRun is TextDrawingRun drawingRun)
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

            // Clamp the line to whole pixels
            Vector2 thicknessOffset = new(0, thickness * .5F);
            Vector2 tl = start - thicknessOffset;
            Vector2 bl = start + thicknessOffset;
            Vector2 tr = end - thicknessOffset;
            tl.Y = MathF.Floor(tl.Y);
            tr.Y = MathF.Floor(tr.Y);
            bl.Y = MathF.Ceiling(bl.Y);
            tl.X = MathF.Floor(tl.X);
            tr.X = MathF.Floor(tr.X);
            float newThickness = bl.Y - tl.Y;
            Vector2 offsetNew = new(0, newThickness * .5f);

            pen ??= new SolidPen(this.currentBrush ?? this.Brush);
            this.AppendDecoration(ref targetDecoration, tl + offsetNew, tr + offsetNew, pen, newThickness);
        }

        private void FinaliseDecoration(ref TextDecorationDetails? decoration)
        {
            if (decoration != null)
            {
                Path path = new(new LinearLineSegment(decoration.Value.Start, decoration.Value.End));
                RectangleF currentBounds = path.Bounds;
                var currentRenderPosition = Point.Truncate(currentBounds.Location);
                PointF subPixelOffset = currentBounds.Location - this.currentRenderPosition;

                subPixelOffset.X = MathF.Round(subPixelOffset.X * AccuracyMultiple) / AccuracyMultiple;
                subPixelOffset.Y = MathF.Round(subPixelOffset.Y * AccuracyMultiple) / AccuracyMultiple;

                var additionalOffset = new Size(2, 2);
                IPath offsetPath = path.Translate(new Point(-currentRenderPosition.X, -currentRenderPosition.Y) + additionalOffset);
                IPath outline = decoration.Value.Pen.GeneratePath(offsetPath, decoration.Value.Thickness);

                if (outline.Bounds.Width != 0 && outline.Bounds.Height != 0)
                {
                    // render the Path here
                    this.DrawingOperations.Add(new DrawingOperation
                    {
                        Brush = decoration.Value.Pen.StrokeFill,
                        Location = currentRenderPosition - additionalOffset,
                        Map = this.Render(outline),
                        RenderPass = 3 // after outlines !!
                    });
                }

                decoration = null;
            }
        }

        private void AppendDecoration(ref TextDecorationDetails? decoration, Vector2 start, Vector2 end, Pen pen, float thickness)
        {
            if (decoration != null)
            {
                // Let's try and expand it first.
                // Do we need some leway here, does it only need to be the same line height?
                if (thickness == decoration.Value.Thickness &&
                    decoration.Value.End.Y == start.Y &&
                    (decoration.Value.End.X + 1) >= start.X &&
                    decoration.Value.Pen.Equals(pen))
                {
                    // Expand the line
                    start = decoration.Value.Start;

                    // If this is null finalize does nothing we then set it again before we leave
                    decoration = null;
                }
            }

            this.FinaliseDecoration(ref decoration);
            decoration = new TextDecorationDetails
            {
                Start = start,
                End = end,
                Pen = pen,
                Thickness = MathF.Abs(thickness)
            };
        }

        public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
        {
            this.currentColor = null;

            this.currentTextRun = parameters.TextRun;
            if (parameters.TextRun is TextDrawingRun drawingRun)
            {
                this.currentBrush = drawingRun.Brush;
                this.currentPen = drawingRun.Pen;
            }
            else
            {
                this.currentBrush = null;
                this.currentPen = null;
            }

            RectangleF currentBounds = new RectangularPolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                .Transform(this.transform)
                .Bounds;

            this.currentRenderPosition = Point.Truncate(currentBounds.Location);
            PointF subPixelOffset = currentBounds.Location - this.currentRenderPosition;

            subPixelOffset.X = MathF.Round(subPixelOffset.X * AccuracyMultiple) / AccuracyMultiple;
            subPixelOffset.Y = MathF.Round(subPixelOffset.Y * AccuracyMultiple) / AccuracyMultiple;

            // We have offset our rendering origin a little bit down to prevent edge cropping, move the draw origin up to compensate
            this.currentRenderPosition = new Point(this.currentRenderPosition.X - this.offset, this.currentRenderPosition.Y - this.offset);
            this.currentGlyphRenderParams = (parameters, subPixelOffset);

            if (this.glyphData.ContainsKey(this.currentGlyphRenderParams))
            {
                // We have already drawn the glyph vectors skip trying again
                this.rasterizationRequired = false;
                return false;
            }

            // We check to see if we have a render cache and if we do then we render else
            this.builder.Clear();

            // Ensure all glyphs render around [zero, zero]  so offset negative root positions so when we draw the glyph we can offset it back
            Vector2 originTransform = new(-(int)currentBounds.X + this.offset, -(int)currentBounds.Y + this.offset);

            Matrix3x2 transform = this.transform;
            transform.Translation += originTransform;
            this.builder.SetTransform(transform);

            this.rasterizationRequired = true;
            return true;
        }

        public void BeginText(in FontRectangle bounds) =>

            // Not concerned about this one
            this.DrawingOperations.Clear();

        public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
        {
            this.builder.AddCubicBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);
            this.currentPoint = point;
        }

        public void Dispose()
        {
            foreach (KeyValuePair<(GlyphRendererParameters Glyph, PointF SubPixelOffset), GlyphRenderData> kv in this.glyphData)
            {
                kv.Value.Dispose();
            }

            foreach (DrawingOperation operation in this.DrawingOperations)
            {
                operation.Map.Dispose();
            }

            this.glyphData.Clear();
        }

        public void EndFigure() => this.builder.CloseFigure();

        public void EndGlyph()
        {
            GlyphRenderData renderData = default;

            // fix up the text runs colors
            // only if both brush and pen is null do we fallback to the defualt value
            if (this.currentBrush == null && this.currentPen == null)
            {
                this.currentBrush = this.Brush;
                this.currentPen = this.Pen;
            }

            bool renderFill = false;
            bool renderOutline = false;

            // If we are using the fonts color layers we ignore the request to draw an outline only
            // cause that wont really work and instead force drawing with fill with the requested color
            // if color fonts disabled then this.currentColor will always be null
            if (this.currentBrush != null || this.currentColor != null)
            {
                renderFill = true;
                if (this.currentColor.HasValue)
                {
                    if (this.brushLookup.TryGetValue(this.currentColor.Value, out Brush brush))
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

            // Has the glyph been rendered already?
            if (this.rasterizationRequired)
            {
                IPath path = this.builder.Build();

                if (path.Bounds.Equals(RectangleF.Empty))
                {
                    return;
                }

                // If we are using the fonts color layers we ignore the request to draw an outline only
                // cause that wont really work and instead force drawing with fill with the requested color
                // if color fonts disabled then this.currentColor will always be null
                if (renderFill)
                {
                    renderData.FillMap = this.Render(path);
                }

                if (renderOutline)
                {
                    path = this.currentPen.GeneratePath(path);
                    renderData.OutlineMap = this.Render(path);
                }

                this.glyphData[this.currentGlyphRenderParams] = renderData;
            }
            else
            {
                renderData = this.glyphData[this.currentGlyphRenderParams];
            }

            if (renderData.FillMap != null)
            {
                this.DrawingOperations.Add(new DrawingOperation
                {
                    Location = this.currentRenderPosition,
                    Map = renderData.FillMap,
                    Brush = this.currentBrush,
                    RenderPass = 1
                });
            }

            if (renderData.OutlineMap != null)
            {
                this.DrawingOperations.Add(new DrawingOperation
                {
                    Location = this.currentRenderPosition,
                    Map = renderData.OutlineMap,
                    Brush = this.currentPen?.StrokeFill ?? this.currentBrush,
                    RenderPass = 2 // render outlines 2nd to ensure they are always ontop of fills
                });
            }
        }

        private Buffer2D<float> Render(IPath path)
        {
            Size size = Rectangle.Ceiling(new RectangularPolygon(path.Bounds)
                        .Transform(this.Options.Transform)
                        .Bounds)
                        .Size;

            size = new Size(size.Width + (this.offset * 2), size.Height + (this.offset * 2));

            int subpixelCount = FillPathProcessor.MinimumSubpixelCount;
            float xOffset = 0.5f;
            GraphicsOptions graphicsOptions = this.Options.GraphicsOptions;
            if (graphicsOptions.Antialias)
            {
                xOffset = 0f; // We are antialiasing skip offsetting as real antialiasing should take care of offset.
                subpixelCount = Math.Max(subpixelCount, graphicsOptions.AntialiasSubpixelDepth);
            }

            // Take the path inside the path builder, scan thing and generate a Buffer2d representing the glyph and cache it.
            Buffer2D<float> fullBuffer = this.MemoryAllocator.Allocate2D<float>(size.Width + 1, size.Height + 1, AllocationOptions.Clean);

            var scanner = PolygonScanner.Create(
                path,
                0,
                size.Height,
                subpixelCount,
                IntersectionRule.Nonzero,
                this.MemoryAllocator);

            try
            {
                while (scanner.MoveToNextPixelLine())
                {
                    Span<float> scanline = fullBuffer.DangerousGetRowSpan(scanner.PixelLineY);
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

            return fullBuffer;
        }

        public void EndText()
        {
            // ensure we have captured the last underline/strikeout path
            this.FinaliseDecoration(ref this.currentUnderline);
            this.FinaliseDecoration(ref this.currentStrikout);
        }

        public void LineTo(Vector2 point)
        {
            this.builder.AddLine(this.currentPoint, point);
            this.currentPoint = point;
        }

        public void MoveTo(Vector2 point)
        {
            this.builder.StartFigure();
            this.currentPoint = point;
        }

        public void QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point)
        {
            this.builder.AddQuadraticBezier(this.currentPoint, secondControlPoint, point);
            this.currentPoint = point;
        }

        private struct GlyphRenderData : IDisposable
        {
            // public Color? Color;
            public Buffer2D<float> FillMap;

            public Buffer2D<float> OutlineMap;

            public void Dispose()
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
}
