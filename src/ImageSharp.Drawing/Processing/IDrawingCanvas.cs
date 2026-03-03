// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a drawing canvas over a frame target.
/// </summary>
public interface IDrawingCanvas : IDisposable
{
    /// <summary>
    /// Gets the local bounds of this canvas.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <summary>
    /// Gets the number of saved states currently on the canvas stack.
    /// </summary>
    public int SaveCount { get; }

    /// <summary>
    /// Saves the current drawing state on the state stack.
    /// </summary>
    /// <remarks>
    /// This operation stores the current canvas state by reference.
    /// If the same <see cref="DrawingOptions"/> instance is mutated after
    /// <see cref="Save()"/>, those mutations are visible when restoring.
    /// </remarks>
    /// <returns>The save count after the state has been pushed.</returns>
    public int Save();

    /// <summary>
    /// Saves the current drawing state and replaces the active state with the provided options and clip paths.
    /// </summary>
    /// <remarks>
    /// The provided <paramref name="options"/> instance is stored by reference.
    /// Mutating it after this call mutates the active/restored state behavior.
    /// </remarks>
    /// <param name="options">Drawing options for the new active state.</param>
    /// <param name="clipPaths">Clip paths for the new active state.</param>
    /// <returns>The save count after the previous state has been pushed.</returns>
    public int Save(DrawingOptions options, params IPath[] clipPaths);

    /// <summary>
    /// Restores the most recently saved state.
    /// </summary>
    public void Restore();

    /// <summary>
    /// Restores to a specific save count.
    /// </summary>
    /// <remarks>
    /// State frames above <paramref name="saveCount"/> are discarded,
    /// and the last discarded frame becomes the current state.
    /// </remarks>
    /// <param name="saveCount">The save count to restore to.</param>
    public void RestoreTo(int saveCount);

    /// <summary>
    /// Creates a child canvas over a subregion in local coordinates.
    /// </summary>
    /// <param name="region">The child region in local coordinates.</param>
    /// <returns>A child canvas with local origin at (0,0).</returns>
    public IDrawingCanvas CreateRegion(Rectangle region);

    /// <summary>
    /// Clears the whole canvas using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    public void Clear(Brush brush);

    /// <summary>
    /// Clears a local region using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="region">Region to clear in local coordinates.</param>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    public void Clear(Rectangle region, Brush brush);

    /// <summary>
    /// Clears a path region using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="path">The path region to clear.</param>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    public void Clear(IPath path, Brush brush);

    /// <summary>
    /// Fills the whole canvas using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    public void Fill(Brush brush);

    /// <summary>
    /// Fills a local region using the given brush.
    /// </summary>
    /// <param name="region">Region to fill in local coordinates.</param>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    public void Fill(Rectangle region, Brush brush);

    /// <summary>
    /// Fills all paths in a collection using the given brush and drawing options.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="paths">Path collection to fill.</param>
    public void Fill(Brush brush, IPathCollection paths);

    /// <summary>
    /// Fills a path built by the provided builder using the given brush.
    /// </summary>
    /// <param name="pathBuilder">The path builder describing the fill region.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    public void Fill(PathBuilder pathBuilder, Brush brush);

    /// <summary>
    /// Fills a path in local coordinates using the given brush.
    /// </summary>
    /// <param name="path">The path to fill.</param>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    public void Fill(IPath path, Brush brush);

    /// <summary>
    /// Draws an arc outline using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate the arc outline.</param>
    /// <param name="center">Arc center point in local coordinates.</param>
    /// <param name="radius">Arc radii in local coordinates.</param>
    /// <param name="rotation">Ellipse rotation in degrees.</param>
    /// <param name="startAngle">Arc start angle in degrees.</param>
    /// <param name="sweepAngle">Arc sweep angle in degrees.</param>
    public void DrawArc(Pen pen, PointF center, SizeF radius, float rotation, float startAngle, float sweepAngle);

    /// <summary>
    /// Draws a cubic bezier outline using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate the bezier outline.</param>
    /// <param name="points">Bezier control points.</param>
    public void DrawBezier(Pen pen, params PointF[] points);

    /// <summary>
    /// Draws an ellipse outline using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate the ellipse outline.</param>
    /// <param name="center">Ellipse center point in local coordinates.</param>
    /// <param name="size">Ellipse width and height in local coordinates.</param>
    public void DrawEllipse(Pen pen, PointF center, SizeF size);

