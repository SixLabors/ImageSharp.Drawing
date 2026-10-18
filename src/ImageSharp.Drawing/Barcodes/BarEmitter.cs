// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Measures and draws a linear or stacked <see cref="BarcodeSymbol"/>. All bars render through a single fill of one <see cref="BarcodePath"/>. Each text
/// placement draws through one text command. The measure and the draw share one layout, so the measured metrics are the metrics of what is drawn.
/// </summary>
internal static class BarEmitter
{
    /// <summary>
    /// The smallest size in points that the text of a linear or stacked symbol prints at. Section 8.1 of the ISBN Users' Manual: "The ISBN should always be
    /// printed in type large enough to be easily legible (i.e., 9-point or larger)." Section 7 of the ISMN Users' Manual gives the same floor.
    /// </summary>
    private const float MinimumTextPoints = 9F;

    /// <summary>
    /// Measures the symbol without drawing anything.
    /// </summary>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
    {
        using PooledBuffer<MeasuredLine> lineBuffer = CreateLineBuffer(ref symbol, options);

        // A TextBlock is a class, so the span holds object references and cannot come from the byte pool. A bar symbol has at most 14 text placements: an
        // ISBN has one for each of its 13 digits and one for its caption. The 16 stack slots hold all of them, and a larger count takes an array.
        InlineArray16<TextBlock> blockStorage = default;
        int lineCount = lineBuffer.Span.Length;
        Span<TextBlock> blocks = lineCount <= 16 ? ((Span<TextBlock>)blockStorage)[..lineCount] : new TextBlock[lineCount];
        return new BarLayout(ref symbol, options, origin, lineBuffer.Span, blocks).Metrics;
    }

    /// <summary>
    /// Draws the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="background">The brush that fills the renderable bounds before the bars draw, or <see langword="null"/> for none.</param>
    /// <returns>The measured layout of the symbol, the same as <see cref="Measure"/> returns.</returns>
    public static BarcodeMetrics Draw(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Brush? background)
    {
        using PooledBuffer<MeasuredLine> lineBuffer = CreateLineBuffer(ref symbol, options);

        // A TextBlock is a class, so the span holds object references and cannot come from the byte pool. A bar symbol has at most 14 text placements: an
        // ISBN has one for each of its 13 digits and one for its caption. The 16 stack slots hold all of them, and a larger count takes an array.
        InlineArray16<TextBlock> blockStorage = default;
        int lineCount = lineBuffer.Span.Length;
        Span<TextBlock> blocks = lineCount <= 16 ? ((Span<TextBlock>)blockStorage)[..lineCount] : new TextBlock[lineCount];
        BarLayout layout = new(ref symbol, options, origin, lineBuffer.Span, blocks);

        BarcodeMetrics metrics = layout.Metrics;

        // The path retains the rectangles until the canvas flushes, so it owns this array.
        RectangleF[] bars = new RectangleF[layout.BarCount(ref symbol)];
        layout.WriteBars(ref symbol, bars);

        if (background is not null)
        {
            RectangleF renderable = metrics.RenderableBounds;
            canvas.Fill(background, new RectanglePolygon(renderable.X, renderable.Y, renderable.Width, renderable.Height));
        }

        canvas.Fill(options.BarBrush, new BarcodePath(bars, layout.BarInk));
        layout.DrawText(canvas, options.BarBrush);
        return metrics;
    }

    /// <summary>
    /// Returns the x origin of a line. The line stands on the cell edges the bars actually drew on, not on the exact fractional position, so a digit stays over
    /// its own symbol character. The unrounded edges would let the text and the bars disagree by up to half a module. A centred line straddles the middle of
    /// its span, and a left aligned line puts the left edge of its ink on the left edge of its span.
    /// </summary>
    /// <param name="placement">The placement.</param>
    /// <param name="boundsLeft">The left edge of the ink of the measured line, from <see cref="TextMetrics.Bounds"/>.</param>
    /// <param name="renderableWidth">The width of the measured line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
    /// <param name="symbolLeft">The x of the first bar, in pixels.</param>
    /// <param name="moduleWidth">The module width in pixels, stretched to the drawn bar block.</param>
    /// <returns>The x origin in pixels.</returns>
    public static float TextOrigin(BarcodeTextPlacement placement, float boundsLeft, float renderableWidth, float symbolLeft, float moduleWidth)
    {
        float left = MathF.Round(symbolLeft + (placement.Left * moduleWidth));
        if (placement.Alignment == BarcodeTextAlignment.Left)
        {
            return left - boundsLeft;
        }

        float right = MathF.Round(symbolLeft + (placement.Right * moduleWidth));
        return ((left + right) * 0.5F) - (renderableWidth * 0.5F);
    }

    /// <summary>
    /// Rents the buffer that holds the measured lines of the symbol: one line for each text placement when the options carry a font, and none otherwise.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <returns>The line buffer.</returns>
    private static PooledBuffer<MeasuredLine> CreateLineBuffer(ref BarcodeSymbol symbol, BarcodeOptions options)
        => options.Font is null || symbol.Text.IsEmpty ? default : new(symbol.Text.Length);

