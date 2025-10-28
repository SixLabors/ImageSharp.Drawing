// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts.Rendering;

namespace SixLabors.ImageSharp.Drawing.Shapes.Text;

/// <summary>
/// Describes a single painted layer as a span within the glyph's path list.
/// </summary>
public readonly struct GlyphLayerInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphLayerInfo"/> struct.
    /// </summary>
    /// <param name="startIndex">Start index (inclusive) of the layer's paths within the glyph's path list.</param>
    /// <param name="count">Number of paths in this layer.</param>
    /// <param name="paint">The layer paint (null means use renderer default).</param>
    /// <param name="fillRule">The fill rule to use for this layer.</param>
    /// <param name="bounds">Axis-aligned bounds of the layer geometry.</param>
    /// <param name="kind">An optional semantic hint for the layer type.</param>
    internal GlyphLayerInfo(
        int startIndex,
        int count,
        Paint? paint,
        FillRule fillRule,
        RectangleF bounds,
        GlyphLayerKind kind)
    {
        this.StartIndex = startIndex;
        this.Count = count;
        this.Paint = paint;
        this.IntersectionRule = TextUtilities.MapFillRule(fillRule);

        CompositeMode compositeMode = paint?.CompositeMode ?? CompositeMode.SrcOver;
        this.PixelAlphaCompositionMode = TextUtilities.MapCompositionMode(compositeMode);
        this.PixelColorBlendingMode = TextUtilities.MapBlendingMode(compositeMode);
        this.Bounds = bounds;
        this.Kind = kind;
    }

    private GlyphLayerInfo(
        int startIndex,
        int count,
        Paint? paint,
        IntersectionRule intersectionRule,
        PixelAlphaCompositionMode compositionMode,
        PixelColorBlendingMode colorBlendingMode,
        RectangleF bounds,
        GlyphLayerKind kind)
    {
        this.StartIndex = startIndex;
        this.Count = count;
        this.Paint = paint;
        this.IntersectionRule = intersectionRule;
        this.PixelAlphaCompositionMode = compositionMode;
        this.PixelColorBlendingMode = colorBlendingMode;
        this.Bounds = bounds;
        this.Kind = kind;
    }

    /// <summary>
    /// Gets the start index (inclusive) of the layer span within the glyph's path list.
    /// </summary>
    public int StartIndex { get; }

    /// <summary>
    /// Gets the number of paths in this layer.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets the paint definition to use for this layer; may be <see langword="null"/>.
    /// </summary>
    public Paint? Paint { get; }

    /// <summary>
    /// Gets the fill rule for rasterization of this layer.
    /// </summary>
    public IntersectionRule IntersectionRule { get; }

    /// <summary>
    /// Gets the pixel alpha composition mode to use for this layer.
    /// </summary>
    public PixelAlphaCompositionMode PixelAlphaCompositionMode { get; }

    /// <summary>
    /// Gets the pixel color blending mode to use for this layer.
    /// </summary>
    public PixelColorBlendingMode PixelColorBlendingMode { get; }

    /// <summary>
    /// Gets the bounds of the layer geometry (device space).
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the semantic kind of the layer (for policy decisions).
    /// </summary>
    public GlyphLayerKind Kind { get; }

    internal static GlyphLayerInfo Transform(in GlyphLayerInfo info, Matrix3x2 matrix)
        => new(
            info.StartIndex,
            info.Count,
            info.Paint,
            info.IntersectionRule,
            info.PixelAlphaCompositionMode,
            info.PixelColorBlendingMode,
            RectangleF.Transform(info.Bounds, matrix),
            info.Kind);
}
