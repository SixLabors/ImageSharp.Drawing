// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
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
    /// Measures the symbol without drawing anything.
    /// </summary>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
    {
        using PooledBuffer<MeasuredLine> lineBuffer = BarcodeEmitter.CreateLineBuffer(ref symbol, options);

        // A TextBlock is a class, so the span holds object references and cannot come from the byte pool. A bar symbol has at most 14 text placements: an
        // ISBN has one for each of its 13 digits and one for its caption. The 16 stack slots hold all of them.
        InlineArray16<TextBlock> blockStorage = default;
        Span<TextBlock> blocks = ((Span<TextBlock>)blockStorage)[..lineBuffer.Span.Length];
        Shape(ref symbol, options, lineBuffer.Span, blocks);
        return Measure(ref symbol, options, origin, lineBuffer.Span, out _);
    }

    /// <summary>
    /// Draws the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="background">The brush that fills the renderable bounds before the bars draw, or <see langword="null"/> for none.</param>
    /// <returns>The measured layout of the symbol, the same as <see cref="Measure(ref BarcodeSymbol, BarcodeOptions, PointF)"/> returns.</returns>
    public static BarcodeMetrics Draw(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Brush? background)
    {
        using PooledBuffer<MeasuredLine> lineBuffer = BarcodeEmitter.CreateLineBuffer(ref symbol, options);

        // A TextBlock is a class, so the span holds object references and cannot come from the byte pool. A bar symbol has at most 14 text placements: an
        // ISBN has one for each of its 13 digits and one for its caption. The 16 stack slots hold all of them.
        InlineArray16<TextBlock> blockStorage = default;
        Span<TextBlock> blocks = ((Span<TextBlock>)blockStorage)[..lineBuffer.Span.Length];
        Shape(ref symbol, options, lineBuffer.Span, blocks);
        return Draw(canvas, ref symbol, options, origin, lineBuffer.Span, blocks, background);
    }

    /// <summary>
    /// Shapes the text of the symbol: one text block and one measured line for each text placement. The layout reads the lines and the draw renders the
    /// blocks, so neither shapes the text again. A symbology that limits the size of its text and a caption that spans the bars measure their lines one more
    /// time before the blocks, to find the font size.
    /// </summary>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="lines">The measured lines to write, one for each text placement, or empty when the options carry no font.</param>
    /// <param name="blocks">The text blocks to write, one for each line.</param>
    public static void Shape(ref BarcodeSymbol symbol, BarcodeOptions options, Span<MeasuredLine> lines, Span<TextBlock> blocks)
    {
        Font? font = options.Font;
        if (font is null || lines.IsEmpty)
        {
            return;
        }

        // The text spans stretch with a bar block of whole pixel runs, so they still cover it.
        float moduleWidth = (float)(symbol.XDimension / BarcodeOptions.MillimetresPerInch * options.Dpi);
        float stretch = symbol.UniformBars ? BlockWidth(ref symbol, moduleWidth) / (symbol.WidthInModules * moduleWidth) : 1F;

        // A bar symbol has at most 14 text placements, so the scales fit inline.
        InlineArray16<float> lineScaleStorage = default;
        Span<float> lineScales = ((Span<float>)lineScaleStorage)[..lines.Length];
        lineScales.Fill(1F);
        Font digitFont = HasSizeLimits(ref symbol) ? DigitFont(ref symbol, font, options, moduleWidth, moduleWidth * stretch, lineScales) : font;
        LineFonts fonts = new(digitFont, options.CaptionFont ?? font, symbol.CaptionMinimumSize, symbol.TextWeight, options, moduleWidth * stretch);

        ReadOnlySpan<BarcodeTextPlacement> text = symbol.Text;
        for (int i = 0; i < lines.Length; i++)
        {
            BarcodeTextPlacement placement = text[i];
            ReadOnlySpan<char> characters = symbol.TextOf(in placement);
            RichTextOptions textOptions = fonts.OptionsFor(in placement, characters, lineScales[i]);

            // The rectangles of a line are anchored at its origin, so the line is measured from its top left corner and placed on the cell later. The block
            // keeps the options it was prepared with, and the draw renders the same block at the place the layout gives it.
            textOptions.Origin = PointF.Empty;
            TextBlock block = new(characters, textOptions);
            blocks[i] = block;
            TextMetrics metrics = block.Measure(-1);

            // The lines below the bars share one baseline, which the tallest of them sets, so they keep the baseline and the line height of their first line.
            float baseline = 0;
            float lineHeight = 0;
            if (placement.Side != BarcodeTextSide.AboveBars)
            {
                LineMetrics lineMetrics = metrics.LineMetrics[0];
                baseline = lineMetrics.Baseline;
                lineHeight = lineMetrics.LineHeight;
            }

            lines[i] = new MeasuredLine(metrics.Advance, metrics.Bounds, metrics.RenderableBounds, baseline, lineHeight);
        }
    }

    /// <summary>
    /// Measures the symbol from its shaped text without drawing anything.
    /// </summary>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="lines">The lines that <see cref="Shape"/> wrote. The layout writes the position of each line.</param>
    /// <param name="symbolOrigin">The top left corner of the first module of the symbol, in pixels, before it rounds onto the pixel grid.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Span<MeasuredLine> lines, out PointF symbolOrigin)
    {
        BarLayout layout = new(ref symbol, options, origin, lines);
        symbolOrigin = layout.SymbolOrigin;
        return layout.Metrics;
    }

    /// <summary>
    /// Draws the symbol from its shaped text onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="lines">The lines that <see cref="Shape"/> wrote. The layout writes the position of each line.</param>
    /// <param name="blocks">The blocks that <see cref="Shape"/> wrote, one for each line.</param>
    /// <param name="background">The brush that fills the renderable bounds before the bars draw, or <see langword="null"/> for none.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Draw(
        DrawingCanvas canvas,
        ref BarcodeSymbol symbol,
        BarcodeOptions options,
        PointF origin,
        Span<MeasuredLine> lines,
        ReadOnlySpan<TextBlock> blocks,
        Brush? background)
    {
        BarLayout layout = new(ref symbol, options, origin, lines);
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
        layout.DrawText(canvas, blocks, options.BarBrush);
        return metrics;
    }

    /// <summary>
    /// Returns the x origin of a line. The line stands on the cell edges where the bars draw, not on the exact fractional position, so a digit stays over its
    /// own symbol character. With unrounded edges, the text and the bars can disagree by up to half a module. A centred line straddles the middle of
    /// its span, a left aligned line puts the left edge of its ink on the left edge of its span, and a right aligned line puts the right edge of its ink on the
    /// right edge of its span.
    /// </summary>
    /// <param name="placement">The placement.</param>
    /// <param name="bounds">The ink of the measured line, from <see cref="TextMetrics.Bounds"/>.</param>
    /// <param name="renderableWidth">The width of the measured line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
    /// <param name="symbolLeft">The x of the first bar, in pixels.</param>
    /// <param name="moduleWidth">The module width in pixels, stretched to the drawn bar block.</param>
    /// <returns>The x origin in pixels.</returns>
    public static float TextOrigin(BarcodeTextPlacement placement, FontRectangle bounds, float renderableWidth, float symbolLeft, float moduleWidth)
    {
        float left = MathF.Round(symbolLeft + (placement.Left * moduleWidth));
        float right = MathF.Round(symbolLeft + (placement.Right * moduleWidth));
        return placement.Alignment switch
        {
            BarcodeTextAlignment.Left => left - bounds.Left,
            BarcodeTextAlignment.Right => right - bounds.Right,
            _ => ((left + right) * 0.5F) - (renderableWidth * 0.5F),
        };
    }

    /// <summary>
    /// Returns whether the symbology limits the size of its digit lines: it states a size in points or the printed height of its characters, or a line must
    /// stay within its span.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns><see langword="true"/> when the size of the digit lines is limited.</returns>
    private static bool HasSizeLimits(ref BarcodeSymbol symbol)
    {
        if (symbol.TextHeightMaximum > 0 || symbol.TextMinimumSize > 0 || symbol.TextMaximumSize > 0)
        {
            return true;
        }

        foreach (BarcodeTextPlacement placement in symbol.Text)
        {
            if (placement.WithinSpan)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the font of the digit lines of a symbology that limits their size, and the scale of each line that must fit its span. A stated size in points
    /// moves the size of the font of the options into that range, and a stated printed height of the characters moves it into that range. A line that is wider
    /// than its span at that size then prints at the scale that fits it.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="font">The font of the options.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="moduleWidth">The width of one module in pixels.</param>
    /// <param name="spanModuleWidth">The width of one module of a text span in pixels, stretched to the drawn bar block.</param>
    /// <param name="lineScales">The scale of each line, one for each text placement, to write.</param>
    /// <returns>The font of the digit lines.</returns>
    /// <exception cref="ArgumentException">A line does not fit its span at the smallest printed height of the symbology.</exception>
    private static Font DigitFont(ref BarcodeSymbol symbol, Font font, BarcodeOptions options, float moduleWidth, float spanModuleWidth, Span<float> lineScales)
    {
        // A size in points needs no measure. The other limits measure the text in the font of that size.
        float pointSize = font.Size;
        if (symbol.TextMinimumSize > 0)
        {
            pointSize = MathF.Max(pointSize, symbol.TextMinimumSize);
        }

        if (symbol.TextMaximumSize > 0)
        {
            pointSize = MathF.Min(pointSize, symbol.TextMaximumSize);
        }

        font = pointSize == font.Size ? font : new Font(font, pointSize);

        // The ink height comes from the design bounds of the glyphs, and hinting does not move a glyph on the x axis, so the measured height and widths scale
        // with the font size. Each line gives the sizes that put it on its limits, and where the limits of two lines disagree, the minimum height wins over
        // the maximum height. Only a line that a limit applies to is measured. A line of one character that only has to fit its span takes the width of its
        // glyph, which needs no shaping.
        ReadOnlySpan<BarcodeTextPlacement> text = symbol.Text;
        float minimumHeight = symbol.TextHeightMinimum * moduleWidth;
        float maximumHeight = symbol.TextHeightMaximum * moduleWidth;
        float sizeAtMinimum = 0;
        float sizeAtMaximum = float.MaxValue;
        RichTextOptions? measureOptions = null;
        for (int i = 0; i < text.Length; i++)
        {
            BarcodeTextPlacement placement = text[i];
            if (placement.IsCaption || (maximumHeight <= 0 && !placement.WithinSpan))
            {
                continue;
            }

            ReadOnlySpan<char> characters = symbol.TextOf(in placement);
            float width;
            if (maximumHeight <= 0
                && placement.Alignment != BarcodeTextAlignment.Centered
                && characters.Length == 1
                && font.TryGetGlyphs(new CodePoint(characters[0]), out Glyph? glyph)
                && glyph is Glyph characterGlyph)
            {
                width = characterGlyph.BoundingBox(GlyphLayoutMode.Horizontal, Vector2.Zero, options.Dpi).Width;
            }
            else
            {
                measureOptions ??= BarcodeTextOptionsFactory.Create(font, options.Dpi, symbol.TextWeight);
                TextMetrics metrics = TextMeasurer.Measure(characters, measureOptions);
                float inkHeight = metrics.Bounds.Height * placement.FontScale;
                if (maximumHeight > 0 && inkHeight > 0)
                {
                    sizeAtMinimum = MathF.Max(sizeAtMinimum, font.Size * minimumHeight / inkHeight);
                    sizeAtMaximum = MathF.Min(sizeAtMaximum, font.Size * maximumHeight / inkHeight);
                }

                // A line aligned on an edge fits its ink to the span, and a centred line fits its renderable bounds, which hold its ink.
                width = placement.Alignment == BarcodeTextAlignment.Centered ? metrics.RenderableBounds.Width : metrics.Bounds.Width;
            }

            // The width at the size of the options, until the size of the font is known.
            lineScales[i] = placement.WithinSpan ? width * placement.FontScale : 1F;
        }

        float size = MathF.Max(MathF.Min(font.Size, sizeAtMaximum), sizeAtMinimum);
        for (int i = 0; i < text.Length; i++)
        {
            BarcodeTextPlacement placement = text[i];
            if (!placement.WithinSpan || placement.IsCaption)
            {
                continue;
            }

            float width = lineScales[i] * size / font.Size;
            float span = (placement.Right - placement.Left) * spanModuleWidth;
            float scale = width > span ? span / width : 1F;

            // The smallest printed height and the span are both requirements, so a line that needs a smaller font than the height allows cannot be printed.
            if (maximumHeight > 0 && size * scale < sizeAtMinimum)
            {
                throw new ArgumentException("A text line does not fit its span at the smallest printed height of its symbology.");
            }

            lineScales[i] = scale;
        }

        return size == font.Size ? font : new Font(font, size);
    }

    /// <summary>
    /// Returns the width of the bar block in pixels. Uniform bars draw each run at a whole number of pixels, so their block is the sum of those runs on the
    /// first row. Other bars draw at the exact width.
    /// </summary>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="moduleWidth">The width of one module in pixels.</param>
    /// <returns>The width in pixels.</returns>
    private static float BlockWidth(ref BarcodeSymbol symbol, float moduleWidth)
    {
        if (!symbol.UniformBars)
        {
            return symbol.WidthInModules * moduleWidth;
        }

        float runWidthInPixels = symbol.RunUnit * moduleWidth;
        BarcodeRow first = symbol.Rows[0];
        ReadOnlySpan<int> runs = symbol.Runs.Slice(first.RunStart, first.RunCount);
        float blockWidth = 0;
        for (int i = 0; i < runs.Length; i++)
        {
            blockWidth += RunPixels(runs[i], runWidthInPixels);
        }

        return blockWidth;
    }

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
    /// bars. Otherwise the font keeps its own size. The minimum size of the symbology wins over both, and a caption that cannot fit at that size widens the
    /// drawing.
    /// </summary>
    /// <param name="caption">The caption text.</param>
    /// <param name="spanModules">The width the caption prints across, in modules.</param>
    /// <param name="moduleWidth">The width of one module in pixels.</param>
    /// <param name="font">The caption font of the options, or their font when they carry no caption font.</param>
    /// <param name="minimumSize">The smallest size of the caption in points, from <see cref="BarcodeSymbol.CaptionMinimumSize"/>.</param>
    /// <param name="weight">The font weight of the text, from <see cref="BarcodeSymbol.TextWeight"/>.</param>
    /// <param name="options">The options that carry the resolution and the fitting choice.</param>
    /// <returns>The font to print the caption with.</returns>
    private static Font ResolveCaptionFont(
        ReadOnlySpan<char> caption,
        float spanModules,
        float moduleWidth,
        Font font,
        float minimumSize,
        FontWeight? weight,
        BarcodeOptions options)
    {
        float size = font.Size;
        if (options.FitCaptionToSymbolWidth)
        {
            float width = TextMeasurer.Measure(caption, BarcodeTextOptionsFactory.Create(font, options.Dpi, weight)).RenderableBounds.Width;
            size = font.Size * spanModules * moduleWidth / width;
        }

        size = MathF.Max(size, minimumSize);
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
        /// The width of one module in pixels.
        /// </summary>
        private readonly float moduleWidth;

        /// <summary>
        /// The top edge of the rows in pixels, on a whole pixel.
        /// </summary>
        private readonly float blockTop;

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
        /// The number of run units in one module of runs that are whole modules with corrections, or 0, from <see cref="BarcodeSymbol.CorrectedModuleUnits"/>.
        /// </summary>
        private readonly int correctedModuleUnits;

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
        /// Initializes a new instance of the <see cref="BarLayout"/> struct. A linear symbol is one row. The rows share the width, the run unit and the bar
        /// rounding of the symbol.
        /// </summary>
        /// <param name="symbol">The encoded linear or stacked symbol.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="origin">The top left corner of the barcode, in pixels.</param>
        /// <param name="lines">The shaped lines, one for each text placement, or empty when the options carry no font. The layout writes their origins.</param>
        public BarLayout(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Span<MeasuredLine> lines)
        {
            this.lines = lines;
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
            this.wholePixelRuns = symbol.UniformBars;
            this.runWidthInPixels = symbol.RunUnit * moduleWidth;
            this.correctedModuleUnits = symbol.CorrectedModuleUnits;
            float blockWidth = BlockWidth(ref symbol, moduleWidth);
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

            // Every line drawn in one font shares one rise, so the lines share a baseline and their glyphs sit level with each other. The rise is the
            // tallest ink any of those lines carries, which a cap height metric misses for an ascender or the overshoot of a round glyph. A symbol prints one
            // line below the bars and at most one above. The clear space is measured to the ink, so the line moves by the offset of its ink edge to put that
            // edge on the gap whatever the font or its size.
            float aboveInk = 0;
            float aboveInkTop = float.MaxValue;
            float belowInk = float.MaxValue;
            float belowInkBottom = float.MinValue;
            float belowBaseline = 0;
            float belowHeight = 0;
            float aboveEdge = float.NaN;
            float belowEdge = float.NaN;
            for (int i = 0; i < lines.Length; i++)
            {
                BarcodeTextPlacement placement = text[i];
                MeasuredLine line = lines[i];
                if (placement.Side == BarcodeTextSide.AboveBars)
                {
                    aboveInk = MathF.Max(aboveInk, line.Bounds.Bottom);
                    aboveInkTop = MathF.Min(aboveInkTop, line.Bounds.Top);
                    aboveEdge = float.IsNaN(aboveEdge) ? placement.TextEdge : MathF.Min(aboveEdge, placement.TextEdge);
                }
                else
                {
                    belowInkBottom = MathF.Max(belowInkBottom, line.Bounds.Bottom);
                    belowEdge = float.IsNaN(belowEdge) ? placement.TextEdge : MathF.Max(belowEdge, placement.TextEdge);

                    // The quiet zone digits of a UPC symbol print smaller than the rest of the line. The tallest line sets where the line sits, and every line
                    // on it shares that baseline.
                    if (line.LineHeight > belowHeight)
                    {
                        belowHeight = line.LineHeight;
                        belowBaseline = line.Baseline;
                    }

                    belowInk = MathF.Min(belowInk, line.Bounds.Top);
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
                float cellRight = leadingOffset + (placement.Right * textModuleWidth);
                float x = placement.Alignment switch
                {
                    BarcodeTextAlignment.Left => cellLeft - line.Bounds.Left,
                    BarcodeTextAlignment.Right => cellRight - line.Bounds.Right,
                    _ => ((cellLeft + cellRight) * 0.5F) - (line.RenderableBounds.Width * 0.5F),
                };

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

            // The origin is the top left corner of the layout box, as it is for text. Text above the symbol or to the left of it moves the symbol down or
            // right.
            PointF symbolOrigin = new(origin.X - layoutLeft, origin.Y - layoutTop);
            RectangleF advance = new(origin.X, origin.Y, layoutRight - layoutLeft, layoutBottom - layoutTop);

            // The bar block starts on a whole pixel, whatever the fraction of a pixel the origin and the quiet zone end on. Each edge then rounds from a whole
            // pixel, so a module of a whole number of pixels draws at exactly that number wherever the symbol stands. From a start on half a pixel, edges round
            // to the even pixel and alternate, and a bar of three pixels draws as four and then two.
            this.SymbolOrigin = new PointF(symbolOrigin.X + leadingOffset, symbolOrigin.Y + (topZone * moduleWidth));
            float symbolLeft = MathF.Round(this.SymbolOrigin.X);
            this.symbolLeft = symbolLeft;
            this.blockTop = MathF.Round(this.SymbolOrigin.Y);

            // The edge the reader sees is what rounds onto the device pixel grid, as the bar edges do. The ink of a line below the bars starts on its text
            // edge, and the ink of a line above the bars ends on it.
            RectangleF textInk = RectangleF.Empty;
            for (int i = 0; i < lines.Length; i++)
            {
                BarcodeTextPlacement placement = text[i];
                ref MeasuredLine line = ref lines[i];
                float textEdge = MathF.Round(symbolOrigin.Y + ((topZone + placement.TextEdge) * moduleWidth));
                float x = TextOrigin(placement, line.Bounds, line.RenderableBounds.Width, symbolLeft, textModuleWidth);
                float y = placement.Side == BarcodeTextSide.AboveBars
                    ? textEdge - aboveInk
                    : textEdge - belowInk + belowBaseline - line.Baseline;

                line.Origin = new Vector2(x, y);
                RectangleF ink = new(x + line.Bounds.X, y + line.Bounds.Y, line.Bounds.Width, line.Bounds.Height);
                textInk = i == 0 ? ink : RectangleF.Union(textInk, ink);
            }

            // Section 5.3.2.4 of the GS1 General Specifications gives the bearer bar "a constant thickness", so the sections of the frame share one pixel
            // thickness. On every side where the frame is the outermost ink its outer edge snaps outward, so no background shows past it. Where text or a quiet
            // zone follows below, the lower edge rounds as the bar edges do, which keeps the clear space. The bars that touch the frame butt against its inner
            // edges, so their height absorbs the fraction of a pixel that the snapping moves, never the frame.
            if (frame > 0)
            {
                this.frameThickness = MathF.Round(frame * moduleWidth);
                float exactTop = this.blockTop;
                float exactBottom = this.blockTop + (symbol.HeightInModules * moduleWidth);
                this.frameTop = topZone > 0 ? MathF.Round(exactTop) : MathF.Floor(exactTop);
                float frameBottom = bottomZone > 0 || !float.IsNaN(belowEdge) ? MathF.Round(exactBottom) : MathF.Ceiling(exactBottom);
                this.upperBottom = this.frameTop + this.frameThickness;
                this.lowerTop = frameBottom - this.frameThickness;

                // When the quiet zones are drawn the frame reaches across them, and its outer edges snap outward from the exact ones, so the frame fills
                // the measured area. The quiet zones absorb the fraction of a pixel between the snapped outer edge and the exact one. The vertical sections
                // also stand at least the quiet zone and their thickness away from the bar block, which starts and ends on whole pixels. The thickness
                // rounds on its own, so without that limit a quiet zone can lose a pixel under its minimum. A quiet zone is a minimum, so a quiet zone of
                // a fraction of a pixel rounds up. Without the quiet zones the frame ends on the bars.
                if (this.sides)
                {
                    float leadingZone = (float)Math.Ceiling(symbol.LeadingQuietZone * (double)moduleWidth);
                    float trailingZone = (float)Math.Ceiling(symbol.TrailingQuietZone * (double)moduleWidth);
                    this.frameLeft = MathF.Min(MathF.Floor(symbolOrigin.X), symbolLeft - leadingZone - this.frameThickness);
                    float blockRight = symbolLeft + blockWidth;
                    this.frameRight = MathF.Max(MathF.Ceiling(symbolOrigin.X + sideZones + blockWidth), blockRight + trailingZone + this.frameThickness);
                }
                else
                {
                    this.frameLeft = frameSpansQuietZones ? MathF.Floor(symbolOrigin.X) : MathF.Round(symbolLeft);
                    this.frameRight = frameSpansQuietZones ? MathF.Ceiling(symbolOrigin.X + sideZones + blockWidth) : MathF.Round(symbolLeft + blockWidth);
                }
            }

            // An identification bar stands its clearance beyond the far ink edge of the line on its side, and spans the symbol and its quiet zones whether or
            // not the quiet zones are drawn, because section 2.4.3 of USPS2000508 extends the bars "at least the total combined width of the barcode and the
            // minimum clear zones". Section 4.9.2 of Publication 199: "Human-readable numbers cannot extend beyond the length of the identification bars", and
            // section 4.9.4 lets the bars extend to "the entire width of the label", so the bars also span the ink of the text. The bars stand on the text
            // edges the lines draw at, so the clearance holds to the drawn ink. The edge next to the text rounds away from it, and both bars share one pixel
            // thickness.
            if (identificationThickness > 0)
            {
                float thickness = MathF.Max(1F, MathF.Round(identificationThickness));
                float left = MathF.Floor(MathF.Min(symbolLeft - (symbol.LeadingQuietZone * moduleWidth), textInk.Left));
                float right = MathF.Ceiling(MathF.Max(symbolLeft + blockWidth + (symbol.TrailingQuietZone * moduleWidth), textInk.Right));
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
                else if (this.correctedModuleUnits > 0)
                {
                    left = this.CorrectedEdge(row.InkStart);
                    right = this.CorrectedEdge(row.InkEnd);
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
        /// Gets the top left corner of the first module of the symbol, in pixels, before it rounds onto the pixel grid.
        /// </summary>
        public PointF SymbolOrigin { get; }

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
        /// Returns the pixel position of an edge of runs that are whole modules with corrections. The module edge rounds onto the pixel grid as an edge without
        /// a correction does, and the correction rounds to its own whole number of pixels, so the edges are those of the runs without corrections until a
        /// correction is half a pixel or more. Rounded together, a correction of a fraction of a pixel can move one edge of a bar a whole pixel and its
        /// neighbour not, and change a bar of two pixels by half its width. The corrections of a character sum to zero and are less than half a module, so the
        /// nearest module to an edge is the module edge it corrects.
        /// </summary>
        /// <param name="units">The edge in run units from the start of the row.</param>
        /// <returns>The edge in pixels.</returns>
        private readonly float CorrectedEdge(int units)
        {
            int unitsPerModule = this.correctedModuleUnits;
            int modules = (units + (unitsPerModule >> 1)) / unitsPerModule;
            int correction = units - (modules * unitsPerModule);
            double edge = Math.Round(this.symbolLeft + (modules * (double)this.moduleWidth));
            return (float)(edge + Math.Round(correction * (double)this.runWidthInPixels));
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
            bool corrected = this.correctedModuleUnits > 0;
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
                int units = 0;
                for (int i = 0; i < runs.Length; i++)
                {
                    double runWidth = this.wholePixelRuns ? RunPixels(runs[i], this.runWidthInPixels) : runs[i] * (double)this.runWidthInPixels;
                    if ((i & 1) == 0)
                    {
                        // Each bar edge rounds from its exact accumulated position onto the device pixel grid, so bars stay crisp for scanning at any module
                        // width and rounding cannot drift across the symbol, and the edge a row shares with the row below it rounds to one pixel row for both.
                        // Integral module widths, heights and origins round to themselves. Corrected runs round their module edges and corrections apart.
                        int bar = i >> 1;
                        float top = rowTop + rowBarTops[bar];
                        float bottom = top + rowHeights[bar];
                        float barTop = frame > 0 && top == frame ? this.upperBottom : this.RowEdge(top);
                        float barBottom = frame > 0 && bottom == symbol.HeightInModules - frame ? this.lowerTop : this.RowEdge(bottom);
                        float left = corrected ? this.CorrectedEdge(units) : (float)Math.Round(x);
                        float right = corrected ? this.CorrectedEdge(units + runs[i]) : (float)Math.Round(x + runWidth);
                        bars[index++] = RectangleF.FromLTRB(left, barTop, right, barBottom);
                    }

                    x += runWidth;
                    units += runs[i];
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
        /// Draws each text line where the layout put it. Each line renders its shaped block, so it is not shaped again.
        /// </summary>
        /// <param name="canvas">The canvas to render onto.</param>
        /// <param name="blocks">The shaped blocks, one for each line.</param>
        /// <param name="brush">The brush of the text.</param>
        public readonly void DrawText(DrawingCanvas canvas, ReadOnlySpan<TextBlock> blocks, Brush brush)
        {
            for (int i = 0; i < this.lines.Length; i++)
            {
                canvas.DrawText(blocks[i], this.lines[i].Origin, -1, brush, null);
            }
        }

        /// <summary>
        /// Returns a horizontal edge of the bar block in pixels: an exact position in modules from the symbol top, rounded onto the device pixel grid.
        /// </summary>
        /// <param name="modules">The position in modules from the symbol top.</param>
        /// <returns>The edge in pixels.</returns>
        private readonly float RowEdge(float modules)
            => MathF.Round(this.blockTop + (modules * this.moduleWidth));
    }

    /// <summary>
    /// The text options the lines of one symbol render with: the digit options, the caption options and the options of the scaled lines. The digit and the
    /// caption options are built on first use and kept. The scaled options are kept while the next scaled line has the same font size.
    /// <see cref="Shape"/> reads the options of each line from here.
    /// </summary>
    private struct LineFonts
    {
        /// <summary>
        /// The font the digit lines render in, from <see cref="DigitFont"/>.
        /// </summary>
        private readonly Font digitFont;

        /// <summary>
        /// The font a caption starts from: the caption font of the options, or their font when they carry no caption font.
        /// </summary>
        private readonly Font captionFont;

        /// <summary>
        /// The smallest size of the caption in points, or 0 when the symbology states none.
        /// </summary>
        private readonly float captionMinimumSize;

        /// <summary>
        /// The font weight of the text, or <see langword="null"/> for the weight of the fonts.
        /// </summary>
        private readonly FontWeight? weight;

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
        /// <param name="captionMinimumSize">The smallest size of the caption in points, or 0 when the symbology states none.</param>
        /// <param name="weight">The font weight of the text, from <see cref="BarcodeSymbol.TextWeight"/>.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="moduleWidth">The width of one module in pixels, stretched to the drawn bar block.</param>
        public LineFonts(Font digitFont, Font captionFont, float captionMinimumSize, FontWeight? weight, BarcodeOptions options, float moduleWidth)
        {
            this.digitFont = digitFont;
            this.captionFont = captionFont;
            this.captionMinimumSize = captionMinimumSize;
            this.weight = weight;
            this.options = options;
            this.moduleWidth = moduleWidth;
        }

        /// <summary>
        /// Returns the text options a placement renders with. A caption renders in the caption font, resolved once for the symbol so that the measure and the
        /// draw use one size.
        /// </summary>
        /// <param name="placement">The placement to resolve for.</param>
        /// <param name="characters">The characters of the placement.</param>
        /// <param name="lineScale">The scale of a line that must fit its span, or 1.</param>
        /// <returns>The text options for the placement.</returns>
        public RichTextOptions OptionsFor(in BarcodeTextPlacement placement, ReadOnlySpan<char> characters, float lineScale)
        {
            float dpi = this.options.Dpi;
            if (placement.IsCaption)
            {
                // The caption font resolves once for the symbol, so that the measure and the draw use one size.
                if (this.captionOptions is null)
                {
                    Font caption = ResolveCaptionFont(
                        characters,
                        placement.Right - placement.Left,
                        this.moduleWidth,
                        this.captionFont,
                        this.captionMinimumSize,
                        this.weight,
                        this.options);

                    this.captionOptions = BarcodeTextOptionsFactory.Create(caption, dpi, this.weight);
                }

                return this.captionOptions;
            }

            float scale = placement.FontScale * lineScale;
            if (scale == 1F)
            {
                return this.digitOptions ??= BarcodeTextOptionsFactory.Create(this.digitFont, dpi, this.weight);
            }

            // A symbol scales at most a handful of placements, so the scaled font and its options are built once and reused while the factor holds.
            float scaledSize = this.digitFont.Size * scale;
            if (this.scaledOptions is null || this.scaledOptions.Font.Size != scaledSize)
            {
                this.scaledOptions = BarcodeTextOptionsFactory.Create(new Font(this.digitFont, scaledSize), dpi, this.weight);
            }

            return this.scaledOptions;
        }
    }
}
