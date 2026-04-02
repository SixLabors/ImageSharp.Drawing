// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Text;
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
    /// Saves the current drawing state and begins an isolated compositing layer.
    /// Subsequent draw commands are recorded into an isolated logical layer. When
    /// <see cref="Restore"/> closes the layer, that layer becomes eligible for
    /// composition during the next <see cref="Flush"/> or <see cref="System.IDisposable.Dispose"/>.
    /// </summary>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public int SaveLayer();

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer.
    /// Subsequent draw commands are recorded into an isolated logical layer. When
    /// <see cref="Restore"/> closes the layer, that layer is composed during the next
    /// <see cref="Flush"/> or <see cref="System.IDisposable.Dispose"/> using the specified
    /// <paramref name="layerOptions"/> (blend mode, alpha composition, opacity).
    /// </summary>
    /// <param name="layerOptions">
    /// Graphics options controlling how the layer is composited on restore.
    /// </param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public int SaveLayer(GraphicsOptions layerOptions);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer
    /// bounded to a subregion. Subsequent draw commands are recorded into that isolated
    /// logical layer. When <see cref="Restore"/> closes the layer, it is composed during
    /// the next <see cref="Flush"/> or <see cref="System.IDisposable.Dispose"/> using the specified
    /// <paramref name="layerOptions"/>.
    /// </summary>
    /// <param name="layerOptions">
    /// Graphics options controlling how the layer is composited on restore.
    /// </param>
    /// <param name="bounds">
    /// The local bounds of the layer. Only this region is allocated and composited.
    /// </param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds);

    /// <summary>
    /// Restores the most recently saved state.
    /// </summary>
    /// <remarks>
    /// If the most recently saved state was created by a <c>SaveLayer</c> overload,
    /// the layer is closed in the deferred scene. Actual composition happens during the
    /// next <see cref="Flush"/> or <see cref="System.IDisposable.Dispose"/>.
    /// </remarks>
    public void Restore();

    /// <summary>
    /// Restores to a specific save count.
    /// </summary>
    /// <remarks>
    /// State frames above <paramref name="saveCount"/> are discarded,
    /// and the last discarded frame becomes the current state.
    /// If any discarded state was created by a <c>SaveLayer</c> overload,
    /// those layers are closed in the deferred scene and are composed during the next
    /// <see cref="Flush"/> or <see cref="System.IDisposable.Dispose"/>.
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
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    /// <param name="region">Region to clear in local coordinates.</param>
    public void Clear(Brush brush, Rectangle region);

    /// <summary>
    /// Clears a path region using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    /// <param name="path">The path region to clear.</param>
    public void Clear(Brush brush, IPath path);

    /// <summary>
    /// Fills the whole canvas using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels.</param>
    public void Fill(Brush brush);

    /// <summary>
    /// Fills a path in local coordinates using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="path">The path to fill.</param>
    public void Fill(Brush brush, IPath path);

    /// <summary>
    /// Applies an image-processing operation to a local region.
    /// </summary>
    /// <param name="region">The local region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    public void Process(Rectangle region, Action<IImageProcessingContext> operation);

    /// <summary>
    /// Applies an image-processing operation to a region described by a path builder.
    /// </summary>
    /// <param name="pathBuilder">The path builder describing the region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    public void Process(PathBuilder pathBuilder, Action<IImageProcessingContext> operation);

    /// <summary>
    /// Applies an image-processing operation to a path region.
    /// </summary>
    /// <remarks>
    /// The operation is constrained to the path bounds and then composited back using an image brush.
    /// </remarks>
    /// <param name="path">The path region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    public void Process(IPath path, Action<IImageProcessingContext> operation);

    /// <summary>
    /// Draws a polyline outline using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate the line outline.</param>
    /// <param name="points">Polyline points.</param>
    public void DrawLine(Pen pen, params PointF[] points);

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
        ReadOnlySpan<char> text,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws layered glyph geometry using a monochrome projection.
    /// </summary>
    /// <remarks>
    /// For painted glyph layers, the implementation uses a coverage/compactness heuristic
    /// to keep one dominant background-like layer as outline-only to preserve interior definition.
    /// All non-painted layers are filled.
    /// </remarks>
    /// <param name="brush">Brush used to fill glyph layers.</param>
    /// <param name="pen">Pen used to outline dominant painted layers.</param>
    /// <param name="glyphs">Layered glyph geometry to draw.</param>
    public void DrawGlyphs(
        Brush brush,
        Pen pen,
        IReadOnlyList<GlyphPathCollection> glyphs);

    /// <summary>
    /// Measures the logical advance of the text in pixel units.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The logical advance rectangle of the text if it was to be rendered.</returns>
    /// <remarks>
    /// This measurement reflects line-box height and horizontal or vertical text advance from the layout model.
    /// It does not guarantee that all rendered glyph pixels fit within the returned rectangle.
    /// Use <see cref="MeasureTextBounds"/> for glyph ink bounds or
    /// <see cref="MeasureTextRenderableBounds"/> for the union of logical advance and rendered bounds.
    /// </remarks>
    public RectangleF MeasureTextAdvance(RichTextOptions textOptions, ReadOnlySpan<char> text);

    /// <summary>
    /// Measures the rendered glyph bounds of the text in pixel units.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The rendered glyph bounds of the text if it was to be rendered.</returns>
    /// <remarks>
    /// This measures the tight ink bounds enclosing all rendered glyphs. The returned rectangle
    /// may be smaller or larger than the logical advance and may have a non-zero origin.
    /// Use <see cref="MeasureTextAdvance"/> for the logical layout box or
    /// <see cref="MeasureTextRenderableBounds"/> for the union of both.
    /// </remarks>
    public RectangleF MeasureTextBounds(RichTextOptions textOptions, ReadOnlySpan<char> text);

    /// <summary>
    /// Measures the full renderable bounds of the text in pixel units.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>
    /// The union of the logical advance rectangle and the rendered glyph bounds if the text was to be rendered.
    /// </returns>
    /// <remarks>
    /// The returned rectangle is in absolute coordinates and is large enough to contain both the logical advance
    /// rectangle and the rendered glyph bounds.
    /// Use this method when both typographic advance and rendered glyph overshoot must fit within the same rectangle.
    /// </remarks>
    public RectangleF MeasureTextRenderableBounds(RichTextOptions textOptions, ReadOnlySpan<char> text);

    /// <summary>
    /// Measures the normalized rendered size of the text in pixel units.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The rendered size of the text with the origin normalized to <c>(0, 0)</c>.</returns>
    /// <remarks>
    /// This is equivalent to measuring the rendered bounds and returning only the width and height.
    /// Use <see cref="MeasureTextBounds"/> when the returned X and Y offset are also required.
    /// </remarks>
    public RectangleF MeasureTextSize(RichTextOptions textOptions, ReadOnlySpan<char> text);

    /// <summary>
    /// Measures the logical advance of each laid-out character entry in pixel units.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="advances">The list of per-entry logical advances of the text if it was to be rendered.</param>
    /// <returns>Whether any of the entries had non-empty advances.</returns>
    /// <remarks>
    /// Each entry reflects the typographic advance width and height for one character.
    /// Use <see cref="TryMeasureCharacterBounds"/> for per-character ink bounds or
    /// <see cref="TryMeasureCharacterRenderableBounds"/> for the union of both.
    /// </remarks>
    public bool TryMeasureCharacterAdvances(RichTextOptions textOptions, ReadOnlySpan<char> text, out ReadOnlySpan<GlyphBounds> advances);

    /// <summary>
    /// Measures the rendered glyph bounds of each laid-out character entry in pixel units.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="bounds">The list of per-entry rendered glyph bounds of the text if it was to be rendered.</param>
    /// <returns>Whether any of the entries had non-empty bounds.</returns>
    /// <remarks>
    /// Each entry reflects the tight ink bounds of one rendered glyph.
    /// Use <see cref="TryMeasureCharacterAdvances"/> for per-character logical advances or
    /// <see cref="TryMeasureCharacterRenderableBounds"/> for the union of both.
    /// </remarks>
    public bool TryMeasureCharacterBounds(RichTextOptions textOptions, ReadOnlySpan<char> text, out ReadOnlySpan<GlyphBounds> bounds);

    /// <summary>
    /// Measures the full renderable bounds of each laid-out character entry in pixel units.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="bounds">The list of per-entry renderable bounds of the text if it was to be rendered.</param>
    /// <returns>Whether any of the entries had non-empty bounds.</returns>
    /// <remarks>
    /// Each returned rectangle is in absolute coordinates and is large enough to contain both the logical advance
    /// rectangle and the rendered glyph bounds for the corresponding laid-out entry.
    /// Use this when both typographic advance and rendered glyph overshoot must fit within the same rectangle.
    /// </remarks>
    public bool TryMeasureCharacterRenderableBounds(RichTextOptions textOptions, ReadOnlySpan<char> text, out ReadOnlySpan<GlyphBounds> bounds);

    /// <summary>
    /// Measures the normalized rendered size of each laid-out character entry in pixel units.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <param name="sizes">The list of per-entry rendered sizes with the origin normalized to <c>(0, 0)</c>.</param>
    /// <returns>Whether any of the entries had non-empty dimensions.</returns>
    /// <remarks>
    /// This is equivalent to measuring per-character bounds and returning only the width and height.
    /// Use <see cref="TryMeasureCharacterBounds"/> when the returned X and Y offset are also required.
    /// </remarks>
    public bool TryMeasureCharacterSizes(RichTextOptions textOptions, ReadOnlySpan<char> text, out ReadOnlySpan<GlyphBounds> sizes);

    /// <summary>
    /// Gets the number of laid-out lines contained within the text.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>The laid-out line count.</returns>
    public int CountTextLines(RichTextOptions textOptions, ReadOnlySpan<char> text);

    /// <summary>
    /// Gets per-line layout metrics for the supplied text.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>
    /// An array of <see cref="LineMetrics"/> in pixel units, one entry per laid-out line.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The returned <see cref="LineMetrics.Start"/> and <see cref="LineMetrics.Extent"/> are expressed
    /// in the primary flow direction for the active layout mode.
    /// </para>
    /// <para>
    /// <see cref="LineMetrics.Ascender"/>, <see cref="LineMetrics.Baseline"/>, and <see cref="LineMetrics.Descender"/>
    /// are line-box positions relative to the current line origin and are suitable for drawing guide lines.
    /// </para>
    /// <list type="bullet">
    /// <item><description>Horizontal layouts: Start = X position, Extent = width.</description></item>
    /// <item><description>Vertical layouts: Start = Y position, Extent = height.</description></item>
    /// </list>
    /// </remarks>
    public LineMetrics[] GetTextLineMetrics(RichTextOptions textOptions, ReadOnlySpan<char> text);

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
