// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a drawing canvas over a frame target.
/// </summary>
public abstract partial class DrawingCanvas : IDisposable
{
    /// <summary>
    /// Gets the local bounds of this canvas.
    /// </summary>
    public abstract Rectangle Bounds { get; }

    /// <summary>
    /// Gets the number of saved states currently on the canvas stack.
    /// </summary>
    public abstract int SaveCount { get; }

    /// <summary>
    /// Saves the current drawing state on the state stack.
    /// </summary>
    /// <remarks>
    /// This operation stores the current canvas state by reference.
    /// If the same <see cref="DrawingOptions"/> instance is mutated after
    /// <see cref="Save()"/>, those mutations are visible when restoring.
    /// </remarks>
    /// <returns>The save count after the state has been pushed.</returns>
    public abstract int Save();

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
    public abstract int Save(DrawingOptions options, params IPath[] clipPaths);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer
    /// bounded to a subregion. Subsequent draw commands are recorded into that isolated
    /// logical layer. When <see cref="Restore"/> closes the layer, it is recorded into the
    /// canvas timeline and later composed during <see cref="IDisposable.Dispose"/> using the specified
    /// <paramref name="layerOptions"/>.
    /// </summary>
    /// <remarks>
    /// The layer bounds are expressed in the current local coordinate system and are
    /// transformed with the active drawing transform when the layer is created. They
    /// limit allocation and compositing only; they do not change the canvas coordinate
    /// system used by commands recorded inside the layer.
    /// </remarks>
    /// <param name="layerOptions">
    /// Graphics options controlling how the closed layer is composited against the parent canvas
    /// when the canvas timeline is rendered during <see cref="IDisposable.Dispose"/>.
    /// </param>
    /// <param name="bounds">
    /// The local bounds of the layer. Only this region is allocated and composited.
    /// </param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer
    /// using the supplied drawing options and clip paths for commands recorded into the layer.
    /// </summary>
    /// <param name="layerOptions">
    /// Graphics options controlling how the closed layer is composited against the parent canvas
    /// when the canvas timeline is rendered during <see cref="IDisposable.Dispose"/>.
    /// </param>
    /// <param name="bounds">
    /// The local bounds of the layer. Only this region is allocated and composited.
    /// </param>
    /// <param name="options">Drawing options for the layer contents.</param>
    /// <param name="clipPaths">Clip paths for the layer contents.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds, DrawingOptions options, params IPath[] clipPaths);

    /// <summary>
    /// Restores the most recently saved state.
    /// </summary>
    /// <remarks>
    /// If the most recently saved state was created by a <c>SaveLayer</c> overload,
    /// the layer is closed in the recorded timeline. Actual composition happens during
    /// <see cref="IDisposable.Dispose"/>.
    /// </remarks>
    public abstract void Restore();

    /// <summary>
    /// Restores to a specific save count.
    /// </summary>
    /// <remarks>
    /// State frames above <paramref name="saveCount"/> are discarded,
    /// and the last discarded frame becomes the current state.
    /// If any discarded state was created by a <c>SaveLayer</c> overload,
    /// those layers are closed in the recorded timeline and composed during
    /// <see cref="IDisposable.Dispose"/>.
    /// </remarks>
    /// <param name="saveCount">The save count to restore to.</param>
    public abstract void RestoreTo(int saveCount);

    /// <summary>
    /// Creates a child canvas over a subregion in local coordinates.
    /// </summary>
    /// <param name="region">The child region in local coordinates.</param>
    /// <returns>A child canvas with local origin at (0,0).</returns>
    public abstract DrawingCanvas CreateRegion(Rectangle region);

    /// <summary>
    /// Clears a path region using the given brush and clear-style composition options.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels during clear.</param>
    /// <param name="path">The path region to clear.</param>
    public abstract void Clear(Brush brush, IPath path);

    /// <summary>
    /// Fills a path in local coordinates using the given brush.
    /// </summary>
    /// <param name="brush">Brush used to shade covered pixels.</param>
    /// <param name="path">The path to fill.</param>
    public abstract void Fill(Brush brush, IPath path);