    /// <summary>
    /// Returns the pixel offset of an edge in a row of uniform bars: the sum of the whole pixel widths of the runs before the edge.
    /// </summary>
    /// <param name="runs">The runs of the row.</param>
    /// <param name="units">The edge in run units from the start of the row.</param>
    /// <param name="runWidthInPixels">The width of one run unit in pixels.</param>
    /// <returns>The offset in pixels.</returns>
    private static float PixelOffset(ReadOnlySpan<int> runs, int units, float runWidthInPixels)
    {
        float pixels = 0;
        int position = 0;
        for (int i = 0; position < units; i++)
        {
            pixels += RunPixels(runs[i], runWidthInPixels);
            position += runs[i];
        }

        return pixels;
    }

    /// <summary>
    /// Returns the whole number of pixels a run of a symbol with uniform bars draws at. A run that is not empty draws at least one pixel.
    /// </summary>
    /// <param name="units">The run width in run units.</param>
    /// <param name="runWidthInPixels">The width of one run unit in pixels.</param>
    /// <returns>The run width in pixels.</returns>
    private static float RunPixels(int units, float runWidthInPixels)
        => units == 0 ? 0F : MathF.Max(1F, MathF.Round(units * runWidthInPixels, MidpointRounding.AwayFromZero));

    /// <summary>
    /// Resolves the font an ISBN, ISMN or ISSN caption prints in. Under <see cref="BarcodeOptions.FitCaptionToSymbolWidth"/> the size is the one that spans the
    /// bars. Otherwise the font keeps its own size. The 9 point floor wins over both, because it is the one size rule the standards state, and a caption that
    /// cannot fit at 9 point widens the drawing.
    /// </summary>
    /// <param name="caption">The caption text.</param>
    /// <param name="spanModules">The width the caption prints across, in modules.</param>
    /// <param name="moduleWidth">The width of one module in pixels.</param>
    /// <param name="font">The caption font of the options, or their font when they carry no caption font.</param>
    /// <param name="options">The options that carry the resolution and the fitting choice.</param>
    /// <returns>The font to print the caption with.</returns>
    private static Font ResolveCaptionFont(ReadOnlySpan<char> caption, float spanModules, float moduleWidth, Font font, BarcodeOptions options)
    {
        float size = font.Size;
        if (options.FitCaptionToSymbolWidth)
        {
            float width = TextMeasurer.Measure(caption, BarcodeTextOptionsFactory.Create(font, options.Dpi)).RenderableBounds.Width;
            size = font.Size * spanModules * moduleWidth / width;
        }

        size = MathF.Max(size, MinimumTextPoints);
        return size == font.Size ? font : new Font(font, size);
    }