    /// <summary>
    /// Draws a polyline outline using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate the line outline.</param>
    /// <param name="points">Polyline points.</param>
    public void DrawLine(Pen pen, params PointF[] points);

    /// <summary>
    /// Draws a rectangular outline using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate the rectangle outline.</param>
    /// <param name="region">Rectangle region to stroke.</param>
    public void Draw(Pen pen, Rectangle region);

    /// <summary>
    /// Draws all paths in a collection using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate outlines.</param>
    /// <param name="paths">Path collection to stroke.</param>
    public void Draw(Pen pen, IPathCollection paths);

    /// <summary>
    /// Draws a path outline built by the provided builder using the given pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the outline fill path.</param>
    /// <param name="pathBuilder">The path builder describing the path to stroke.</param>
    public void Draw(Pen pen, PathBuilder pathBuilder);

    /// <summary>
    /// Draws a path outline in local coordinates using the given pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the outline fill path.</param>
    /// <param name="path">The path to stroke.</param>
    public void Draw(Pen pen, IPath path);

    /// <summary>
    /// Draws text onto this canvas.
    /// </summary>
    /// <param name="textOptions">The text rendering options.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    public void DrawText(
        RichTextOptions textOptions,
        string text,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Measures the advance box of the specified text.
    /// </summary>
    /// <param name="textOptions">Text layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The measured advance as a rectangle in px units.</returns>
    public RectangleF MeasureTextAdvance(RichTextOptions textOptions, string text);

    /// <summary>
    /// Measures the tight bounds of the specified text.
    /// </summary>
    /// <param name="textOptions">Text layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The measured bounds rectangle in px units.</returns>
    public RectangleF MeasureTextBounds(RichTextOptions textOptions, string text);

    /// <summary>
    /// Measures the size of the specified text.
    /// </summary>
    /// <param name="textOptions">Text layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The measured size as a rectangle in px units.</returns>
    public RectangleF MeasureTextSize(RichTextOptions textOptions, string text);

    /// <summary>
    /// Tries to measure per-character advances for the specified text.
    /// </summary>
    /// <param name="textOptions">Text layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="advances">Receives per-character advance metrics in px units.</param>
    /// <returns><see langword="true"/> if all character advances were measured; otherwise <see langword="false"/>.</returns>
    public bool TryMeasureCharacterAdvances(RichTextOptions textOptions, string text, out ReadOnlySpan<GlyphBounds> advances);

    /// <summary>
    /// Tries to measure per-character bounds for the specified text.
    /// </summary>
    /// <param name="textOptions">Text layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="bounds">Receives per-character bounds in px units.</param>
    /// <returns><see langword="true"/> if all character bounds were measured; otherwise <see langword="false"/>.</returns>
    public bool TryMeasureCharacterBounds(RichTextOptions textOptions, string text, out ReadOnlySpan<GlyphBounds> bounds);

    /// <summary>
    /// Tries to measure per-character sizes for the specified text.
    /// </summary>
    /// <param name="textOptions">Text layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="sizes">Receives per-character sizes in px units.</param>
    /// <returns><see langword="true"/> if all character sizes were measured; otherwise <see langword="false"/>.</returns>
    public bool TryMeasureCharacterSizes(RichTextOptions textOptions, string text, out ReadOnlySpan<GlyphBounds> sizes);

    /// <summary>
    /// Counts the rendered text lines for the specified text.
    /// </summary>
    /// <param name="textOptions">Text layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The number of rendered lines.</returns>
    public int CountTextLines(RichTextOptions textOptions, string text);

    /// <summary>
    /// Gets line metrics for the specified text.
    /// </summary>
    /// <param name="textOptions">Text layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>An array of line metrics in px units.</returns>
    public LineMetrics[] GetTextLineMetrics(RichTextOptions textOptions, string text);

    /// <summary>
    /// Draws an image source region into a destination rectangle.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="sourceRect">The source rectangle within <paramref name="image"/>.</param>
    /// <param name="destinationRect">The destination rectangle in local canvas coordinates.</param>
    /// <param name="sampler">
    /// Optional resampler used when scaling or transforming the image. Defaults to <see cref="KnownResamplers.Bicubic"/>.
    /// </param>
    public void DrawImage(
        Image image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler = null);

    /// <summary>
    /// Flushes queued drawing commands to the target in submission order.
    /// </summary>
    public void Flush();
}
