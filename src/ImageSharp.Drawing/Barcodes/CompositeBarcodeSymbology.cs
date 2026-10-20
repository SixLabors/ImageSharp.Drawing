// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A composite barcode symbology: one symbol made of two or more symbologies. Each symbology encodes its own text into its own part of the symbol, and the
/// composite places each part relative to the parts before it.
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
    /// One pass over the arranged parts of a composite: measure or draw.
    /// </summary>
    private interface ICompositePass
    {
        /// <summary>
        /// Measures or draws the arranged parts.
        /// </summary>
        /// <param name="parts">The arranged parts.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="origin">The top left corner of the composite, in pixels.</param>
        /// <param name="advance">The layout box of the composite, relative to <paramref name="origin"/>.</param>
        /// <returns>The metrics of the composite.</returns>
        public BarcodeMetrics Run(in CompositeParts parts, BarcodeOptions options, PointF origin, RectangleF advance);
    }

    /// <summary>
    /// Gets the symbologies of the parts, in the order of their texts.
    /// </summary>
    public ReadOnlySpan<BarcodeSymbology> Symbologies => this.symbologies;

    /// <summary>
    /// Encodes a composite of two parts and measures the parts from the origin without drawing anything.
    /// </summary>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the composite, in pixels.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <returns>The metrics of the composite.</returns>
    /// <exception cref="ArgumentException">The composite does not have two parts, a text is not valid, or the parts do not join.</exception>
    internal BarcodeMetrics Measure(BarcodeOptions options, PointF origin, ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        MeasurePass measure = default;
        return this.Lay(ref measure, options, origin, 2, first, second, []);
    }

    /// <summary>
    /// Encodes a composite of three parts and measures the parts from the origin without drawing anything.
    /// </summary>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the composite, in pixels.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <param name="third">The text of the third symbology.</param>
    /// <returns>The metrics of the composite.</returns>
    /// <exception cref="ArgumentException">The composite does not have three parts, a text is not valid, or the parts do not join.</exception>
    internal BarcodeMetrics Measure(BarcodeOptions options, PointF origin, ReadOnlySpan<char> first, ReadOnlySpan<char> second, ReadOnlySpan<char> third)
    {
        MeasurePass measure = default;
        return this.Lay(ref measure, options, origin, 3, first, second, third);
    }

    /// <summary>
    /// Encodes a composite of two parts and draws the parts from the origin.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the composite, in pixels.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <returns>The metrics of the composite.</returns>
    /// <exception cref="ArgumentException">The composite does not have two parts, a text is not valid, or the parts do not join.</exception>
    internal BarcodeMetrics Draw(DrawingCanvas canvas, BarcodeOptions options, PointF origin, ReadOnlySpan<char> first, ReadOnlySpan<char> second)
    {
        DrawPass draw = new(canvas);
        return this.Lay(ref draw, options, origin, 2, first, second, []);
    }

    /// <summary>
    /// Encodes a composite of three parts and draws the parts from the origin.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the composite, in pixels.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <param name="third">The text of the third symbology.</param>
    /// <returns>The metrics of the composite.</returns>
    /// <exception cref="ArgumentException">The composite does not have three parts, a text is not valid, or the parts do not join.</exception>
    internal BarcodeMetrics Draw(
        DrawingCanvas canvas,
        BarcodeOptions options,
        PointF origin,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
    {
        DrawPass draw = new(canvas);
        return this.Lay(ref draw, options, origin, 3, first, second, third);
    }

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
    /// Returns where the symbol of a part after the first stands. The composite places it relative to the symbols of the parts before it, as its standard
    /// requires. A symbol stands at the top left corner of its first module, so the quiet zones and the text of a part do not move it.
    /// </summary>
    /// <param name="index">The index of the part in <see cref="Symbologies"/>, 1 or more.</param>
    /// <param name="parts">The encoded parts, one for each symbology of <see cref="Symbologies"/> and in the same order.</param>
    /// <param name="symbolOrigins">The top left corner of the first module of the symbol of each part before it, in pixels.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <returns>The top left corner of the first module of the symbol of the part, in pixels.</returns>
    /// <exception cref="ArgumentException">The parts do not join.</exception>
    internal abstract PointF Place(int index, ReadOnlySpan<BarcodeSymbol> parts, ReadOnlySpan<PointF> symbolOrigins, BarcodeOptions options);

    /// <summary>
    /// Returns the metrics of a composite. The renderable bounds are the union of the layout box and the bounds, as they are for one symbol.
    /// </summary>
    /// <param name="origin">The top left corner of the composite, in pixels.</param>
    /// <param name="advance">The layout box of the composite, relative to <paramref name="origin"/>.</param>
    /// <param name="bounds">The union of the ink of the parts, in pixels.</param>
    /// <returns>The metrics of the composite.</returns>
    private static BarcodeMetrics CompositeMetrics(PointF origin, RectangleF advance, RectangleF bounds)
        => new(advance, bounds, RectangleF.Union(new RectangleF(origin, advance.Size), bounds));

    /// <summary>
    /// Encodes each text with its own symbology, shapes the text of each part one time, arranges the parts and runs the pass over them. The parts live on
    /// this stack frame and in the arrays they rent, which they return once the pass ends.
    /// </summary>
    /// <typeparam name="TPass">The pass that measures or draws the parts. It is a struct, so the compiler specializes this method for each pass.</typeparam>
    /// <param name="pass">The pass that measures or draws the parts.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the composite, in pixels.</param>
    /// <param name="count">The number of texts the caller gives, 2 or 3.</param>
    /// <param name="first">The text of the first symbology.</param>
    /// <param name="second">The text of the second symbology.</param>
    /// <param name="third">The text of the third symbology, unused when <paramref name="count"/> is 2.</param>
    /// <returns>The metrics of the composite.</returns>
    private BarcodeMetrics Lay<TPass>(
        ref TPass pass,
        BarcodeOptions options,
        PointF origin,
        int count,
        ReadOnlySpan<char> first,
        ReadOnlySpan<char> second,
        ReadOnlySpan<char> third)
        where TPass : struct, ICompositePass
    {
        InlineArray3<BarcodeSymbol> partStorage = default;
        Span<BarcodeSymbol> symbols = ((Span<BarcodeSymbol>)partStorage)[..count];
        try
        {
            this.EncodeParts(symbols, options, first, second, third);

            // The lines of all the parts share one buffer, and the lines of part i are the range from lineStarts[i] to lineStarts[i + 1].
            InlineArray4<int> startStorage = default;
            Span<int> lineStarts = ((Span<int>)startStorage)[..(count + 1)];
            for (int i = 0; i < count; i++)
            {
                lineStarts[i + 1] = lineStarts[i] + BarcodeEmitter.LineCount(ref symbols[i], options);
            }

            int lineCount = lineStarts[count];
            using PooledBuffer<MeasuredLine> lineBuffer = lineCount == 0 ? default : new(lineCount);
            Span<MeasuredLine> lines = lineBuffer.Span;

            // A TextBlock is a class, so the span holds object references and cannot come from the byte pool. A bar symbol has at most 14 text placements, a
            // matrix symbol 1 and a hexagonal symbol none, so three parts have at most 42. The 32 stack slots hold up to 32, and a larger count takes an array.
            InlineArray32<TextBlock> blockStorage = default;
            Span<TextBlock> blocks = lineCount <= 32 ? ((Span<TextBlock>)blockStorage)[..lineCount] : new TextBlock[lineCount];
            for (int i = 0; i < count; i++)
            {
                Range range = lineStarts[i]..lineStarts[i + 1];
                BarcodeEmitter.Shape(ref symbols[i], options, lines[range], blocks[range]);
            }

            InlineArray3<PointF> originStorage = default;
            CompositeParts parts = new(symbols, lines, blocks, lineStarts, ((Span<PointF>)originStorage)[..count]);
            RectangleF advance = this.Arrange(in parts, options, origin);
            return pass.Run(in parts, options, origin, advance);
        }
        finally
        {
            foreach (ref BarcodeSymbol symbol in symbols)
            {
                symbol.Dispose();
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
    /// Writes the origin of each part. The text and the quiet zones of a part set where its symbol stands in its layout box, and nothing in the layout box
    /// rounds, so that offset is the same wherever the part stands. The layout of each part at zero gives the offset. The symbol of the first part stands at
    /// its offset, <see cref="Place"/> puts the symbol of each next part, and each part starts its offset before its symbol. The union of the layout boxes then
    /// moves so that its top left corner is the origin, as the layout box of one symbol does.
    /// </summary>
    /// <param name="parts">The shaped parts. Their origins are written.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the composite, in pixels.</param>
    /// <returns>The layout box of the composite, relative to <paramref name="origin"/>.</returns>
    /// <exception cref="ArgumentException">The parts do not join.</exception>
    private RectangleF Arrange(in CompositeParts parts, BarcodeOptions options, PointF origin)
    {
        Span<BarcodeSymbol> symbols = parts.Symbols;
        Span<PointF> origins = parts.Origins;
        InlineArray3<PointF> symbolStorage = default;
        Span<PointF> symbolOrigins = ((Span<PointF>)symbolStorage)[..symbols.Length];
        RectangleF box = RectangleF.Empty;
        for (int i = 0; i < symbols.Length; i++)
        {
            BarcodeMetrics metrics = BarcodeEmitter.Measure(ref symbols[i], options, PointF.Empty, parts.Lines(i), out PointF offset);
            symbolOrigins[i] = i == 0 ? offset : this.Place(i, symbols, symbolOrigins[..i], options);
            origins[i] = new PointF(symbolOrigins[i].X - offset.X, symbolOrigins[i].Y - offset.Y);

            RectangleF partBox = new(origins[i], metrics.Advance.Size);
            box = i == 0 ? partBox : RectangleF.Union(box, partBox);
        }

        for (int i = 0; i < origins.Length; i++)
        {
            origins[i] = new PointF(origin.X + origins[i].X - box.X, origin.Y + origins[i].Y - box.Y);
        }

        return new RectangleF(0, 0, box.Width, box.Height);
    }

    /// <summary>
    /// The encoded parts of a composite, their shaped text and their origins. The lines and the blocks of all the parts share one span each, and each part
    /// reads its own range.
    /// </summary>
    private readonly ref struct CompositeParts
    {
        /// <summary>
        /// The shaped lines of all the parts.
        /// </summary>
        private readonly Span<MeasuredLine> lines;

        /// <summary>
        /// The shaped blocks of all the parts, one for each line.
        /// </summary>
        private readonly ReadOnlySpan<TextBlock> blocks;

        /// <summary>
        /// The index of the first line of each part, and the line count after the last part.
        /// </summary>
        private readonly ReadOnlySpan<int> lineStarts;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompositeParts"/> struct.
        /// </summary>
        /// <param name="symbols">The encoded parts.</param>
        /// <param name="lines">The shaped lines of all the parts.</param>
        /// <param name="blocks">The shaped blocks of all the parts, one for each line.</param>
        /// <param name="lineStarts">The index of the first line of each part, and the line count after the last part.</param>
        /// <param name="origins">The top left corner of each part, to write.</param>
        public CompositeParts(
            Span<BarcodeSymbol> symbols,
            Span<MeasuredLine> lines,
            ReadOnlySpan<TextBlock> blocks,
            ReadOnlySpan<int> lineStarts,
            Span<PointF> origins)
        {
            this.Symbols = symbols;
            this.lines = lines;
            this.blocks = blocks;
            this.lineStarts = lineStarts;
            this.Origins = origins;
        }

        /// <summary>
        /// Gets the encoded parts.
        /// </summary>
        public Span<BarcodeSymbol> Symbols { get; }

        /// <summary>
        /// Gets the top left corner of each part, in pixels.
        /// </summary>
        public Span<PointF> Origins { get; }

        /// <summary>
        /// Returns the shaped lines of one part.
        /// </summary>
        /// <param name="index">The index of the part.</param>
        /// <returns>The lines.</returns>
        public Span<MeasuredLine> Lines(int index) => this.lines[this.lineStarts[index]..this.lineStarts[index + 1]];

        /// <summary>
        /// Returns the shaped blocks of one part.
        /// </summary>
        /// <param name="index">The index of the part.</param>
        /// <returns>The blocks, one for each line.</returns>
        public ReadOnlySpan<TextBlock> Blocks(int index) => this.blocks[this.lineStarts[index]..this.lineStarts[index + 1]];
    }

    /// <summary>
    /// The pass that measures each part without drawing anything.
    /// </summary>
    private readonly struct MeasurePass : ICompositePass
    {
        /// <inheritdoc/>
        public BarcodeMetrics Run(in CompositeParts parts, BarcodeOptions options, PointF origin, RectangleF advance)
        {
            Span<BarcodeSymbol> symbols = parts.Symbols;
            RectangleF bounds = RectangleF.Empty;
            for (int i = 0; i < symbols.Length; i++)
            {
                RectangleF partBounds = BarcodeEmitter.Measure(ref symbols[i], options, parts.Origins[i], parts.Lines(i), out _).Bounds;
                bounds = i == 0 ? partBounds : RectangleF.Union(bounds, partBounds);
            }

            return CompositeMetrics(origin, advance, bounds);
        }
    }

    /// <summary>
    /// The pass that draws the parts onto a canvas. The composite is one symbol, so one background fills the renderable bounds of the composite before any
    /// part draws, and each part draws with no background of its own.
    /// </summary>
    private readonly struct DrawPass : ICompositePass
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
        public BarcodeMetrics Run(in CompositeParts parts, BarcodeOptions options, PointF origin, RectangleF advance)
        {
            Span<BarcodeSymbol> symbols = parts.Symbols;

            // The ink of a part is known only when it is laid out at its origin, so the background needs the measure pass first.
            if (options.Background is not null)
            {
                BarcodeMetrics metrics = default(MeasurePass).Run(in parts, options, origin, advance);
                RectangleF renderable = metrics.RenderableBounds;
                this.canvas.Fill(options.Background, new RectanglePolygon(renderable.X, renderable.Y, renderable.Width, renderable.Height));
                for (int i = 0; i < symbols.Length; i++)
                {
                    BarcodeEmitter.Draw(this.canvas, ref symbols[i], options, parts.Origins[i], parts.Lines(i), parts.Blocks(i), null);
                }

                return metrics;
            }

            // Without a background each part draws at its origin in one pass.
            RectangleF bounds = RectangleF.Empty;
            for (int i = 0; i < symbols.Length; i++)
            {
                BarcodeMetrics metrics = BarcodeEmitter.Draw(this.canvas, ref symbols[i], options, parts.Origins[i], parts.Lines(i), parts.Blocks(i), null);
                bounds = i == 0 ? metrics.Bounds : RectangleF.Union(bounds, metrics.Bounds);
            }

            return CompositeMetrics(origin, advance, bounds);
        }
    }
}