    /// <summary>
    /// Narrows the current clip region by intersecting it with the supplied clip paths.
    /// </summary>
    /// <remarks>
    /// The clip paths are transformed by the active transform at the point this is called, then
    /// intersected with the existing clip — clipping only ever narrows. The resulting clip is part of
    /// the current saved state and is restored by <see cref="Restore"/>. Multiple paths combine as a
    /// union before intersecting (e.g. a region built from several rectangles).
    /// </remarks>
    /// <param name="clipPaths">The clip paths to intersect with the current clip, in local coordinates.</param>
    public abstract void Clip(params IPath[] clipPaths);

    /// <summary>
    /// Narrows the current clip region by applying the specified clipping operation with the supplied clip paths.
    /// </summary>
    /// <remarks>
    /// The clip paths are transformed by the active transform at the point this is called.
    /// </remarks>
    /// <param name="operation">The operation to apply to the current clip.</param>
    /// <param name="clipPaths">The clip paths to combine with the current clip, in local coordinates.</param>
    public abstract void Clip(ClipOperation operation, params IPath[] clipPaths);

    /// <summary>
    /// Applies an image-processing operation to a local region.
    /// </summary>
    /// <param name="region">The local region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    public abstract void Apply(Rectangle region, Action<IImageProcessingContext> operation);

    /// <summary>
    /// Applies an image-processing operation to a region described by a path builder.
    /// </summary>
    /// <param name="pathBuilder">The path builder describing the region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    public abstract void Apply(PathBuilder pathBuilder, Action<IImageProcessingContext> operation);

    /// <summary>
    /// Applies an image-processing operation to a path region.
    /// </summary>
    /// <remarks>
    /// The operation affects only pixels covered by the supplied path.
    /// </remarks>
    /// <param name="path">The path region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    public abstract void Apply(IPath path, Action<IImageProcessingContext> operation);

    /// <summary>
    /// Draws a polyline outline using the provided pen and drawing options.
    /// </summary>
    /// <param name="pen">Pen used to generate the line outline.</param>
    /// <param name="points">Polyline points.</param>
    public abstract void DrawLine(Pen pen, params PointF[] points);

    /// <summary>
    /// Draws a path outline in local coordinates using the given pen.
    /// </summary>
    /// <param name="pen">Pen used to generate the outline fill path.</param>
    /// <param name="path">The path to stroke.</param>
    public abstract void Draw(Pen pen, IPath path);

