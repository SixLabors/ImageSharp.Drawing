// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
    public GlyphLayerInfo(int startIndex, int count, Paint? paint, FillRule fillRule, RectangleF bounds, GlyphLayerKind kind = GlyphLayerKind.Glyph)
    {
        this.StartIndex = startIndex;
        this.Count = count;
        this.Paint = paint;
        this.FillRule = fillRule;
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
    public FillRule FillRule { get; }

    /// <summary>
    /// Gets the bounds of the layer geometry (device space).
    /// </summary>
    public RectangleF Bounds { get; }

    /// <summary>
    /// Gets the semantic kind of the layer (for policy decisions).
    /// </summary>
    public GlyphLayerKind Kind { get; }
}
