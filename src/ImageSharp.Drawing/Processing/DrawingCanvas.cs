// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a stateful, retained-mode drawing canvas over a frame target.
/// </summary>
/// <remarks>
/// Draw calls are recorded into an ordered command stream rather than rasterized immediately;
/// the root canvas replays the recorded timeline when it is disposed. Drawing state (options,
/// transform, clip and layer scope) is managed through the <see cref="Save()"/>/<see cref="Restore"/>
/// state stack.
/// </remarks>
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
    /// Saves the current drawing state and replaces the active state with the provided options.
    /// </summary>
    /// <remarks>
    /// The provided <paramref name="options"/> instance is stored by reference.
    /// Mutating it after this call mutates the active/restored state behavior.
    /// </remarks>
    /// <param name="options">Drawing options for the new active state.</param>
    /// <returns>The save count after the previous state has been pushed.</returns>
    public abstract int Save(DrawingOptions options);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer bounded to a subregion.
    /// Subsequent draw commands target that layer until <see cref="Restore"/> closes it.
    /// </summary>
    /// <remarks>
    /// The layer bounds are expressed in the current local coordinate system and are
    /// transformed with the active drawing transform when the layer is created. They
    /// limit allocation and compositing only; they do not change the canvas coordinate
    /// system used by commands recorded inside the layer.
    /// </remarks>
    /// <param name="layerOptions">
    /// Graphics options controlling how the closed layer is composited against the parent canvas.
    /// </param>
    /// <param name="bounds">
    /// The local bounds of the layer. Only this region is allocated and composited.
    /// </param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer
    /// using the supplied drawing options for commands recorded into the layer.
    /// </summary>
    /// <param name="layerOptions">
    /// Graphics options controlling how the closed layer is composited against the parent canvas.
    /// </param>
    /// <param name="bounds">
    /// The local bounds of the layer. Only this region is allocated and composited.
    /// </param>
    /// <param name="options">Drawing options for the layer contents.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds, DrawingOptions options);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer whose content is
    /// transformed by an effect when the layer is restored.
    /// </summary>
    /// <remarks>
    /// The layer isolates the content, so the effect operates on exactly what is drawn between
    /// this call and the matching <see cref="Restore"/>, against transparency; a
    /// <see cref="DropShadowLayerEffect"/>, for example, slots its shadow beneath that content
    /// before the layer composites onto the canvas. The bounds are expanded internally by the
    /// effect's reach so blurred or offset output is not cut off.
    /// </remarks>
    /// <param name="layerOptions">
    /// Graphics options controlling how the closed layer is composited against the parent canvas.
    /// </param>
    /// <param name="bounds">The content bounds in local canvas coordinates.</param>
    /// <param name="effect">The effect applied to the layer content on restore.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds, LayerEffect effect);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer whose content is
    /// transformed by an effect when the layer is restored, using the supplied drawing options for
    /// commands recorded into the layer.
    /// </summary>
    /// <param name="layerOptions">
    /// Graphics options controlling how the closed layer is composited against the parent canvas.
    /// </param>
    /// <param name="bounds">The content bounds in local canvas coordinates.</param>
    /// <param name="effect">The effect applied to the layer content on restore.</param>
    /// <param name="options">Drawing options for the layer contents.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds, LayerEffect effect, DrawingOptions options);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer whose content is
    /// transformed by an effect confined to a path region when the layer is restored.
    /// </summary>
    /// <remarks>
    /// The effect processes only pixels covered by the supplied path; its output lands on the path
    /// translated by the effect's offset. The layer bounds are derived from the path and expanded
    /// by the effect's reach.
    /// </remarks>
    /// <param name="layerOptions">
    /// Graphics options controlling how the closed layer is composited against the parent canvas.
    /// </param>
    /// <param name="region">The path region the effect processes, in local coordinates.</param>
    /// <param name="effect">The effect applied to the layer content on restore.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, IPath region, LayerEffect effect);

    /// <summary>
    /// Saves the current drawing state and begins an isolated compositing layer whose content is
    /// transformed by an effect confined to a path region when the layer is restored, using the
    /// supplied drawing options for commands recorded into the layer.
    /// </summary>
    /// <param name="layerOptions">
    /// Graphics options controlling how the closed layer is composited against the parent canvas.
    /// </param>
    /// <param name="region">The path region the effect processes, in local coordinates.</param>
    /// <param name="effect">The effect applied to the layer content on restore.</param>
    /// <param name="options">Drawing options for the layer contents.</param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, IPath region, LayerEffect effect, DrawingOptions options);

    /// <summary>
    /// Restores the most recently saved state.
    /// </summary>
    /// <remarks>
    /// If the most recently saved state was created by a <c>SaveLayer</c> overload,
    /// the layer is closed and becomes part of subsequent rendering.
    /// </remarks>
    public abstract void Restore();

    /// <summary>
    /// Restores to a specific save count.
    /// </summary>
    /// <remarks>
    /// State frames above <paramref name="saveCount"/> are discarded,
    /// and the last discarded frame becomes the current state.
    /// If any discarded state was created by a <c>SaveLayer</c> overload,
    /// those layers are closed and become part of subsequent rendering.
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
    /// Fills a path region with the given brush using replacement composition: the brush output
    /// overwrites the covered pixels outright, including their alpha, rather than blending over them.
    /// </summary>
    /// <param name="brush">Brush used to shade destination pixels during the clear.</param>
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
    /// intersected with the existing clip; clipping only ever narrows. The resulting clip is part of
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
    /// Applies an image-processing operation to a local region and composites the processed pixels
    /// back with explicit options, optionally offset from where they were read. The target region
    /// is untouched while the operation runs, so the processed pixels can be blended against the
    /// original content; for example a drop shadow composites the tinted, blurred region back
    /// beneath the content with <see cref="PixelAlphaCompositionMode.DestOver"/> at the shadow offset.
    /// </summary>
    /// <param name="region">The local region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    /// <param name="writeBackOptions">The graphics options used to composite the processed pixels back.</param>
    /// <param name="writeBackOffset">The offset at which the processed pixels are written back.</param>
    public abstract void Apply(Rectangle region, Action<IImageProcessingContext> operation, GraphicsOptions writeBackOptions, Point writeBackOffset);

    /// <summary>
    /// Applies an image-processing operation to a path region and composites the processed pixels
    /// back with explicit options, optionally offset from where they were read.
    /// </summary>
    /// <remarks>
    /// The write-back affects only pixels covered by the supplied path translated by the offset.
    /// </remarks>
    /// <param name="path">The path region to process.</param>
    /// <param name="operation">The image-processing operation to apply to the region.</param>
    /// <param name="writeBackOptions">The graphics options used to composite the processed pixels back.</param>
    /// <param name="writeBackOffset">The offset at which the processed pixels are written back.</param>
    public abstract void Apply(IPath path, Action<IImageProcessingContext> operation, GraphicsOptions writeBackOptions, Point writeBackOffset);

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
    /// <param name="glyphIds">The glyph identifiers.</param>
    /// <param name="points">The absolute glyph origins in pixel units.</param>
    /// <param name="options">The glyph rendering options, including the font and optional glyph paint.</param>
    /// <param name="brush">Default brush used to fill glyphs when <see cref="RichGlyphOptions.Brush"/> is not set.</param>
    /// <param name="pen">Default pen used to outline glyphs when <see cref="RichGlyphOptions.Pen"/> is not set.</param>
    public abstract void DrawText(ReadOnlySpan<ushort> glyphIds, ReadOnlySpan<Vector2> points, RichGlyphOptions options, Brush? brush, Pen? pen);

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
    /// Copies pixels from another canvas into this canvas.
    /// </summary>
    /// <remarks>
    /// Any queued commands on both canvases are applied before the copy.
    /// </remarks>
    /// <param name="source">The source canvas.</param>
    /// <param name="sourceRectangle">The source rectangle in source-local coordinates.</param>
    /// <param name="targetPoint">The target point in this canvas' local coordinates.</param>
    public abstract void CopyPixelsFrom(DrawingCanvas source, Rectangle sourceRectangle, Point targetPoint);

    /// <summary>
    /// Seals the commands recorded so far into a retained backend scene and clears them from the
    /// canvas queue.
    /// </summary>
    /// <remarks>
    /// The active clip range is closed before the scene is built and reopened afterwards, so this
    /// acts as an ordering boundary in the recorded timeline. Image resources retained for the
    /// sealed commands are transferred to the returned scene, which owns and disposes them.
    /// </remarks>
    /// <returns>The created backend scene.</returns>
    public abstract DrawingBackendScene CreateScene();

    /// <summary>
    /// Records a previously created backend scene into this canvas' command timeline.
    /// </summary>
    /// <remarks>
    /// The active command range is sealed before the scene is inserted so the scene renders in
    /// submission order relative to the commands recorded around it. Like other recorded work,
    /// the scene is replayed into the target frame when the root canvas is disposed or otherwise
    /// flushed, not at the point of this call.
    /// </remarks>
    /// <param name="scene">The backend scene to render.</param>
    public abstract void RenderScene(DrawingBackendScene scene);

    /// <summary>
    /// Seals the drawing commands recorded so far into an ordered range so that subsequent
    /// commands begin a new range.
    /// </summary>
    /// <remarks>
    /// This does not rasterize or composite anything into the target frame. Recorded commands
    /// are replayed into the target only when the root canvas is disposed, or when they are
    /// lowered explicitly through <see cref="CreateScene"/>, <see cref="RenderScene"/> or
    /// <see cref="CopyPixelsFrom"/>. Use this to establish an ordering boundary in the recorded
    /// timeline without ending the canvas.
    /// </remarks>
    public abstract void Flush();

    /// <summary>
    /// Releases the canvas. Disposing the root canvas closes any open layers and clips and
    /// replays the recorded command timeline into the target frame; child region canvases
    /// share the root's command stream and do not flush on disposal.
    /// </summary>
    /// <remarks>
    /// The root canvas also releases image resources retained for deferred draws and, when it
    /// owns the text drawing cache, clears that cache. Disposal is idempotent.
    /// </remarks>
    public abstract void Dispose();
}
