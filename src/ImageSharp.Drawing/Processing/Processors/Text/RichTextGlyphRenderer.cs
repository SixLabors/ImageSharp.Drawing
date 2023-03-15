// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Processors.Text
{
    // TODO: Add caching.
    internal sealed class RichTextGlyphRenderer : BaseGlyphBuilder, IColorGlyphRenderer, IDisposable
    {
        private const byte RenderOrderFill = 0;
        private const byte RenderOrderOutline = 1;
        private const byte RenderOrderDecoration = 2;

        private readonly TextDrawingOptions textOptions;
        private readonly DrawingOptions drawingOptions;
        private readonly MemoryAllocator memoryAllocator;
        private readonly Pen defaultPen;
        private readonly Brush defaultBrush;
        private readonly IPathInternals path;
        private Vector2 textPathOffset;
        private bool isDisposed;

        private readonly Dictionary<Color, Brush> brushLookup = new();
        private TextRun currentTextRun;
        private Brush currentBrush;
        private Pen currentPen;
        private Color? currentColor;
        private TextDecorationDetails? currentUnderline;
        private TextDecorationDetails? currentStrikout;
        private TextDecorationDetails? currentOverline;

        public RichTextGlyphRenderer(
            TextDrawingOptions textOptions,
            DrawingOptions drawingOptions,
            MemoryAllocator memoryAllocator,
            Pen pen,
            Brush brush)
        {
            this.textOptions = textOptions;
            this.drawingOptions = drawingOptions;
            this.memoryAllocator = memoryAllocator;
            this.defaultPen = pen;
            this.defaultBrush = brush;
            this.DrawingOperations = new List<DrawingOperation>();

            // Set the default transform.
            this.Builder.SetTransform(drawingOptions.Transform);

            IPath path = textOptions.Path;
            if (path is not null)
            {
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

            float yOffset = this.textOptions.VerticalAlignment switch
            {
                VerticalAlignment.Center => bounds.Bottom - (bounds.Height * .5F),
                VerticalAlignment.Bottom => bounds.Bottom,
                VerticalAlignment.Top => bounds.Top,
                _ => bounds.Top,
            };

            float xOffset = this.textOptions.HorizontalAlignment switch
            {
                HorizontalAlignment.Center => bounds.Right - (bounds.Width * .5F),
                HorizontalAlignment.Right => bounds.Right,
                HorizontalAlignment.Left => bounds.Left,
                _ => bounds.Left,
            };
            this.textPathOffset = new(xOffset, yOffset);
        }

        /// <inheritdoc/>
        protected override void BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters)
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

            this.TransformGlyph(in bounds);
        }

        /// <inheritdoc/>
        public void SetColor(GlyphColor color)
            => this.currentColor = new Color(new Rgba32(color.Red, color.Green, color.Blue, color.Alpha));

        public override TextDecorations EnabledDecorations()
        {
            TextRun run = this.currentTextRun;
            TextDecorations decorations = run.TextDecorations;

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

        public override void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
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

            pen ??= new SolidPen(this.currentBrush ?? this.defaultBrush);
            this.AppendDecoration(ref targetDecoration, tl + offsetNew, tr + offsetNew, pen, newThickness);
        }

        protected override void EndGlyph()
        {
            GlyphRenderData renderData = default;

            // fix up the text runs colors
            // only if both brush and pen is null do we fallback to the defualt value
            if (this.currentBrush == null && this.currentPen == null)
            {
                this.currentBrush = this.defaultBrush;
                this.currentPen = this.defaultPen;
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

            // Path has already been added to the collection via the base class.
            IPath path = this.Paths.Last();
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

            if (renderData.FillMap != null)
            {
                this.DrawingOperations.Add(new DrawingOperation
                {
                    Location = Point.Truncate(path.Bounds.Location),
                    Map = renderData.FillMap,
                    Brush = this.currentBrush,
                    RenderPass = RenderOrderFill
                });
            }

            if (renderData.OutlineMap != null)
            {
                this.DrawingOperations.Add(new DrawingOperation
                {
                    Location = Point.Truncate(path.Bounds.Location),
                    Map = renderData.OutlineMap,
                    Brush = this.currentPen?.StrokeFill ?? this.currentBrush,
                    RenderPass = RenderOrderOutline
                });
            }
        }

        protected override void EndText()
        {
            // Ensure we have captured the last overline/underline/strikeout path
            this.FinalizeDecoration(ref this.currentOverline);
            this.FinalizeDecoration(ref this.currentUnderline);
            this.FinalizeDecoration(ref this.currentStrikout);
        }

        public void Dispose() => this.Dispose(disposing: true);

        private void FinalizeDecoration(ref TextDecorationDetails? decoration)
        {
            if (decoration != null)
            {
                // TODO: If the path is curved a line segment does not work well.
                // What would be great would be if we could take a slice of a path given an offset and length.
                IPath path = new Path(new LinearLineSegment(decoration.Value.Start, decoration.Value.End));
                IPath outline = decoration.Value.Pen.GeneratePath(path, decoration.Value.Thickness);

                // Calculate the transform for this path.
                // We cannot use the pathbuilder transform as this path is rendered independently.
                FontRectangle rectangle = new(outline.Bounds.Location, new(outline.Bounds.Width, outline.Bounds.Height));
                Matrix3x2 pathTransform = this.ComputeTransform(in rectangle);
                Matrix3x2 defaultTransform = this.drawingOptions.Transform;
                outline = outline.Transform(pathTransform * defaultTransform);

                if (outline.Bounds.Width != 0 && outline.Bounds.Height != 0)
                {
                    // Render the Path here
                    this.DrawingOperations.Add(new DrawingOperation
                    {
                        Brush = decoration.Value.Pen.StrokeFill,
                        Location = Point.Truncate(outline.Bounds.Location),
                        Map = this.Render(outline),
                        RenderPass = RenderOrderDecoration
                    });
                }

                decoration = null;
            }
        }

        private void AppendDecoration(ref TextDecorationDetails? decoration, Vector2 start, Vector2 end, Pen pen, float thickness)
        {
            if (decoration != null)
            {
                // TODO: This only works well if we are not trying to follow a path.
                if (this.path is not null)
                {
                    // Let's try and expand it first.
                    if (thickness == decoration.Value.Thickness
                        && decoration.Value.End.Y == start.Y
                        && (decoration.Value.End.X + 1) >= start.X
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
                Thickness = MathF.Abs(thickness)
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void TransformGlyph(in FontRectangle bounds)
        {
            if (this.path is null)
            {
                return;
            }

            this.Builder.SetTransform(this.ComputeTransform(in bounds));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Matrix3x2 ComputeTransform(in FontRectangle bounds)
        {
            if (this.path is null)
            {
                return Matrix3x2.Identity;
            }

            // Find the intersection point.
            // This should be offset to ensure we rotate at the bottom-center of the glyph.
            float halfWidth = bounds.Width * .5F;

            // Find the point of this intersection along the given path.
            SegmentInfo pathPoint = this.path.PointAlongPath(bounds.Left + halfWidth);

            // Now offset our target point since we're aligning the bottom-left location of our glyph against the path.
            // This is good and accurate when we are vertically aligned to the path however the distance between
            // characters in multiline text scales with the angle and vertical offset.
            // This is expected and consistant with other libraries.
            // Multiple line text should be rendered using multiple paths to avoid this behavior.
            Vector2 targetPoint = (Vector2)pathPoint.Point + new Vector2(-halfWidth, bounds.Top) - bounds.Location - this.textPathOffset;

            // Due to how matrix combining works you have to combine this in the reverse order of operation.
            return Matrix3x2.CreateTranslation(targetPoint)
                * Matrix3x2.CreateRotation(pathPoint.Angle - MathF.PI, pathPoint.Point);
        }

        private Buffer2D<float> Render(IPath path)
        {
            // We need to offset the path now against 0,0 for rasterization.
            IPath offsetPath = path.Translate(-path.Bounds.Location);
            Size size = Rectangle.Ceiling(offsetPath.Bounds).Size;
            int subpixelCount = FillPathProcessor.MinimumSubpixelCount;
            float xOffset = .5F;
            GraphicsOptions graphicsOptions = this.drawingOptions.GraphicsOptions;
            if (graphicsOptions.Antialias)
            {
                xOffset = 0F; // We are antialiasing skip offsetting as real antialiasing should take care of offset.
                subpixelCount = Math.Max(subpixelCount, graphicsOptions.AntialiasSubpixelDepth);
            }

            // Take the path inside the path builder, scan thing and generate a Buffer2D representing the glyph.
            Buffer2D<float> buffer = this.memoryAllocator.Allocate2D<float>(size.Width, size.Height, AllocationOptions.Clean);

            var scanner = PolygonScanner.Create(
                offsetPath,
                0,
                size.Height,
                subpixelCount,
                IntersectionRule.Nonzero,
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
                    foreach (DrawingOperation operation in this.DrawingOperations)
                    {
                        operation.Map.Dispose();
                    }
                }

                this.isDisposed = true;
            }
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