    /// <summary>
    /// The layout of a linear or stacked symbol: where the bar block, the frame, the identification bars and each text line stand, from one origin. The
    /// constructor places everything and measures the ink, and <see cref="WriteBars"/> writes the bars for the draw, so the measure and the draw read one
    /// layout.
    /// </summary>
    private ref struct BarLayout
    {
        /// <summary>
        /// The top left corner of the symbol and its quiet zones, in pixels. The layout box of the barcode starts at the origin, so text above the symbol or to
        /// the left of it moves this corner down or right.
        /// </summary>
        private readonly PointF origin;

        /// <summary>
        /// The width of one module in pixels.
        /// </summary>
        private readonly float moduleWidth;

        /// <summary>
        /// The top quiet zone in modules, or zero when the quiet zones are not drawn.
        /// </summary>
        private readonly float topZone;

        /// <summary>
        /// The x of the first bar, in pixels.
        /// </summary>
        private readonly float symbolLeft;

        /// <summary>
        /// The width of the bar block in pixels.
        /// </summary>
        private readonly float blockWidth;

        /// <summary>
        /// Whether every run draws at a whole number of pixels of its own.
        /// </summary>
        private readonly bool wholePixelRuns;

        /// <summary>
        /// The width of one run unit in pixels.
        /// </summary>
        private readonly float runWidthInPixels;

        /// <summary>
        /// The thickness of the frame in modules, or zero for none.
        /// </summary>
        private readonly float frame;

        /// <summary>
        /// The frame thickness in whole pixels.
        /// </summary>
        private readonly float frameThickness;

        /// <summary>
        /// The top edge of the upper frame section, in pixels.
        /// </summary>
        private readonly float frameTop;

        /// <summary>
        /// The bottom edge of the upper frame section, in pixels.
        /// </summary>
        private readonly float upperBottom;

        /// <summary>
        /// The top edge of the lower frame section, in pixels.
        /// </summary>
        private readonly float lowerTop;

        /// <summary>
        /// The left edge of the frame, in pixels.
        /// </summary>
        private readonly float frameLeft;

        /// <summary>
        /// The right edge of the frame, in pixels.
        /// </summary>
        private readonly float frameRight;

        /// <summary>
        /// Whether the frame has vertical sections.
        /// </summary>
        private readonly bool sides;

        /// <summary>
        /// The upper identification bar in pixels, or an empty rectangle for none.
        /// </summary>
        private readonly RectangleF upperIdentificationBar;

        /// <summary>
        /// The lower identification bar in pixels, or an empty rectangle for none.
        /// </summary>
        private readonly RectangleF lowerIdentificationBar;

        /// <summary>
        /// The measured lines, one for each text placement, or none when the options carry no font.
        /// </summary>
        private readonly Span<MeasuredLine> lines;

        /// <summary>
        /// The prepared text block of each line, which the layout measures and the draw renders.
        /// </summary>
        private readonly Span<TextBlock> blocks;

        /// <summary>
        /// The text options of the lines.
        /// </summary>
        private LineFonts fonts;

        /// <summary>
        /// Initializes a new instance of the <see cref="BarLayout"/> struct. A linear symbol is one row. The rows share the width, the run unit and the bar
        /// rounding of the symbol.
        /// </summary>
        /// <param name="symbol">The encoded linear or stacked symbol.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="origin">The top left corner of the barcode, in pixels.</param>
        /// <param name="lines">The measured lines to write, one for each text placement, or empty when the options carry no font.</param>
        /// <param name="blocks">The text blocks to write, one for each line.</param>
        public BarLayout(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Span<MeasuredLine> lines, Span<TextBlock> blocks)
        {
            this.lines = lines;
            this.blocks = blocks;
            float moduleWidth = (float)(symbol.XDimension / BarcodeOptions.MillimetresPerInch * options.Dpi);
            this.moduleWidth = moduleWidth;

            ReadOnlySpan<int> allRuns = symbol.Runs;
            ReadOnlySpan<BarcodeTextPlacement> text = symbol.Text;

            // The bars that close the top and the bottom: a bearer bar on a linear symbol, which also has vertical sections when the quiet zones are drawn, or
            // the edge bars of a stacked symbol.
            float frame = symbol.FrameThickness;
            this.frame = frame;
            float widthInModules = symbol.WidthInModules;

            // A symbol whose bars are one width at one pitch, the postal symbologies, draws every run at a whole number of pixels of its own, so every bar
            // keeps that width and that pitch: Table 11 of the Mailmark barcode definition document requires that "they must be equally spaced", and page 12
            // of the Japan Post manual gives one bar width, "バー幅 0.6×a/10". Edges rounded from the accumulated exact position alternate between two widths.
            // The text spans stretch with the bar block so they still cover it.
            BarcodeRow first = symbol.Rows[0];
            this.wholePixelRuns = symbol.UniformBars;
            this.runWidthInPixels = symbol.RunUnit * moduleWidth;
            float blockWidth = symbol.WidthInModules * moduleWidth;
            if (this.wholePixelRuns)
            {
                blockWidth = 0;
                ReadOnlySpan<int> firstRuns = allRuns.Slice(first.RunStart, first.RunCount);
                for (int i = 0; i < firstRuns.Length; i++)
                {
                    blockWidth += RunPixels(firstRuns[i], this.runWidthInPixels);
                }
            }

            this.blockWidth = blockWidth;
            float stretch = this.wholePixelRuns ? blockWidth / (symbol.WidthInModules * moduleWidth) : 1F;
            float textModuleWidth = moduleWidth * stretch;

            // The first bar stands after the leading quiet zone and, when the frame has vertical sections, after the frame.
            float leadingOffset = 0;
            bool frameSpansQuietZones = false;
            if (options.IncludeQuietZones)
            {
                widthInModules += symbol.LeadingQuietZone + symbol.TrailingQuietZone;
                leadingOffset += symbol.LeadingQuietZone * moduleWidth;

                // Section 5.3.2.4 of the GS1 General Specifications runs the bearer bar around the quiet zones, so its vertical sections need the quiet zones
                // on the page. Without them the same clause permits the horizontal sections alone. Section 2.1 of USS-16K and of USS-49 gives the top and
                // bottom separator bars the same reach: they "extend to the ends of the quiet zones".
                frameSpansQuietZones = frame > 0;
                this.sides = frameSpansQuietZones && symbol.Kind == BarcodeSymbolKind.Linear;
                if (this.sides)
                {
                    widthInModules += frame + frame;
                    leadingOffset += frame * moduleWidth;
                }
            }

            float sideZones = (widthInModules - symbol.WidthInModules) * moduleWidth;
            float topZone = options.IncludeQuietZones ? symbol.TopQuietZone : 0F;
            float bottomZone = options.IncludeQuietZones ? symbol.BottomQuietZone : 0F;
            this.topZone = topZone;

            // The text is measured first, because its line boxes set where the symbol stands. Every text line prints at 9 point or larger, the one size floor
            // the standards state.
            Font? font = options.Font;
            this.fonts = default;
            float aboveInk = 0;
            float aboveInkTop = float.MaxValue;
            float belowInk = float.MaxValue;
            float belowInkBottom = float.MinValue;
            float belowBaseline = 0;
            float aboveEdge = float.NaN;
            float belowEdge = float.NaN;
            if (font is not null && !lines.IsEmpty)
            {
                Font digitFont = DigitFont(ref symbol, font, options, moduleWidth, stretch);
                this.fonts = new LineFonts(digitFont, options.CaptionFont ?? font, options, textModuleWidth);

                // Every line drawn in one font shares one rise, so the lines share a baseline and their glyphs sit level with each other. The rise is the
                // tallest ink any of those lines carries, which a cap height metric misses for an ascender or the overshoot of a round glyph. A symbol prints
                // one line below the bars and at most one above. The clear space is measured to the ink, so the line moves by the offset of its ink edge to
                // put that edge on the gap whatever the font or its size.
                float belowHeight = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    BarcodeTextPlacement placement = text[i];
                    ReadOnlySpan<char> characters = symbol.TextOf(in placement);
                    RichTextOptions measureOptions = this.fonts.OptionsFor(in placement, characters);

                    // The rectangles of a line are anchored at its origin, so the line is measured from its top left corner and placed on the cell later. The
                    // block shapes the line once, and the draw renders the same block at the place the layout gives it.
                    measureOptions.Origin = PointF.Empty;
                    TextBlock block = new(characters, measureOptions);
                    blocks[i] = block;
                    TextMetrics metrics = block.Measure(-1);
                    float baseline = 0;
                    if (placement.Side == BarcodeTextSide.AboveBars)
                    {
                        aboveInk = MathF.Max(aboveInk, metrics.Bounds.Bottom);
                        aboveInkTop = MathF.Min(aboveInkTop, metrics.Bounds.Top);
                        aboveEdge = float.IsNaN(aboveEdge) ? placement.TextEdge : MathF.Min(aboveEdge, placement.TextEdge);
                    }
                    else
                    {
                        belowInkBottom = MathF.Max(belowInkBottom, metrics.Bounds.Bottom);
                        belowEdge = float.IsNaN(belowEdge) ? placement.TextEdge : MathF.Max(belowEdge, placement.TextEdge);

                        // The quiet zone digits of a UPC symbol print smaller than the rest of the line. The tallest line sets where the line sits, and every
                        // line on it shares that baseline.
                        LineMetrics lineMetrics = metrics.LineMetrics[0];
                        baseline = lineMetrics.Baseline;
                        if (lineMetrics.LineHeight > belowHeight)
                        {
                            belowHeight = lineMetrics.LineHeight;
                            belowBaseline = lineMetrics.Baseline;
                        }

                        belowInk = MathF.Min(belowInk, metrics.Bounds.Top);
                    }

                    lines[i] = new MeasuredLine(metrics.Advance, metrics.Bounds, metrics.RenderableBounds.Width, baseline);
                }
            }

            // The layout box: the barcode and the line box of each line, in exact positions relative to the top left corner of the barcode. The identification
            // bars belong to the layout of the symbol, as the frame does, and they reach across the quiet zones whether or not the quiet zones are drawn.
            // Nothing in this box rounds, as nothing in the layout box of text rounds.
            float layoutLeft = 0;
            float layoutTop = 0;
            float layoutRight = sideZones + blockWidth;
            float layoutBottom = (topZone + symbol.HeightInModules + bottomZone) * moduleWidth;
            for (int i = 0; i < lines.Length; i++)
            {
                BarcodeTextPlacement placement = text[i];
                MeasuredLine line = lines[i];
                float cellLeft = leadingOffset + (placement.Left * textModuleWidth);
                float x = placement.Alignment == BarcodeTextAlignment.Left
                    ? cellLeft - line.Ink.Left
                    : ((cellLeft + leadingOffset + (placement.Right * textModuleWidth)) * 0.5F) - (line.RenderableWidth * 0.5F);
                float edge = (topZone + placement.TextEdge) * moduleWidth;
                float y = placement.Side == BarcodeTextSide.AboveBars ? edge - aboveInk : edge - belowInk + belowBaseline - line.Baseline;
                layoutLeft = MathF.Min(layoutLeft, x + line.Advance.Left);
                layoutTop = MathF.Min(layoutTop, y + line.Advance.Top);
                layoutRight = MathF.Max(layoutRight, x + line.Advance.Right);
                layoutBottom = MathF.Max(layoutBottom, y + line.Advance.Bottom);
            }

            float identificationThickness = symbol.IdentificationBarThickness * moduleWidth;
            float identificationClearance = symbol.IdentificationBarClearance * moduleWidth;
            if (identificationThickness > 0)
            {
                layoutLeft = MathF.Min(layoutLeft, leadingOffset - (symbol.LeadingQuietZone * moduleWidth));
                layoutRight = MathF.Max(layoutRight, leadingOffset + blockWidth + (symbol.TrailingQuietZone * moduleWidth));
                if (!float.IsNaN(aboveEdge))
                {
                    float bottom = ((topZone + aboveEdge) * moduleWidth) - (aboveInk - aboveInkTop) - identificationClearance;
                    layoutTop = MathF.Min(layoutTop, bottom - identificationThickness);
                }

                if (!float.IsNaN(belowEdge))
                {
                    float top = ((topZone + belowEdge) * moduleWidth) + (belowInkBottom - belowInk) + identificationClearance;
                    layoutBottom = MathF.Max(layoutBottom, top + identificationThickness);
                }
            }

            // The origin is the top left corner of the layout box, as it is for text. Text above the symbol or to the left of it moves the symbol down or right.
            PointF symbolOrigin = new(origin.X - layoutLeft, origin.Y - layoutTop);
            this.origin = symbolOrigin;
            RectangleF advance = new(origin.X, origin.Y, layoutRight - layoutLeft, layoutBottom - layoutTop);

            // The whole pixel runs land on the pixel grid only when the block starts on it, whatever the fraction of a pixel the quiet zone ends on.
            float symbolLeft = symbolOrigin.X + leadingOffset;
            if (this.wholePixelRuns)
            {
                symbolLeft = MathF.Round(symbolLeft);
            }

            this.symbolLeft = symbolLeft;

            // The edge the reader sees is what rounds onto the device pixel grid, as the bar edges do. The ink of a line below the bars starts on its text edge,
            // and the ink of a line above the bars ends on it.
            RectangleF textInk = RectangleF.Empty;
            for (int i = 0; i < lines.Length; i++)
            {
                BarcodeTextPlacement placement = text[i];
                ref MeasuredLine line = ref lines[i];
                float textEdge = MathF.Round(symbolOrigin.Y + ((topZone + placement.TextEdge) * moduleWidth));
                line.X = TextOrigin(placement, line.Ink.Left, line.RenderableWidth, symbolLeft, textModuleWidth);
                line.Y = placement.Side == BarcodeTextSide.AboveBars
                    ? textEdge - aboveInk
                    : textEdge - belowInk + belowBaseline - line.Baseline;

                RectangleF ink = new(line.X + line.Ink.X, line.Y + line.Ink.Y, line.Ink.Width, line.Ink.Height);
                textInk = i == 0 ? ink : RectangleF.Union(textInk, ink);
            }

            // Section 5.3.2.4 of the GS1 General Specifications gives the bearer bar "a constant thickness", so the sections of the frame share one pixel
            // thickness. On every side where the frame is the outermost ink its outer edge snaps outward, so no background shows past it. Where text or a quiet
            // zone follows below, the lower edge rounds as the bar edges do, which keeps the clear space. The bars that touch the frame butt against its inner
            // edges, so their height absorbs the fraction of a pixel that the snapping moves, never the frame.
            if (frame > 0)
            {
                this.frameThickness = MathF.Round(frame * moduleWidth);
                float exactTop = symbolOrigin.Y + (topZone * moduleWidth);
                float exactBottom = symbolOrigin.Y + ((topZone + symbol.HeightInModules) * moduleWidth);
                this.frameTop = topZone > 0 ? MathF.Round(exactTop) : MathF.Floor(exactTop);
                float frameBottom = bottomZone > 0 || !float.IsNaN(belowEdge) ? MathF.Round(exactBottom) : MathF.Ceiling(exactBottom);
                this.upperBottom = this.frameTop + this.frameThickness;
                this.lowerTop = frameBottom - this.frameThickness;

                // When the quiet zones are drawn the frame reaches across them, and the quiet zones absorb the fraction of a pixel between the snapped outer
                // edge and the exact one. Without them the frame ends on the bars.
                this.frameLeft = frameSpansQuietZones ? MathF.Floor(symbolOrigin.X) : MathF.Round(symbolLeft);
                this.frameRight = frameSpansQuietZones ? MathF.Ceiling(symbolOrigin.X + sideZones + blockWidth) : MathF.Round(symbolLeft + blockWidth);
            }

            // An identification bar stands its clearance beyond the far ink edge of the line on its side, and spans the symbol and its quiet zones whether or
            // not the quiet zones are drawn, because section 2.4.3 of USPS2000508 extends the bars "at least the total combined width of the barcode and the
            // minimum clear zones". The bars stand on the text edges the lines draw at, so the clearance holds to the drawn ink. The edge next to the text
            // rounds away from it, and both bars share one pixel thickness.
            if (identificationThickness > 0)
            {
                float thickness = MathF.Max(1F, MathF.Round(identificationThickness));
                float left = MathF.Floor(symbolLeft - (symbol.LeadingQuietZone * moduleWidth));
                float right = MathF.Ceiling(symbolLeft + blockWidth + (symbol.TrailingQuietZone * moduleWidth));
                if (!float.IsNaN(aboveEdge))
                {
                    float edge = MathF.Round(symbolOrigin.Y + ((topZone + aboveEdge) * moduleWidth));
                    float bottom = MathF.Floor(edge - (aboveInk - aboveInkTop) - identificationClearance);
                    this.upperIdentificationBar = RectangleF.FromLTRB(left, bottom - thickness, right, bottom);
                }

                if (!float.IsNaN(belowEdge))
                {
                    float edge = MathF.Round(symbolOrigin.Y + ((topZone + belowEdge) * moduleWidth));
                    float top = MathF.Ceiling(edge + (belowInkBottom - belowInk) + identificationClearance);
                    this.lowerIdentificationBar = RectangleF.FromLTRB(left, top, right, top + thickness);
                }
            }

            // The ink of the bars comes from the ink edges of each row, which the symbol records once: each edge rounds onto the pixel grid as the bar edge it
            // names does, so the ink is the union of the bars without a visit to each bar. Uniform bars place each run at a whole number of pixels, so their
            // edges add those pixel widths up to the edge.
            RectangleF barInk = RectangleF.Empty;
            ReadOnlySpan<BarcodeRow> rows = symbol.Rows;
            for (int r = 0; r < rows.Length; r++)
            {
                BarcodeRow row = rows[r];
                if (row.InkStart < 0)
                {
                    continue;
                }

                float left;
                float right;
                if (this.wholePixelRuns)
                {
                    ReadOnlySpan<int> rowRuns = allRuns.Slice(row.RunStart, row.RunCount);
                    left = symbolLeft + PixelOffset(rowRuns, row.InkStart, this.runWidthInPixels);
                    right = symbolLeft + PixelOffset(rowRuns, row.InkEnd, this.runWidthInPixels);
                }
                else
                {
                    left = (float)Math.Round(symbolLeft + (row.InkStart * (double)this.runWidthInPixels));
                    right = (float)Math.Round(symbolLeft + (row.InkEnd * (double)this.runWidthInPixels));
                }

                RectangleF rowInk = RectangleF.FromLTRB(left, this.RowEdge(row.Top + row.InkTop), right, this.RowEdge(row.Top + row.Height));
                barInk = barInk.IsEmpty ? rowInk : RectangleF.Union(barInk, rowInk);
            }

            // The frame is the outermost ink on the sides it closes, and an identification bar spans the symbol and its quiet zones.
            if (frame > 0)
            {
                barInk = RectangleF.Union(barInk, RectangleF.FromLTRB(this.frameLeft, this.frameTop, this.frameRight, this.lowerTop + this.frameThickness));
            }

            if (!this.upperIdentificationBar.IsEmpty)
            {
                barInk = RectangleF.Union(barInk, this.upperIdentificationBar);
            }

            if (!this.lowerIdentificationBar.IsEmpty)
            {
                barInk = RectangleF.Union(barInk, this.lowerIdentificationBar);
            }

            this.BarInk = barInk;

            // As in text measuring, the advance is relative to the origin, and the bounds and the renderable bounds are absolute.
            RectangleF bounds = textInk.IsEmpty ? barInk : RectangleF.Union(barInk, textInk);
            this.Metrics = new BarcodeMetrics(new RectangleF(0, 0, advance.Width, advance.Height), bounds, RectangleF.Union(advance, bounds));
        }

