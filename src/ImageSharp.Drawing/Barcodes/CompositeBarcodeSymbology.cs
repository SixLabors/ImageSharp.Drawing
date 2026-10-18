// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A composite barcode symbology: one symbol made of two or more symbologies. Each symbology encodes its own text into its own part of the symbol, and the
/// composite places each part relative to the part before it.
/// </summary>
public abstract class CompositeBarcodeSymbology
{
    /// <summary>
    /// The symbologies of the parts, in the order of their texts.
    /// </summary>
    private readonly BarcodeSymbology[] symbologies;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeBarcodeSymbology"/> class.
    /// </summary>
    /// <param name="symbologies">The symbologies of the parts, in the order of their texts. There are two or more.</param>
    private protected CompositeBarcodeSymbology(params ReadOnlySpan<BarcodeSymbology> symbologies)
    {
        Guard.MustBeGreaterThanOrEqualTo(symbologies.Length, 2, nameof(symbologies));
        foreach (BarcodeSymbology symbology in symbologies)
        {
            Guard.NotNull(symbology, nameof(symbologies));
        }

        // The symbology keeps its own copy, so a caller that changes its span after construction cannot change the parts.
        this.symbologies = symbologies.ToArray();
    }

    /// <summary>
    /// One pass over the parts of a composite: measure or draw.
    /// </summary>
    private interface IPartPass
    {
        /// <summary>
        /// Measures or draws one part.
        /// </summary>
        /// <param name="part">The encoded part.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="origin">The top left corner of the part, in pixels.</param>
        /// <returns>The measured layout of the part.</returns>
        public BarcodeMetrics Run(ref BarcodeSymbol part, BarcodeOptions options, PointF origin);
    }

    /// <summary>
    /// Gets the symbologies of the parts, in the order of their texts.
    /// </summary>
    public ReadOnlySpan<BarcodeSymbology> Symbologies => this.symbologies;

    /// <summary>
    /// Encodes a composite of two parts and measures the parts from the origin without drawing anything.
    /// </summary>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the first part, in pixels.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <returns>The union of the metrics of the parts.</returns>
    /// <exception cref="ArgumentException">The composite does not have two parts, a text is not valid, or the parts do not join.</exception>
    internal BarcodeMetrics Measure(BarcodeOptions options, PointF origin, ReadOnlySpan<char> first, ReadOnlySpan<char> second)
        => this.Measure(options, origin, 2, first, second, []);

    /// <summary>
    /// Encodes a composite of three parts and measures the parts from the origin without drawing anything.
    /// </summary>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the first part, in pixels.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <param name="third">The text of the third symbology.</param>
    /// <returns>The union of the metrics of the parts.</returns>
    /// <exception cref="ArgumentException">The composite does not have three parts, a text is not valid, or the parts do not join.</exception>
    internal BarcodeMetrics Measure(BarcodeOptions options, PointF origin, ReadOnlySpan<char> first, ReadOnlySpan<char> second, ReadOnlySpan<char> third)
        => this.Measure(options, origin, 3, first, second, third);

    /// <summary>
    /// Encodes a composite of two parts and draws the parts from the origin.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the first part, in pixels.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <returns>The union of the metrics of the parts.</returns>
    /// <exception cref="ArgumentException">The composite does not have two parts, a text is not valid, or the parts do not join.</exception>
    internal BarcodeMetrics Draw(DrawingCanvas canvas, BarcodeOptions options, PointF origin, ReadOnlySpan<char> first, ReadOnlySpan<char> second)
        => this.Draw(canvas, options, origin, 2, first, second, []);

