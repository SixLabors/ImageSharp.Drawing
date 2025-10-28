// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
        => source.Fill(options, brush, pen, paths, static (gp, layer, path) =>
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
            // Use the bounding box area as an approximation of the glyph area as it is cheaper to compute.
            float glyphArea = gp.Bounds.Width * gp.Bounds.Height;
            float layerArea = path.ComputeArea();

            if (layerArea <= 0 || glyphArea <= 0)
            {
                return false; // degenerate glyph, don't fill
            }

            float coverage = layerArea / glyphArea;

            // <50% coverage, fill. Otherwise, stroke.
            return coverage < 0.50F;
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
    /// <param name="shouldFillLayer">
    /// A function that decides whether to fill or stroke a given layer within a multi-layer (painted) glyph.
    /// </param>
    /// <returns>The <see cref="IImageProcessingContext"/> to allow chaining of operations.</returns>
    public static IImageProcessingContext Fill(
        this IImageProcessingContext source,
        DrawingOptions options,
        Brush brush,
        Pen pen,
        IReadOnlyList<GlyphPathCollection> paths,
        Func<GlyphPathCollection, GlyphLayerInfo, IPath, bool> shouldFillLayer)
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

                if (shouldFillLayer(gp, layer, path))
                {
                    // Respect the layer's fill rule if different to the drawing options.
                    DrawingOptions o = options.CloneOrReturnForRules(
                        layer.IntersectionRule,
                        layer.PixelAlphaCompositionMode,
                        layer.PixelColorBlendingMode);

                    source.Fill(o, brush, path);
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