        /// <summary>
        /// Gets the ink of the bars, the frame and the identification bars, in pixels.
        /// </summary>
        public RectangleF BarInk { get; }

        /// <summary>
        /// Gets the metrics of the layout.
        /// </summary>
        public BarcodeMetrics Metrics { get; }

        /// <summary>
        /// Returns the number of bar rectangles <see cref="WriteBars"/> writes: the bars, the frame sections and the identification bars.
        /// </summary>
        /// <param name="symbol">The encoded symbol.</param>
        /// <returns>The number of rectangles.</returns>
        public readonly int BarCount(ref BarcodeSymbol symbol)
        {
            int count = symbol.BarHeights.Length;
            if (this.frame > 0)
            {
                count += this.sides ? 4 : 2;
            }

            count += this.upperIdentificationBar.IsEmpty ? 0 : 1;
            count += this.lowerIdentificationBar.IsEmpty ? 0 : 1;
            return count;
        }

        /// <summary>
        /// Writes every bar rectangle: the bars, then the frame sections, then the identification bars.
        /// </summary>
        /// <param name="symbol">The encoded symbol.</param>
        /// <param name="bars">The rectangles to write, <see cref="BarCount"/> of them.</param>
        public readonly void WriteBars(ref BarcodeSymbol symbol, Span<RectangleF> bars)
        {
            int index = 0;
            ReadOnlySpan<BarcodeRow> rows = symbol.Rows;
            ReadOnlySpan<int> allRuns = symbol.Runs;
            ReadOnlySpan<float> barHeights = symbol.BarHeights;
            ReadOnlySpan<float> barTops = symbol.BarTops;
            float frame = this.frame;
            for (int r = 0; r < rows.Length; r++)
            {
                BarcodeRow row = rows[r];
                ReadOnlySpan<int> runs = allRuns.Slice(row.RunStart, row.RunCount);
                ReadOnlySpan<float> rowHeights = barHeights.Slice(row.BarStart, row.BarCount);
                ReadOnlySpan<float> rowBarTops = barTops.Slice(row.BarStart, row.BarCount);
                float rowTop = row.Top;

                // The position adds every run before it. A double holds each product of a whole run and the run width exactly, so the sum carries no rounding
                // error from the runs before it.
                double x = this.symbolLeft;
                for (int i = 0; i < runs.Length; i++)
                {
                    double runWidth = this.wholePixelRuns ? RunPixels(runs[i], this.runWidthInPixels) : runs[i] * (double)this.runWidthInPixels;
                    if ((i & 1) == 0)
                    {
                        // Each bar edge rounds from its exact accumulated position onto the device pixel grid, so bars stay crisp for scanning at any module
                        // width and rounding cannot drift across the symbol, and the edge a row shares with the row below it rounds to one pixel row for both.
                        // Integral module widths, heights and origins round to themselves.
                        int bar = i >> 1;
                        float top = rowTop + rowBarTops[bar];
                        float bottom = top + rowHeights[bar];
                        float barTop = frame > 0 && top == frame ? this.upperBottom : this.RowEdge(top);
                        float barBottom = frame > 0 && bottom == symbol.HeightInModules - frame ? this.lowerTop : this.RowEdge(bottom);
                        bars[index++] = RectangleF.FromLTRB((float)Math.Round(x), barTop, (float)Math.Round(x + runWidth), barBottom);
                    }

                    x += runWidth;
                }
            }

            // The vertical sections of the frame run between the horizontal ones, so no two rectangles overlap.
            if (frame > 0)
            {
                float width = this.frameRight - this.frameLeft;
                float height = this.lowerTop - this.upperBottom;
                bars[index++] = new RectangleF(this.frameLeft, this.frameTop, width, this.frameThickness);
                bars[index++] = new RectangleF(this.frameLeft, this.lowerTop, width, this.frameThickness);
                if (this.sides)
                {
                    bars[index++] = new RectangleF(this.frameLeft, this.upperBottom, this.frameThickness, height);
                    bars[index++] = new RectangleF(this.frameRight - this.frameThickness, this.upperBottom, this.frameThickness, height);
                }
            }

            if (!this.upperIdentificationBar.IsEmpty)
            {
                bars[index++] = this.upperIdentificationBar;
            }

            if (!this.lowerIdentificationBar.IsEmpty)
            {
                bars[index] = this.lowerIdentificationBar;
            }
        }