    /// <summary>
    /// Encodes a composite of three parts and draws the parts from the origin.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the first part, in pixels.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <param name="third">The text of the third symbology.</param>
    /// <returns>The union of the metrics of the parts.</returns>
    /// <exception cref="ArgumentException">The composite does not have three parts, a text is not valid, or the parts do not join.</exception>
    internal BarcodeMetrics Draw(
        DrawingCanvas canvas,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
        => this.Draw(canvas, options, origin, 3, first, second, third);

    /// <summary>
    /// Encodes the text of one part. By default the symbology of the part encodes its text as it would alone. A composite whose standard changes a part when
    /// it is joined, a linkage flag for example, overrides this to encode that part the joined way.
    /// </summary>
    /// <param name="index">The index of the part in <see cref="Symbologies"/>.</param>
    /// <param name="text">The text of the part.</param>
    /// <param name="options">The options that control layout choices made during encoding.</param>
    /// <param name="part">The part to write. It is empty on entry.</param>
    /// <exception cref="ArgumentException">The text is not valid for the symbology of the part.</exception>
    internal virtual void EncodePart(int index, ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol part)
        => this.symbologies[index].Encode(text, options, ref part);

    /// <summary>
    /// Returns the origin of a part after the first: the composite places it relative to the part before it, as its standard requires.
    /// </summary>
    /// <param name="index">The index of the part in <see cref="Symbologies"/>, 1 or more.</param>
    /// <param name="parts">The encoded parts, one for each symbology of <see cref="Symbologies"/> and in the same order.</param>
    /// <param name="previousOrigin">The origin of the part before it, in pixels.</param>
    /// <param name="previous">The metrics of the part before it. Its advance is relative to <paramref name="previousOrigin"/>.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <returns>The top left corner of the part, in pixels.</returns>
    /// <exception cref="ArgumentException">The parts do not join.</exception>
    internal abstract PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, PointF previousOrigin, in BarcodeMetrics previous, BarcodeOptions options);

    /// <summary>
    /// Returns a rectangle moved by an offset.
    /// </summary>
    /// <param name="rectangle">The rectangle.</param>
    /// <param name="offset">The offset.</param>
    /// <returns>The moved rectangle.</returns>
    private static RectangleF Offset(RectangleF rectangle, PointF offset)
        => new(rectangle.X + offset.X, rectangle.Y + offset.Y, rectangle.Width, rectangle.Height);

