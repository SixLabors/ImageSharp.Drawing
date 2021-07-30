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
using SixLabors.ImageSharp.Processing.Processors;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text
{
    /// <summary>
    /// Using the brush as a source of pixels colors blends the brush color with source.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    internal class DrawTextProcessor<TPixel> : ImageProcessor<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private CachingGlyphRenderer textRenderer;

        private readonly DrawTextProcessor definition;

        public DrawTextProcessor(Configuration configuration, DrawTextProcessor definition, Image<TPixel> source, Rectangle sourceRectangle)
            : base(configuration, source, sourceRectangle)
        {
            this.definition = definition;
        }

        private DrawingOptions Options => this.definition.Options;

        private Font Font => this.definition.Font;

        private PointF Location => this.definition.Location;

        private string Text => this.definition.Text;

        private IPen Pen => this.definition.Pen;

        private IBrush Brush => this.definition.Brush;

        protected override void BeforeImageApply()
        {
            base.BeforeImageApply();

            // do everything at the image level as we are delegating the processing down to other processors
            var style = new RendererOptions(this.Font, this.Options.TextOptions.DpiX, this.Options.TextOptions.DpiY, this.Location)
            {
                ApplyKerning = this.Options.TextOptions.ApplyKerning,
                TabWidth = this.Options.TextOptions.TabWidth,
                WrappingWidth = this.Options.TextOptions.WrapTextWidth,
                HorizontalAlignment = this.Options.TextOptions.HorizontalAlignment,
                VerticalAlignment = this.Options.TextOptions.VerticalAlignment,
                LineSpacing = this.Options.TextOptions.LineSpacing,
                FallbackFontFamilies = this.Options.TextOptions.FallbackFonts,
                ColorFontSupport = this.definition.Options.TextOptions.RenderColorFonts ? ColorFontSupport.MicrosoftColrFormat : ColorFontSupport.None,
            };

            this.textRenderer = new CachingGlyphRenderer(
                this.Configuration.MemoryAllocator,
                this.Text.Length,
                this.Pen,
                this.Brush != null,
                this.Options.Transform)
            {
                Options = this.Options
            };

            var renderer = new TextRenderer(this.textRenderer);
            renderer.RenderText(this.Text, style);
        }

        protected override void AfterImageApply()
        {
            base.AfterImageApply();
            this.textRenderer?.Dispose();
            this.textRenderer = null;
        }

        /// <inheritdoc/>
        protected override void OnFrameApply(ImageFrame<TPixel> source)
        {
            // this is a no-op as we have processes all as an image, we should be able to pass out of before email apply a skip frames outcome
            Draw(this.textRenderer.FillOperations, this.Brush);
            Draw(this.textRenderer.OutlineOperations, this.Pen?.StrokeFill);

            void Draw(List<DrawingOperation> operations, IBrush brush)
            {
                if (operations?.Count > 0)
                {
                    var brushes = new Dictionary<Color, BrushApplicator<TPixel>>();
                    foreach (DrawingOperation operation in operations)
                    {
                        if (operation.Color.HasValue)
                        {
                            if (!brushes.TryGetValue(operation.Color.Value, out _))
                            {
                                brushes[operation.Color.Value] = new SolidBrush(operation.Color.Value).CreateApplicator(
                                    this.Configuration,
                                    this.textRenderer.Options.GraphicsOptions,
                                    source,
                                    this.SourceRectangle);
                            }
                        }
                    }

                    using (BrushApplicator<TPixel> app = brush.CreateApplicator(this.Configuration, this.textRenderer.Options.GraphicsOptions, source, this.SourceRectangle))
                    {
                        foreach (DrawingOperation operation in operations)
                        {
                            var currentApp = app;
                            if (operation.Color != null)
                            {
                                brushes.TryGetValue(operation.Color.Value, out currentApp);
                            }

                            Buffer2D<float> buffer = operation.Map;
                            int startY = operation.Location.Y;
                            int startX = operation.Location.X;
                            int offsetSpan = 0;

                            if (startX + buffer.Height < 0)
                            {
                                continue;
                            }

                            if (startX + buffer.Width < 0)
                            {
                                continue;
                            }

                            if (startX < 0)
                            {
                                offsetSpan = -startX;
                                startX = 0;
                            }

                            if (startX >= source.Width)
                            {
                                continue;
                            }

                            int firstRow = 0;
                            if (startY < 0)
                            {
                                firstRow = -startY;
                            }

                            int maxHeight = source.Height - startY;
                            int end = Math.Min(operation.Map.Height, maxHeight);

                            for (int row = firstRow; row < end; row++)
                            {
                                int y = startY + row;
                                Span<float> span = buffer.GetRowSpan(row).Slice(offsetSpan);
                                currentApp.Apply(span, startX, y);
                            }
                        }
                    }

                    foreach (var app in brushes.Values)
                    {
                        app.Dispose();
                    }
                }
            }
        }

        private struct DrawingOperation
        {
            public Buffer2D<float> Map { get; set; }

            public Point Location { get; set; }

            public Color? Color { get; set; }
        }

        private class CachingGlyphRenderer : IColorGlyphRenderer, IDisposable
        {
            // just enough accuracy to allow for 1/8 pixel differences which
            // later are accumulated while rendering, but do not grow into full pixel offsets
            // The value 8 is benchmarked to:
            // - Provide a good accuracy (smaller than 0.2% image difference compared to the non-caching variant)
            // - Cache hit ratio above 60%
            private const float AccuracyMultiple = 8;
            private readonly Matrix3x2 transform;
            private readonly PathBuilder builder;

            private Point currentRenderPosition;
            private (GlyphRendererParameters glyph, PointF subPixelOffset) currentGlyphRenderParams;
            private readonly int offset;
            private PointF currentPoint;
            private Color? currentColor;

            private readonly Dictionary<(GlyphRendererParameters glyph, PointF subPixelOffset), GlyphRenderData>
                glyphData = new Dictionary<(GlyphRendererParameters glyph, PointF subPixelOffset), GlyphRenderData>();

            private readonly bool renderOutline;
            private readonly bool renderFill;
            private bool rasterizationRequired;

            public CachingGlyphRenderer(MemoryAllocator memoryAllocator, int size, IPen pen, bool renderFill, Matrix3x2 transform)
            {
                this.MemoryAllocator = memoryAllocator;
                this.currentRenderPosition = default;
                this.Pen = pen;
                this.renderFill = renderFill;
                this.renderOutline = pen != null;
                this.offset = 2;
                if (this.renderFill)
                {
                    this.FillOperations = new List<DrawingOperation>(size);
                }

                if (this.renderOutline)
                {
                    this.offset = (int)MathF.Ceiling((pen.StrokeWidth * 2) + 2);
                    this.OutlineOperations = new List<DrawingOperation>(size);
                }

                this.transform = transform;
                this.builder = new PathBuilder();
            }

            public List<DrawingOperation> FillOperations { get; }

            public List<DrawingOperation> OutlineOperations { get; }

            public MemoryAllocator MemoryAllocator { get; internal set; }

            public IPen Pen { get; internal set; }

            public DrawingOptions Options { get; internal set; }

            protected void SetLayerColor(Color color)
            {
                this.currentColor = color;
            }

            public void SetColor(GlyphColor color)
            {
                this.SetLayerColor(new Color(new Rgba32(color.Red, color.Green, color.Blue, color.Alpha)));
            }

            public void BeginFigure()
            {
                this.builder.StartFigure();
            }

            public bool BeginGlyph(FontRectangle bounds, GlyphRendererParameters parameters)
            {
                this.currentColor = null;
                var currentBounds = new RectangularPolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height)
                    .Transform(this.transform)
                    .Bounds;

                this.currentRenderPosition = Point.Truncate(currentBounds.Location);
                PointF subPixelOffset = currentBounds.Location - this.currentRenderPosition;

                subPixelOffset.X = MathF.Round(subPixelOffset.X * AccuracyMultiple) / AccuracyMultiple;
                subPixelOffset.Y = MathF.Round(subPixelOffset.Y * AccuracyMultiple) / AccuracyMultiple;

                // we have offset our rendering origin a little bit down to prevent edge cropping, move the draw origin up to compensate
                this.currentRenderPosition = new Point(this.currentRenderPosition.X - this.offset, this.currentRenderPosition.Y - this.offset);
                this.currentGlyphRenderParams = (parameters, subPixelOffset);

                if (this.glyphData.ContainsKey(this.currentGlyphRenderParams))
                {
                    // we have already drawn the glyph vectors skip trying again
                    this.rasterizationRequired = false;
                    return false;
                }

                // we check to see if we have a render cache and if we do then we render else
                this.builder.Clear();

                // ensure all glyphs render around [zero, zero]  so offset negative root positions so when we draw the glyph we can offset it back
                var origionTransform = new Vector2(-(int)currentBounds.X + this.offset, -(int)currentBounds.Y + this.offset);

                var transform = this.transform;
                transform.Translation = transform.Translation + origionTransform;
                this.builder.SetTransform(transform);

                this.rasterizationRequired = true;
                return true;
            }

            public void BeginText(FontRectangle bounds)
            {
                // not concerned about this one
                this.OutlineOperations?.Clear();
                this.FillOperations?.Clear();
            }

            public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point)
            {
                this.builder.AddBezier(this.currentPoint, secondControlPoint, thirdControlPoint, point);
                this.currentPoint = point;
            }

            public void Dispose()
            {
                foreach (KeyValuePair<(GlyphRendererParameters glyph, PointF subPixelOffset), GlyphRenderData> kv in this.glyphData)
                {
                    kv.Value.Dispose();
                }

                this.glyphData.Clear();
            }

            public void EndFigure()
            {
                this.builder.CloseFigure();
            }

            public void EndGlyph()
            {
                GlyphRenderData renderData = default;

                // has the glyph been rendered already?
                if (this.rasterizationRequired)
                {
                    IPath path = this.builder.Build();

                    if (path.Bounds.Equals(RectangleF.Empty))
                    {
                        return;
                    }

                    // if we are using the fonts color layers we ignore the request to draw an outline only
                    // cause that wont really work and instead force drawing with fill with the requested color
                    // if color fonts disabled then this.currentColor will always be null
                    if (this.renderFill || this.currentColor != null)
                    {
                        renderData.FillMap = this.Render(path);
                        renderData.Color = this.currentColor;
                    }

                    if (this.renderOutline && this.currentColor == null)
                    {
                        if (this.Pen.StrokePattern.Length == 0)
                        {
                            path = path.GenerateOutline(this.Pen.StrokeWidth);
                        }
                        else
                        {
                            path = path.GenerateOutline(this.Pen.StrokeWidth, this.Pen.StrokePattern);
                        }

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
                    this.FillOperations.Add(new DrawingOperation
                    {
                        Location = this.currentRenderPosition,
                        Map = renderData.FillMap,
                        Color = renderData.Color
                    });
                }

                if (renderData.OutlineMap != null)
                {
                    this.OutlineOperations.Add(new DrawingOperation
                    {
                        Location = this.currentRenderPosition,
                        Map = renderData.OutlineMap
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
                    xOffset = 0f; // we are antialiasing skip offsetting as real antialiasing should take care of offset.
                    subpixelCount = Math.Max(subpixelCount, graphicsOptions.AntialiasSubpixelDepth);
                }

                // take the path inside the path builder, scan thing and generate a Buffer2d representing the glyph and cache it.
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
                        Span<float> scanline = fullBuffer.GetRowSpan(scanner.PixelLineY);
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
                    // ref structs can't implement interfaces so technically PolygonScanner is not IDisposable
                    scanner.Dispose();
                }

                return fullBuffer;
            }

            public void EndText()
            {
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
                this.builder.AddBezier(this.currentPoint, secondControlPoint, point);
                this.currentPoint = point;
            }

            private struct GlyphRenderData : IDisposable
            {
                public Color? Color;

                public Buffer2D<float> FillMap;

                public Buffer2D<float> OutlineMap;

                public void Dispose()
                {
                    this.FillMap?.Dispose();
                    this.OutlineMap?.Dispose();
                }
            }
        }
    }
}
