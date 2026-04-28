// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Text;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents a drawing canvas over a frame target.
/// </summary>
public abstract class DrawingCanvas : IDisposable
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
    /// logical layer. When <see cref="Restore"/> closes the layer, it is composed during
    /// the next <see cref="Flush"/> or <see cref="IDisposable.Dispose"/> using the specified
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
    /// when composition runs (on the next <see cref="Flush"/> or <see cref="IDisposable.Dispose"/>).
    /// </param>
    /// <param name="bounds">
    /// The local bounds of the layer. Only this region is allocated and composited.
    /// </param>
    /// <returns>The save count after the layer state has been pushed.</returns>
    public abstract int SaveLayer(GraphicsOptions layerOptions, Rectangle bounds);

    /// <summary>
    /// Restores the most recently saved state.
    /// </summary>
    /// <remarks>
    /// If the most recently saved state was created by a <c>SaveLayer</c> overload,
    /// the layer is closed in the deferred scene. Actual composition happens during the
    /// next <see cref="Flush"/> or <see cref="IDisposable.Dispose"/>.
    /// </remarks>
    public abstract void Restore();

    /// <summary>
    /// Restores to a specific save count.
    /// </summary>
    /// <remarks>
    /// State frames above <paramref name="saveCount"/> are discarded,
    /// and the last discarded frame becomes the current state.
    /// If any discarded state was created by a <c>SaveLayer</c> overload,
    /// those layers are closed in the deferred scene and are composed during the next
    /// <see cref="Flush"/> or <see cref="IDisposable.Dispose"/>.
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
    /// Measures the full set of layout metrics for the supplied text in a single pass.
    /// </summary>
    /// <param name="textOptions">The text shaping and layout options.</param>
    /// <param name="text">The text to measure.</param>
    /// <returns>A <see cref="TextMetrics"/> value containing every measurement for the laid-out text.</returns>
    /// <remarks>
    /// <para>
    /// The returned <see cref="TextMetrics"/> exposes logical advance, rendered bounds, normalized size,
    /// combined renderable bounds, per-character entries, and per-line metrics. The text is shaped and
    /// laid out once, so this is the cheapest option when more than one measurement is required.
    /// </para>
    /// <para>
    /// When only one or two values are required, call the granular overloads on
    /// <see cref="TextMeasurer"/> (for example <see cref="TextMeasurer.MeasureAdvance(ReadOnlySpan{char}, TextOptions)"/>,
    /// <see cref="TextMeasurer.MeasureBounds(ReadOnlySpan{char}, TextOptions)"/>, or
    /// <see cref="TextMeasurer.CountLines(ReadOnlySpan{char}, TextOptions)"/>) directly.
    /// Those overloads skip the per-character and per-line array materialization performed here.
    /// </para>
    /// </remarks>
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
    /// Flushes queued drawing commands to the target in submission order.
    /// </summary>
    public abstract void Flush();

    /// <inheritdoc />
    public abstract void Dispose();
}