    /// <summary>
    /// Draws text onto this canvas.
    /// </summary>
    /// <param name="textOptions">The text rendering options.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    public abstract void DrawText(
        RichTextOptions textOptions,
        ReadOnlySpan<char> text,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws text along a path baseline onto this canvas.
    /// </summary>
    /// <param name="textOptions">The text rendering options.</param>
    /// <param name="text">The text to draw.</param>
    /// <param name="path">The path used as the text baseline in local canvas coordinates.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    public abstract void DrawText(
        RichTextOptions textOptions,
        ReadOnlySpan<char> text,
        IPath path,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws a prepared text block onto this canvas.
    /// </summary>
    /// <param name="textBlock">The prepared text block to draw.</param>
    /// <param name="location">The drawing location in local canvas coordinates.</param>
    /// <param name="wrappingLength">The wrapping length in pixels. Use <c>-1</c> to disable wrapping.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    public abstract void DrawText(
        TextBlock textBlock,
        PointF location,
        float wrappingLength,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws a prepared text block along a path baseline onto this canvas.
    /// </summary>
    /// <param name="textBlock">The prepared text block to draw.</param>
    /// <param name="path">The path used as the text baseline in local canvas coordinates.</param>
    /// <param name="wrappingLength">The wrapping length in pixels. Use <c>-1</c> to disable wrapping.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    public abstract void DrawText(
        TextBlock textBlock,
        IPath path,
        float wrappingLength,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws one prepared line layout onto this canvas.
    /// </summary>
    /// <param name="lineLayout">The prepared line layout to draw.</param>
    /// <param name="location">The drawing location in local canvas coordinates.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    public abstract void DrawText(
        LineLayout lineLayout,
        PointF location,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws one prepared line layout along a path baseline onto this canvas.
    /// </summary>
    /// <param name="lineLayout">The prepared line layout to draw.</param>
    /// <param name="path">The path used as the text baseline in local canvas coordinates.</param>
    /// <param name="brush">Optional brush used to fill glyphs.</param>
    /// <param name="pen">Optional pen used to outline glyphs.</param>
    public abstract void DrawText(
        LineLayout lineLayout,
        IPath path,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws a single glyph, identified by its glyph id, onto this canvas.
    /// </summary>
    /// <param name="glyphId">The id of the glyph within the font face referenced by <paramref name="options"/>.</param>
    /// <param name="options">
    /// The glyph rendering options, including the font, origin, grapheme index and optional per-glyph paint.
    /// </param>
    /// <param name="brush">Default brush used to fill the glyph when <see cref="RichGlyphOptions.Brush"/> is not set.</param>
    /// <param name="pen">Default pen used to outline the glyph when <see cref="RichGlyphOptions.Pen"/> is not set.</param>
    public abstract void DrawText(
        ushort glyphId,
        RichGlyphOptions options,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws positioned glyphs onto this canvas.
    /// </summary>
    /// <param name="glyphRun">The positioned glyphs.</param>
    /// <param name="options">The glyph rendering options, including the font and optional glyph paint.</param>
    /// <param name="brush">Default brush used to fill glyphs when <see cref="RichGlyphOptions.Brush"/> is not set.</param>
    /// <param name="pen">Default pen used to outline glyphs when <see cref="RichGlyphOptions.Pen"/> is not set.</param>
    public abstract void DrawText(
        GlyphRun glyphRun,
        RichGlyphOptions options,
        Brush? brush,
        Pen? pen);

    /// <summary>
    /// Draws layered glyph geometry.
    /// </summary>
    /// <param name="brush">Brush used to fill glyph layers.</param>
    /// <param name="pen">Pen used to outline dominant painted layers.</param>
    /// <param name="glyphs">Layered glyph geometry to draw.</param>
    public abstract void DrawGlyphs(
        Brush brush,
        Pen pen,
        IEnumerable<GlyphPathCollection> glyphs);

    /// <summary>
    /// Measures the full set of layout metrics for the supplied text.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>A <see cref="TextMetrics"/> value containing the metrics for the laid-out text.</returns>
    public abstract TextMetrics MeasureText(RichTextOptions textOptions, ReadOnlySpan<char> text);

    /// <summary>
    /// Draws an image source region into a destination rectangle.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="sourceRect">The source rectangle within <paramref name="image"/>.</param>
    /// <param name="destinationRect">The destination rectangle in local canvas coordinates.</param>
    /// <param name="sampler">
    /// Optional resampler used when scaling or transforming the image. Defaults to <see cref="KnownResamplers.Bicubic"/>.
    /// </param>
    public abstract void DrawImage(
        Image image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        IResampler? sampler = null);

    /// <summary>
    /// Draws an image source region into a destination rectangle, tiling the painted area by repeating
    /// the destination rectangle outwards per the supplied <see cref="WrapMode"/>s.
    /// </summary>
    /// <param name="image">The source image.</param>
    /// <param name="sourceRect">The source rectangle within <paramref name="image"/>.</param>
    /// <param name="destinationRect">The destination rectangle in local canvas coordinates (defines a single tile cell).</param>
    /// <param name="wrapX">The horizontal wrap mode applied when sampling beyond <paramref name="destinationRect"/>.</param>
    /// <param name="wrapY">The vertical wrap mode applied when sampling beyond <paramref name="destinationRect"/>.</param>
    /// <param name="sampler">
    /// Optional resampler used when scaling or transforming the image. Defaults to <see cref="KnownResamplers.Bicubic"/>.
    /// </param>
    public abstract void DrawImage(
        Image image,
        Rectangle sourceRect,
        RectangleF destinationRect,
        WrapMode wrapX,
        WrapMode wrapY,
        IResampler? sampler = null);

    /// <summary>
    /// Creates a retained backend scene from the drawing commands currently queued on this canvas.
    /// </summary>
    /// <returns>A retained backend scene.</returns>
    public abstract DrawingBackendScene CreateScene();

    /// <summary>
    /// Renders a retained backend scene into this canvas target.
    /// </summary>
    /// <param name="scene">The retained backend scene to render.</param>
    public abstract void RenderScene(DrawingBackendScene scene);

    /// <summary>
    /// Seals queued drawing commands into the canvas timeline.
    /// </summary>
    public abstract void Flush();

    /// <inheritdoc />
    public abstract void Dispose();
}
