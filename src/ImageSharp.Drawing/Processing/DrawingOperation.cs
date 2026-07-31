// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Identifies how a text drawing operation paints its path.
/// </summary>
internal enum DrawingOperationKind : byte
{
    /// <summary>
    /// The path interior is filled using <see cref="DrawingOperation.Brush"/>.
    /// </summary>
    Fill = 0,

    /// <summary>
    /// The path is stroked using <see cref="DrawingOperation.Pen"/>.
    /// </summary>
    Draw = 1
}

/// <summary>
/// Represents one paint operation emitted by <see cref="RichTextGlyphRenderer"/> during text
/// rendering and later converted to composition commands by
/// <see cref="DrawingCanvas{TPixel}"/>.
/// </summary>
internal struct DrawingOperation
{
    /// <summary>
    /// Gets or sets a value identifying whether the operation fills or strokes <see cref="Path"/>.
    /// </summary>
    public DrawingOperationKind Kind { get; set; }

    /// <summary>
    /// Gets or sets the glyph or decoration path in local coordinates relative to <see cref="RenderLocation"/>.
    /// </summary>
    public IPath Path { get; set; }

    /// <summary>
    /// Gets or sets the pixel-clamped target location at which <see cref="Path"/> is rendered.
    /// </summary>
    public Point RenderLocation { get; set; }

    /// <summary>
    /// Gets or sets the fractional-pixel remainder of the target location, in the range [0, 1).
    /// Cached glyph paths are anchored at their exact outline origin so one cache entry serves
    /// every position; the backends apply this remainder as a residual translation, which reuses
    /// the scale-keyed flattened geometry while rendering the glyph at its exact sub-pixel
    /// position. Zero for uncached operations whose paths bake their own fraction.
    /// </summary>
    public Vector2 SubPixelOffset { get; set; }

    /// <summary>
    /// Gets or sets the cache key identifying the glyph path in the text cache.
    /// </summary>
    public RichTextGlyphRenderer.CacheKey GlyphKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="GlyphKey"/> is valid. This is
    /// <see langword="false"/> for uncached operations such as path-following text and decorations.
    /// </summary>
    public bool HasGlyphKey { get; set; }

    /// <summary>
    /// Gets or sets the fill rule used to resolve the interior of <see cref="Path"/>.
    /// </summary>
    public IntersectionRule IntersectionRule { get; set; }

    /// <summary>
    /// Gets or sets the render pass used to order operations: fills paint beneath outlines,
    /// and outlines beneath decorations. Emission order is preserved within each pass.
    /// </summary>
    public byte RenderPass { get; set; }

    /// <summary>
    /// Gets or sets the brush used by <see cref="DrawingOperationKind.Fill"/> operations.
    /// </summary>
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the pen used by <see cref="DrawingOperationKind.Draw"/> operations.
    /// </summary>
    public Pen? Pen { get; set; }

    /// <summary>
    /// Gets or sets the alpha composition mode captured from the active text run or graphics options.
    /// </summary>
    public PixelAlphaCompositionMode PixelAlphaCompositionMode { get; set; }

    /// <summary>
    /// Gets or sets the color blending mode captured from the active text run or graphics options.
    /// </summary>
    public PixelColorBlendingMode PixelColorBlendingMode { get; set; }
}
