// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

namespace SixLabors.ImageSharp.Drawing.Processing;

internal enum DrawingOperationKind : byte
{
    Fill = 0,
    Draw = 1
}

internal struct DrawingOperation
{
    public DrawingOperationKind Kind { get; set; }

    public IPath Path { get; set; }

    public Point RenderLocation { get; set; }

    public RichTextGlyphRenderer.CacheKey GlyphKey { get; set; }

    public bool HasGlyphKey { get; set; }

    public IntersectionRule IntersectionRule { get; set; }

    public byte RenderPass { get; set; }

    public Brush? Brush { get; set; }

    public Pen? Pen { get; set; }

    public PixelAlphaCompositionMode PixelAlphaCompositionMode { get; set; }

    public PixelColorBlendingMode PixelColorBlendingMode { get; set; }
}
