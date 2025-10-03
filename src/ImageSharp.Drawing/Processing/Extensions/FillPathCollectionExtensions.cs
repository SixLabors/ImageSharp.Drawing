// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Shapes.Text;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow the filling of collections of polygon outlines.
/// </summary>
public static class FillPathCollectionExtensions
{
    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The graphics options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="paths">The collection of paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        IPathCollection paths)
    {
        foreach (IPath s in paths)
        {
            source.Fill(options, brush, s);
        }

        return source;
    }

    /// <summary>
    /// Flood fills the image in the shape of the provided glyphs with the specified brush and pen.
    /// For multi-layer glyphs, a heuristic is used to decide whether to fill or stroke each layer.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The graphics options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="paths">The collection of glyph paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        Pen pen,
        IReadOnlyList<GlyphPathCollection> paths)
        => source.Fill(options, brush, pen, paths, static (gp, layer) =>
        {
            if (layer.Kind == GlyphLayerKind.Decoration)
            {
                // Decorations (underlines, strikethroughs, etc) are always filled.
                return true;
            }

            if (layer.Kind == GlyphLayerKind.Glyph)
            {
                // Standard glyph layers are filled by default.
                return true;
            }

            // Default heuristic: stroke "background-like" layers (large coverage), fill others.
            // TODO: We should be using the area, not the bounds. Thin layers with large width/height
            // will be misclassified. e.g. shadows.
            RectangleF glyphBounds = gp.Bounds;
            RectangleF layerBounds = layer.Bounds;

            if (glyphBounds.Width <= 0 || glyphBounds.Height <= 0)
            {
                return true; // degenerate glyph, just fill
            }

            // Use each dimension independently to avoid misclassifying thin layers.
            float rx = layerBounds.Width / glyphBounds.Width;
            float ry = layerBounds.Height / glyphBounds.Height;

            // ≥50% coverage, stroke (don't fill). Otherwise, fill.
            return rx < 0.5F || ry < 0.5F;
        });

    /// <summary>
    /// Flood fills the image in the shape of the provided glyphs with the specified brush and pen.
    /// For multi-layer glyphs, a heuristic is used to decide whether to fill or stroke each layer.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The graphics options.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="paths">The collection of glyph paths.</param>
    /// <param name="shouldFillLayer">A function that decides whether to fill or stroke a given layer.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        Pen pen,
        IReadOnlyList<GlyphPathCollection> paths,
        Func<GlyphPathCollection, GlyphLayerInfo, bool> shouldFillLayer)
    {
        foreach (GlyphPathCollection gp in paths)
        {
            if (gp.LayerCount == 0)
            {
                continue;
            }

            if (gp.LayerCount == 1)
            {
                // Single-layer glyph: just fill with the supplied brush.
                source.Fill(options, brush, gp.Paths);
                continue;
            }

            // Multi-layer: decide per layer whether to fill or stroke.
            for (int i = 0; i < gp.Layers.Count; i++)
            {
                GlyphLayerInfo layer = gp.Layers[i];
                IPath path = gp.PathList[i];

                if (shouldFillLayer(gp, layer))
                {
                    IntersectionRule fillRule = layer.FillRule == FillRule.EvenOdd
                        ? IntersectionRule.EvenOdd
                        : IntersectionRule.NonZero;

                    // Respect the layer's fill rule if different to the drawing options.
                    source.Fill(options.CloneOrReturnForIntersectionRule(fillRule), brush, path);
                }
                else
                {
                    // Outline only to preserve interior detail.
                    source.Draw(options, pen, path);
                }
            }
        }

        return source;
    }

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified brush.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="paths">The collection of paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Brush brush,
        IPathCollection paths) =>
        source.Fill(source.GetDrawingOptions(), brush, paths);

    /// <summary>
    /// Flood fills the image in the shape of the provided glyphs with the specified brush and pen.
    /// For multi-layer glyphs, a heuristic is used to decide whether to fill or stroke each layer.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="brush">The brush.</param>
    /// <param name="pen">The pen.</param>
    /// <param name="paths">The collection of glyph paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Brush brush,
        Pen pen,
        IReadOnlyList<GlyphPathCollection> paths) =>
        source.Fill(source.GetDrawingOptions(), brush, pen, paths);

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified color.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="options">The options.</param>
    /// <param name="color">The color.</param>
    /// <param name="paths">The paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Color color,
        IPathCollection paths) =>
        source.Fill(options, new SolidBrush(color), paths);

    /// <summary>
    /// Flood fills the image in the shape of the provided polygon with the specified color.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="paths">The collection of paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Color color,
        IPathCollection paths) =>
        source.Fill(new SolidBrush(color), paths);

    /// <summary>
    /// Flood fills the image in the shape of the provided glyphs with the specified color.
    /// For multi-layer glyphs, a heuristic is used to decide whether to fill or stroke each layer.
    /// </summary>
    /// <param name="source">The source image processing context.</param>
    /// <param name="color">The color.</param>
    /// <param name="paths">The collection of glyph paths.</param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        Color color,
        IReadOnlyList<GlyphPathCollection> paths) =>
        source.Fill(new SolidBrush(color), new SolidPen(color), paths);
}