        /// <summary>
        /// Draws each text line where the layout put it. Each line renders its prepared block, so it is not shaped again.
        /// </summary>
        /// <param name="canvas">The canvas to render onto.</param>
        /// <param name="brush">The brush of the text.</param>
        public readonly void DrawText(DrawingCanvas canvas, Brush brush)
        {
            for (int i = 0; i < this.lines.Length; i++)
            {
                canvas.DrawText(this.blocks[i], new PointF(this.lines[i].X, this.lines[i].Y), -1, brush, null);
            }
        }

        /// <summary>
        /// Returns the font of the digit lines. A digit prints inside its own symbol character, so its size is capped to that cell and the text cannot run into
        /// its neighbour. The 9 point floor wins over the cap where the two disagree.
        /// </summary>
        /// <param name="symbol">The encoded symbol.</param>
        /// <param name="font">The font of the options.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="moduleWidth">The width of one module in pixels.</param>
        /// <param name="stretch">The factor the text spans stretch by to cover a bar block of whole pixel runs.</param>
        /// <returns>The font of the digit lines.</returns>
        private static Font DigitFont(ref BarcodeSymbol symbol, Font font, BarcodeOptions options, float moduleWidth, float stretch)
        {
            Font digitFont = font.Size >= MinimumTextPoints ? font : new Font(font, MinimumTextPoints);
            float cap = float.MaxValue;

            // A symbology that states the printed height of its characters gives a range of ink heights. Ink height scales with the font size, so each line
            // gives the sizes that put its ink on the two limits. The caller's size is clamped into the range, and the minimum wins over the cap as the 9 point
            // floor does.
            float minimumHeight = symbol.TextHeightMinimum * moduleWidth;
            float maximumHeight = symbol.TextHeightMaximum * moduleWidth;
            float sizeAtMinimum = 0;
            float sizeAtMaximum = float.MaxValue;
            RichTextOptions capOptions = BarcodeTextOptionsFactory.Create(digitFont, options.Dpi);
            ReadOnlySpan<BarcodeTextPlacement> text = symbol.Text;
            for (int i = 0; i < text.Length; i++)
            {
                BarcodeTextPlacement placement = text[i];
                if (placement.IsCaption)
                {
                    continue;
                }

                // What runs into a neighbour is the glyph bounds, so the cap measures those. The renderable bounds are their union with the advance, and the
                // advance is space the glyph reserves rather than covers.
                FontRectangle ink = TextMeasurer.MeasureBounds(symbol.TextOf(in placement), capOptions);
                if (ink.Width <= 0)
                {
                    continue;
                }

                float cell = (placement.Right - placement.Left) * moduleWidth * stretch;
                cap = MathF.Min(cap, digitFont.Size * cell / (ink.Width * placement.FontScale));
                if (maximumHeight > 0)
                {
                    float inkHeight = ink.Height * placement.FontScale;
                    sizeAtMinimum = MathF.Max(sizeAtMinimum, digitFont.Size * minimumHeight / inkHeight);
                    sizeAtMaximum = MathF.Min(sizeAtMaximum, digitFont.Size * maximumHeight / inkHeight);
                }
            }

            float size = MathF.Min(MathF.Max(digitFont.Size, sizeAtMinimum), sizeAtMaximum);
            float capped = MathF.Max(MathF.Min(size, cap), MathF.Max(sizeAtMinimum, MinimumTextPoints));
            return capped == digitFont.Size ? digitFont : new Font(digitFont, capped);
        }