    /// <summary>
    /// Encodes each text with its own symbology and measures the parts. The parts live on this stack frame and in the arrays they rent, which they return
    /// once they are measured.
    /// </summary>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the first part, in pixels.</param>
    /// <param name="count">The number of texts the caller gives, 2 or 3.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <param name="third">The text of the third symbology, unused when <paramref name="count"/> is 2.</param>
    /// <returns>The union of the metrics of the parts.</returns>
    private BarcodeMetrics Measure(
        BarcodeOptions options,
        PointF origin,
        int count,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
    {
        InlineArray3<BarcodeSymbol> partStorage = default;
        Span<BarcodeSymbol> parts = ((Span<BarcodeSymbol>)partStorage)[..count];
        InlineArray3<PointF> originStorage = default;
        try
        {
            this.EncodeParts(parts, options, first, second, third);
            MeasurePass measure = default;
            return this.LayoutParts(ref measure, parts, options, origin, ((Span<PointF>)originStorage)[..count]);
        }
        finally
        {
            foreach (ref BarcodeSymbol part in parts)
            {
                part.Dispose();
            }
        }
    }

    /// <summary>
    /// Encodes each text with its own symbology and draws the parts. The composite is one symbol, so one background fills the renderable bounds of all the
    /// parts before any part draws, and each part draws with no background of its own. The parts live on this stack frame and in the arrays they rent, which
    /// they return once they are drawn.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the first part, in pixels.</param>
    /// <param name="count">The number of texts the caller gives, 2 or 3.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <param name="third">The text of the third symbology, unused when <paramref name="count"/> is 2.</param>
    /// <returns>The union of the metrics of the parts.</returns>
    private BarcodeMetrics Draw(
        DrawingCanvas canvas,
        BarcodeOptions options,
        PointF origin,
        int count,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
    {
        InlineArray3<BarcodeSymbol> partStorage = default;
        Span<BarcodeSymbol> parts = ((Span<BarcodeSymbol>)partStorage)[..count];
        InlineArray3<PointF> originStorage = default;
        Span<PointF> origins = ((Span<PointF>)originStorage)[..count];
        try
        {
            this.EncodeParts(parts, options, first, second, third);

            // Without a background each part draws as soon as the part before it has its metrics, so every part is laid out one time.
            if (options.Background is null)
            {
                DrawPass draw = new(canvas);
                return this.LayoutParts(ref draw, parts, options, origin, origins);
            }

            // The background must fill the union of the parts before any part draws, and the union is known only when every part has its position.
            MeasurePass measure = default;
            BarcodeMetrics metrics = this.LayoutParts(ref measure, parts, options, origin, origins);
            RectangleF renderable = metrics.RenderableBounds;
            canvas.Fill(options.Background, new RectanglePolygon(renderable.X, renderable.Y, renderable.Width, renderable.Height));
            for (int i = 0; i < parts.Length; i++)
            {
                BarcodeEmitter.Draw(canvas, ref parts[i], options, origins[i], null);
            }

            return metrics;
        }
        finally
        {
            foreach (ref BarcodeSymbol part in parts)
            {
                part.Dispose();
            }
        }
    }

    /// <summary>
    /// Encodes each text with the symbology of its part.
    /// </summary>
    /// <param name="parts">The parts to write, one for each text the caller gives.</param>
    /// <param name="options">The options that control layout choices made during encoding.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <param name="third">The text of the third symbology, unused when there are two parts.</param>
    /// <exception cref="ArgumentException">The composite has a different number of parts, or a text is not valid for its symbology.</exception>
    private void EncodeParts(Span<BarcodeSymbol> parts, BarcodeOptions options, ReadOnlySpan<char> first, ReadOnlySpan<char> second, ReadOnlySpan<char> third)
    {
        if (this.symbologies.Length != parts.Length)
        {
            throw new ArgumentException($"The composite has {this.symbologies.Length} parts; got {parts.Length} texts.");
        }

        this.EncodePart(0, first, options, ref parts[0]);
        this.EncodePart(1, second, options, ref parts[1]);
        if (parts.Length == 3)
        {
            this.EncodePart(2, third, options, ref parts[2]);
        }
    }

    /// <summary>
    /// Measures or draws the parts in order. The first part stands at the origin, and each next part stands where <see cref="Place"/> puts it from the metrics
    /// of the part before it. Each part reports its advance relative to its own origin, so the advances join at their absolute positions. The bounds and the
    /// renderable bounds are absolute already.
    /// </summary>
    /// <typeparam name="TPass">The pass that measures or draws each part. It is a struct, so the compiler specializes the loop for each pass.</typeparam>
    /// <param name="pass">The pass that measures or draws each part.</param>
    /// <param name="parts">The encoded parts.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the first part, in pixels.</param>
    /// <param name="origins">The origin of each part, to write.</param>
    /// <returns>The union of the metrics of the parts, with the advance relative to <paramref name="origin"/>.</returns>
    private BarcodeMetrics LayoutParts<TPass>(ref TPass pass, Span<BarcodeSymbol> parts, BarcodeOptions options, PointF origin, Span<PointF> origins)
        where TPass : struct, IPartPass
    {
        origins[0] = origin;
        BarcodeMetrics previous = pass.Run(ref parts[0], options, origin);
        RectangleF advance = Offset(previous.Advance, origin);
        RectangleF bounds = previous.Bounds;
        RectangleF renderable = previous.RenderableBounds;
        for (int i = 1; i < parts.Length; i++)
        {
            origins[i] = this.Place(i, parts, origins[i - 1], in previous, options);
            previous = pass.Run(ref parts[i], options, origins[i]);
            advance = RectangleF.Union(advance, Offset(previous.Advance, origins[i]));
            bounds = RectangleF.Union(bounds, previous.Bounds);
            renderable = RectangleF.Union(renderable, previous.RenderableBounds);
        }

        return new BarcodeMetrics(Offset(advance, new PointF(-origin.X, -origin.Y)), bounds, renderable);
    }

    /// <summary>
    /// The pass that measures each part without drawing anything.
    /// </summary>
    private readonly struct MeasurePass : IPartPass
    {
        /// <inheritdoc/>
        public BarcodeMetrics Run(ref BarcodeSymbol part, BarcodeOptions options, PointF origin)
            => BarcodeEmitter.Measure(ref part, options, origin);
    }

    /// <summary>
    /// The pass that draws each part onto a canvas with no background of its own.
    /// </summary>
    private readonly struct DrawPass : IPartPass
    {
        /// <summary>
        /// The canvas to render onto.
        /// </summary>
        private readonly DrawingCanvas canvas;

        /// <summary>
        /// Initializes a new instance of the <see cref="DrawPass"/> struct.
        /// </summary>
        /// <param name="canvas">The canvas to render onto.</param>
        public DrawPass(DrawingCanvas canvas) => this.canvas = canvas;

        /// <inheritdoc/>
        public BarcodeMetrics Run(ref BarcodeSymbol part, BarcodeOptions options, PointF origin)
            => BarcodeEmitter.Draw(this.canvas, ref part, options, origin, null);
    }
}