        /// <summary>
        /// Returns a horizontal edge of the bar block in pixels: an exact position in modules from the symbol top, rounded onto the device pixel grid.
        /// </summary>
        /// <param name="modules">The position in modules from the symbol top.</param>
        /// <returns>The edge in pixels.</returns>
        private readonly float RowEdge(float modules)
            => MathF.Round(this.origin.Y + ((this.topZone + modules) * this.moduleWidth));
    }

    /// <summary>
    /// What the layout measured of one line and where it put the line, so the draw draws the line without measuring it again.
    /// </summary>
    private struct MeasuredLine
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MeasuredLine"/> struct.
        /// </summary>
        /// <param name="advance">The line box of the line, from <see cref="TextMetrics.Advance"/>, relative to its origin.</param>
        /// <param name="ink">The glyph ink of the line, from <see cref="TextMetrics.Bounds"/>, relative to its origin.</param>
        /// <param name="renderableWidth">The width of the line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
        /// <param name="baseline">The baseline of a line below the bars, or 0 for a line above them.</param>
        public MeasuredLine(FontRectangle advance, FontRectangle ink, float renderableWidth, float baseline)
        {
            this.Advance = advance;
            this.Ink = ink;
            this.RenderableWidth = renderableWidth;
            this.Baseline = baseline;
        }

        /// <summary>
        /// Gets or sets the x origin of the line, in pixels. The layout sets it once the symbol has its position.
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// Gets or sets the y origin of the line, in pixels. The layout sets it once the symbol has its position.
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// Gets the width of the line, from <see cref="TextMetrics.RenderableBounds"/>.
        /// </summary>
        public float RenderableWidth { get; }

        /// <summary>
        /// Gets the line box of the line, from <see cref="TextMetrics.Advance"/>, relative to its origin.
        /// </summary>
        public FontRectangle Advance { get; }

        /// <summary>
        /// Gets the glyph ink of the line, from <see cref="TextMetrics.Bounds"/>, relative to its origin.
        /// </summary>
        public FontRectangle Ink { get; }

        /// <summary>
        /// Gets the baseline of a line below the bars, or 0 for a line above them.
        /// </summary>
        public float Baseline { get; }
    }

    /// <summary>
    /// The text options the lines of one symbol render with. A symbol resolves to at most three fonts: the digits, the scaled quiet zone digits and the
    /// caption. Each font keeps one options instance, built on first use, and the measure and the draw both read it from here.
    /// </summary>
    private struct LineFonts
    {
        /// <summary>
        /// The font the digit lines render in, after the size floor and the cell cap.
        /// </summary>
        private readonly Font digitFont;

        /// <summary>
        /// The font a caption starts from: the caption font of the options, or their font when they carry no caption font.
        /// </summary>
        private readonly Font captionFont;

        /// <summary>
        /// The sizing and painting options, which carry the resolution and the fitting choice.
        /// </summary>
        private readonly BarcodeOptions options;

        /// <summary>
        /// The width of one module in pixels, stretched to the drawn bar block.
        /// </summary>
        private readonly float moduleWidth;

        /// <summary>
        /// The options of the digit lines, or <see langword="null"/> before first use.
        /// </summary>
        private RichTextOptions? digitOptions;

        /// <summary>
        /// The options of the last scaled line, or <see langword="null"/> before first use.
        /// </summary>
        private RichTextOptions? scaledOptions;

        /// <summary>
        /// The options of the caption, or <see langword="null"/> before first use.
        /// </summary>
        private RichTextOptions? captionOptions;

        /// <summary>
        /// Initializes a new instance of the <see cref="LineFonts"/> struct.
        /// </summary>
        /// <param name="digitFont">The font the digit lines render in.</param>
        /// <param name="captionFont">The font a caption starts from.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="moduleWidth">The width of one module in pixels, stretched to the drawn bar block.</param>
        public LineFonts(Font digitFont, Font captionFont, BarcodeOptions options, float moduleWidth)
        {
            this.digitFont = digitFont;
            this.captionFont = captionFont;
            this.options = options;
            this.moduleWidth = moduleWidth;
        }

        /// <summary>
        /// Returns the text options a placement renders with. A caption renders in the caption font, resolved once for the symbol so that the measure and the
        /// draw use one size.
        /// </summary>
        /// <param name="placement">The placement to resolve for.</param>
        /// <param name="characters">The characters of the placement.</param>
        /// <returns>The text options for the placement.</returns>
        public RichTextOptions OptionsFor(in BarcodeTextPlacement placement, ReadOnlySpan<char> characters)
        {
            float dpi = this.options.Dpi;
            if (placement.IsCaption)
            {
                return this.captionOptions ??= BarcodeTextOptionsFactory.Create(
                    ResolveCaptionFont(characters, placement.Right - placement.Left, this.moduleWidth, this.captionFont, this.options),
                    dpi);
            }

            if (placement.FontScale == 1F)
            {
                return this.digitOptions ??= BarcodeTextOptionsFactory.Create(this.digitFont, dpi);
            }

            // A symbol scales at most a handful of placements, and the UPC quiet zone digits all scale by the same factor, so the scaled font and its options
            // are built once and reused while that factor holds.
            float scaledSize = this.digitFont.Size * placement.FontScale;
            if (this.scaledOptions is null || this.scaledOptions.Font.Size != scaledSize)
            {
                this.scaledOptions = BarcodeTextOptionsFactory.Create(new Font(this.digitFont, scaledSize), dpi);
            }

            return this.scaledOptions;
        }
    }
}
