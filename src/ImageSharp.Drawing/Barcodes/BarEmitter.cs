// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Helpers;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Converts a linear or stacked <see cref="BarcodeSymbol"/> into canvas commands. All bars render through a single fill of one <see cref="BarcodePath"/>. Each
/// text placement draws through one text command.
/// </summary>
internal static class BarEmitter
{
    /// <summary>
    /// The smallest size in points that the text of a linear or stacked symbol prints at. Section 8.1 of the ISBN Users' Manual: "The ISBN should always be
    /// printed in type large enough to be easily legible (i.e., 9-point or larger)." Section 7 of the ISMN Users' Manual gives the same floor.
    /// </summary>
    private const float MinimumTextPoints = 9F;

    /// <summary>
    /// Renders the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of everything the call draws, including any text that overhangs the symbol, in pixels.</param>
    public static void Emit(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(canvas, ref symbol, options, origin);

    /// <summary>
    /// Measures the area the symbol covers without drawing anything, so a caller can size the image before it draws and no glyph falls outside it.
    /// </summary>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner the symbol would draw from, in pixels.</param>
    /// <returns>The area the symbol covers, including any text that overhangs the bars.</returns>
    public static RectangleF Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(null, ref symbol, options, origin);

    /// <summary>
    /// Lays the symbol out and, when a canvas is supplied, draws it. Both callers share one pass, so the measured area and the drawn area cannot drift apart. A
    /// linear symbol is one row. The rows share the width, the run unit and the bar rounding of the symbol.
    /// </summary>
    /// <param name="canvas">The canvas to render onto, or <see langword="null"/> to measure only.</param>
    /// <param name="symbol">The encoded linear or stacked symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of everything the call draws, in pixels.</param>
    /// <returns>The area the symbol covers, including any text that overhangs the bars.</returns>
    private static RectangleF Layout(DrawingCanvas? canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
    {
        float moduleWidth = (float)(symbol.XDimension / BarcodeOptions.MillimetresPerInch * options.Dpi);

        ReadOnlySpan<BarcodeRow> rows = symbol.Rows;
        ReadOnlySpan<int> allRuns = symbol.Runs;
        ReadOnlySpan<float> barHeights = symbol.BarHeights;
        ReadOnlySpan<float> barTops = symbol.BarTops;
        ReadOnlySpan<BarcodeTextPlacement> text = symbol.Text;

        // The bars that close the top and the bottom: a bearer bar on a linear symbol, which also has vertical sections when the quiet zones are drawn, or the
        // edge bars of a stacked symbol.
        float frame = symbol.FrameThickness;
        bool frameSides = symbol.Kind == BarcodeSymbolKind.Linear;
        float widthInModules = symbol.WidthInModules;

        // A symbol whose bars are one width at one pitch, the postal symbologies, draws every run at a whole number of pixels of its own, so every bar keeps
        // that width and that pitch: Table 11 of the Mailmark barcode definition document requires that "they must be equally spaced", and page 12 of the Japan
        // Post manual gives one bar width, "バー幅 0.6×a/10". Edges rounded from the accumulated exact position alternate between two widths. The text spans
        // stretch with the bar block so they still cover it.
        BarcodeRow first = rows[0];
        bool wholePixelRuns = symbol.UniformBars;
        float runWidthInPixels = symbol.RunUnit * moduleWidth;
        float blockWidth = symbol.WidthInModules * moduleWidth;
        if (wholePixelRuns)
        {
            blockWidth = 0;
            ReadOnlySpan<int> firstRuns = allRuns.Slice(first.RunStart, first.RunCount);
            for (int i = 0; i < firstRuns.Length; i++)
            {
                blockWidth += RunPixels(firstRuns[i], runWidthInPixels);
            }
        }

        float stretch = wholePixelRuns ? blockWidth / (symbol.WidthInModules * moduleWidth) : 1F;
        float textModuleWidth = moduleWidth * stretch;
        float symbolLeft = origin.X;
        float frameLeft = origin.X;
        bool frameSpansQuietZones = false;
        bool sides = false;
        if (options.IncludeQuietZones)
        {
            widthInModules += symbol.LeadingQuietZone + symbol.TrailingQuietZone;
            symbolLeft += symbol.LeadingQuietZone * moduleWidth;

            // Section 5.3.2.4 of the GS1 General Specifications runs the bearer bar around the quiet zones, so its vertical sections need the quiet zones on
            // the page. Without them the same clause permits the horizontal sections alone. Section 2.1 of USS-16K and of USS-49 gives the top and bottom
            // separator bars the same reach: they "extend to the ends of the quiet zones".
            frameSpansQuietZones = frame > 0;
            sides = frameSpansQuietZones && frameSides;
            if (sides)
            {
                widthInModules += frame + frame;
                symbolLeft += frame * moduleWidth;
            }
        }

        // The whole pixel runs land on the pixel grid only when the block starts on it, whatever the fraction of a pixel the quiet zone ends on.
        if (wholePixelRuns)
        {
            symbolLeft = MathF.Round(symbolLeft);
        }

        // The drawn area grows to hold the human readable interpretation in both axes: text hangs below the bars the way the nominal ISO/IEC 15420 symbol
        // reserves its text region, and a caption wider than the symbol, an ISBN line for example, widens the background.
        float backgroundLeft = origin.X;
        float backgroundRight = origin.X + ((widthInModules - symbol.WidthInModules) * moduleWidth) + blockWidth;

        // Every text line prints at 9 point or larger, the one size floor the standards state.
        Font? digitFont = options.Font is null || options.Font.Size >= MinimumTextPoints
            ? options.Font
            : new Font(options.Font, MinimumTextPoints);

        // A digit prints inside its own symbol character, so its size is capped to that cell and the text cannot run into its neighbour. The floor wins over
        // the cap where the two disagree.
        if (digitFont is not null)
        {
            float cap = float.MaxValue;

            // A symbology that states the printed height of its characters gives a range of ink heights. Ink height scales with the font size, so each line
            // gives the sizes that put its ink on the two limits. The caller's size is clamped into the range, and the minimum wins over the cap as the 9 point
            // floor does.
            float minimumHeight = symbol.TextHeightMinimum * moduleWidth;
            float maximumHeight = symbol.TextHeightMaximum * moduleWidth;
            float sizeAtMinimum = 0;
            float sizeAtMaximum = float.MaxValue;
            RichTextOptions capOptions = BarcodeTextOptionsFactory.Create(digitFont, options.Dpi);
            for (int i = 0; i < text.Length; i++)
            {
                BarcodeTextPlacement capPlacement = text[i];
                if (capPlacement.IsCaption)
                {
                    continue;
                }

                // What runs into a neighbour is the glyph bounds, so the cap measures those. The renderable bounds are their union with the advance, and the
                // advance is space the glyph reserves rather than covers.
                FontRectangle ink = TextMeasurer.MeasureBounds(symbol.TextOf(in capPlacement), capOptions);
                if (ink.Width <= 0)
                {
                    continue;
                }

                float cell = (capPlacement.Right - capPlacement.Left) * moduleWidth * stretch;
                cap = MathF.Min(cap, digitFont.Size * cell / (ink.Width * capPlacement.FontScale));
                if (maximumHeight > 0)
                {
                    float inkHeight = ink.Height * capPlacement.FontScale;
                    sizeAtMinimum = MathF.Max(sizeAtMinimum, digitFont.Size * minimumHeight / inkHeight);
                    sizeAtMaximum = MathF.Min(sizeAtMaximum, digitFont.Size * maximumHeight / inkHeight);
                }
            }

            float size = MathF.Min(MathF.Max(digitFont.Size, sizeAtMinimum), sizeAtMaximum);
            float capped = MathF.Max(MathF.Min(size, cap), MathF.Max(sizeAtMinimum, MinimumTextPoints));
            if (capped != digitFont.Size)
            {
                digitFont = new Font(digitFont, capped);
            }
        }

        // One rule places every line: its ink starts on the edge it names and takes one line height. A line above the bars pushes the whole bar block down by
        // the room it needs, and the band holds that room, measured from the fonts. A quiet zone above or below the symbol reserves room the same way, and
        // where a line also stands on that side the larger of the two holds, because the encoder places such a line beyond the quiet zone.
        float topBand = 0;
        float bottomInModules = symbol.HeightInModules;
        if (options.IncludeQuietZones)
        {
            topBand = symbol.TopQuietZone;
            bottomInModules += symbol.BottomQuietZone;
        }

        // The measuring pass and the drawing pass share the fonts and their options, so the measured rectangle of a line is the one it draws into.
        LineFonts fonts = digitFont is null ? default : new LineFonts(digitFont, options, textModuleWidth);

        // The drawing pass places each line from what the measuring pass measured.
        using PooledBuffer<MeasuredLine> lineBuffer = digitFont is null || text.IsEmpty ? default : new(text.Length);
        Span<MeasuredLine> lines = lineBuffer.Span;

        // Every line drawn in one font shares one rise, so the lines share a baseline and their glyphs sit level with each other. The rise is the tallest ink
        // any of those lines carries, which a cap height metric misses for an ascender or the overshoot of a round glyph. A symbol prints one line below the
        // bars and at most one above. Each line takes one line height, and its ink sits somewhere inside that. The clear space is measured to the ink, so the
        // line moves by the offset of its ink edge to put that edge on the gap whatever the font or its size.
        float belowHeight = 0;
        float belowInk = float.MaxValue;
        float belowBaseline = 0;
        float aboveInk = 0;

        // The far ink edges of the lines, the top of the line above the bars and the bottom of the line below them, which the identification bars stand clear
        // of. The edges are in modules from the symbol top, at the text edge the lines round to.
        float aboveInkTop = float.MaxValue;
        float belowInkBottom = float.MinValue;
        float aboveEdge = float.NaN;
        float belowEdge = float.NaN;
        if (digitFont is not null)
        {
            for (int i = 0; i < text.Length; i++)
            {
                BarcodeTextPlacement placement = text[i];
                ReadOnlySpan<char> characters = symbol.TextOf(in placement);
                RichTextOptions measureOptions = fonts.OptionsFor(in placement, characters);

                // The rectangle is the union of the glyph bounds and the advance, and both are anchored at the origin, so the line is measured from its top
                // left corner and placed on the cell here.
                measureOptions.Origin = PointF.Empty;

                TextMetrics metrics = TextMeasurer.Measure(characters, measureOptions);
                float inkLeft = metrics.Bounds.Left;
                float renderableWidth = metrics.RenderableBounds.Width;
                float textLeft = TextOrigin(placement, inkLeft, renderableWidth, symbolLeft, textModuleWidth);
                backgroundLeft = MathF.Min(backgroundLeft, textLeft);
                backgroundRight = MathF.Max(backgroundRight, textLeft + renderableWidth);

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

                    // The quiet zone digits of a UPC symbol print smaller than the rest of the line. The tallest line sets where the line sits, and every line
                    // on it shares that baseline.
                    LineMetrics lineMetrics = metrics.LineMetrics[0];
                    baseline = lineMetrics.Baseline;
                    if (lineMetrics.LineHeight > belowHeight)
                    {
                        belowHeight = lineMetrics.LineHeight;
                        belowBaseline = lineMetrics.Baseline;
                    }

                    belowInk = MathF.Min(belowInk, metrics.Bounds.Top);
                }

                lines[i] = new MeasuredLine(inkLeft, renderableWidth, baseline);
            }

            // Section 5.2.5 of the GS1 General Specifications measures the clear space to the ink of the line. The room a line needs is therefore the rise its
            // font draws with, known once every line of that font has been measured. Rounding is monotonic, so the line with the outermost text edge on each
            // side sets the room for all lines on that side.
            if (!float.IsNaN(aboveEdge))
            {
                topBand = MathF.Max(topBand, (aboveInk / moduleWidth) - aboveEdge);
            }

            if (!float.IsNaN(belowEdge))
            {
                bottomInModules = MathF.Max(bottomInModules, belowEdge + ((belowHeight - belowInk) / moduleWidth));
            }
        }

        // An identification bar stands its clearance beyond the far ink edge of the line on its side, and spans the symbol and its quiet zones whether or not
        // the quiet zones are drawn, because section 2.4.3 of USPS2000508 extends the bars "at least the total combined width of the barcode and the minimum
        // clear zones". The bar edges round away from the text, and the text edge itself rounds by up to half a pixel, so each band reserves two pixels more
        // than the exact room.
        float identificationThickness = 0;
        float identificationClearance = symbol.IdentificationBarClearance * moduleWidth;
        bool upperIdentificationBar = false;
        bool lowerIdentificationBar = false;
        if (symbol.IdentificationBarThickness > 0)
        {
            identificationThickness = MathF.Max(1F, MathF.Round(symbol.IdentificationBarThickness * moduleWidth));
            float reserve = (identificationClearance + identificationThickness + 2F) / moduleWidth;
            upperIdentificationBar = !float.IsNaN(aboveEdge);
            lowerIdentificationBar = !float.IsNaN(belowEdge);
            if (upperIdentificationBar)
            {
                topBand = MathF.Max(topBand, ((aboveInk - aboveInkTop) / moduleWidth) - aboveEdge + reserve);
            }

            if (lowerIdentificationBar)
            {
                bottomInModules = MathF.Max(bottomInModules, belowEdge + ((belowInkBottom - belowInk) / moduleWidth) + reserve);
            }

            float barsStart = symbolLeft - (symbol.LeadingQuietZone * moduleWidth);
            backgroundLeft = MathF.Min(backgroundLeft, barsStart);
            backgroundRight = MathF.Max(backgroundRight, barsStart + ((symbol.LeadingQuietZone + symbol.TrailingQuietZone) * moduleWidth) + blockWidth);
        }

        // A baseline rounds onto the device pixel grid, which lifts its ink by up to half a pixel. The band starts the bar block on a whole pixel so that lift
        // cannot carry ink above the reserved room.
        topBand = MathF.Ceiling(topBand * moduleWidth) / moduleWidth;

        float heightInPixels = (topBand + bottomInModules) * moduleWidth;

        // The origin is the top left corner of everything this call draws. When text overhangs the symbol on the left (an ISBN caption wider than the bars),
        // the whole rendering shifts right by a whole number of pixels so the leftmost element starts at the origin without moving the bars off the pixel grid.
        // Nothing renders above the origin because the band already holds every line above the bars.
        float shift = MathF.Ceiling(origin.X - backgroundLeft);
        if (shift > 0)
        {
            symbolLeft += shift;
            frameLeft += shift;
            backgroundLeft += shift;
            backgroundRight += shift;
        }

        // Every bar edge rounds onto the pixel grid, so the bounds snap outward on both axes to contain the rounded edges.
        float backgroundTop = MathF.Floor(origin.Y);
        RectangleF bounds = new(
            MathF.Floor(backgroundLeft),
            backgroundTop,
            MathF.Ceiling(backgroundRight) - MathF.Floor(backgroundLeft),
            MathF.Ceiling(origin.Y + heightInPixels) - backgroundTop);

        if (canvas is null)
        {
            return bounds;
        }

        if (options.Background is not null)
        {
            // The background is a coverage area, not symbol geometry, so it takes the measured bounds: those snap outward to whole pixels, which keeps the
            // edges crisp and stops a fractionally measured text extent leaving a seam.
            canvas.Fill(options.Background, new RectanglePolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        int barCount = barHeights.Length;
        int frameCount = frame > 0 ? (sides ? 4 : 2) : 0;
        int identificationCount = (upperIdentificationBar ? 1 : 0) + (lowerIdentificationBar ? 1 : 0);

        // The path retains the rectangles until the canvas flushes, so it owns this array.
        RectangleF[] bars = new RectangleF[barCount + frameCount + identificationCount];
        float barsLeft = float.MaxValue;
        float barsTop = float.MaxValue;
        float barsRight = float.MinValue;
        float barsBottom = float.MinValue;

        // Section 5.3.2.4 of the GS1 General Specifications gives the bearer bar "a constant thickness", so the sections of the frame share one pixel
        // thickness. On every side where the frame is the outermost ink its outer edge snaps outward with the measured bounds, so no background shows past it.
        // Where text follows below, the lower edge rounds as the bar edges do, which keeps the clear space. The bars that touch the frame butt against its
        // inner edges, so their height absorbs the fraction of a pixel that the snapping moves, never the frame.
        float thickness = 0;
        float frameTop = 0;
        float frameBottom = 0;
        float upperBottom = 0;
        float lowerTop = 0;
        if (frame > 0)
        {
            thickness = MathF.Round(frame * moduleWidth);
            float exactTop = origin.Y + (topBand * moduleWidth);
            float exactBottom = origin.Y + ((topBand + symbol.HeightInModules) * moduleWidth);
            frameTop = topBand > 0 ? MathF.Round(exactTop) : MathF.Floor(exactTop);
            frameBottom = bottomInModules > symbol.HeightInModules ? MathF.Round(exactBottom) : MathF.Ceiling(exactBottom);
            upperBottom = frameTop + thickness;
            lowerTop = frameBottom - thickness;
        }

        int index = 0;
        for (int r = 0; r < rows.Length; r++)
        {
            BarcodeRow row = rows[r];
            ReadOnlySpan<int> runs = allRuns.Slice(row.RunStart, row.RunCount);
            ReadOnlySpan<float> rowHeights = barHeights.Slice(row.BarStart, row.BarCount);
            ReadOnlySpan<float> rowBarTops = barTops.Slice(row.BarStart, row.BarCount);
            float rowTop = row.Top;

            // The position adds every run before it. A double holds each product of a whole run and the run width exactly, so the sum carries no rounding error
            // from the runs before it.
            double x = symbolLeft;
            for (int i = 0; i < runs.Length; i++)
            {
                double runWidth = wholePixelRuns ? RunPixels(runs[i], runWidthInPixels) : runs[i] * (double)runWidthInPixels;
                if ((i & 1) == 0)
                {
                    // Each bar edge rounds from its exact accumulated position onto the device pixel grid, so bars stay crisp for scanning at any module width
                    // and rounding cannot drift across the symbol, and the edge a row shares with the row below it rounds to one pixel row for both. Integral
                    // module widths, heights and origins round to themselves.
                    int bar = i >> 1;
                    float top = rowTop + rowBarTops[bar];
                    float bottom = top + rowHeights[bar];
                    float barLeft = (float)Math.Round(x);
                    float barRight = (float)Math.Round(x + runWidth);
                    float barTop = frame > 0 && top == frame
                        ? upperBottom
                        : MathF.Round(origin.Y + ((topBand + top) * moduleWidth));
                    float barBottom = frame > 0 && bottom == symbol.HeightInModules - frame
                        ? lowerTop
                        : MathF.Round(origin.Y + ((topBand + bottom) * moduleWidth));

                    bars[index++] = RectangleF.FromLTRB(barLeft, barTop, barRight, barBottom);
                    barsLeft = MathF.Min(barsLeft, barLeft);
                    barsTop = MathF.Min(barsTop, barTop);
                    barsRight = MathF.Max(barsRight, barRight);
                    barsBottom = MathF.Max(barsBottom, barBottom);
                }

                x += runWidth;
            }
        }

        if (frame > 0)
        {
            // When the quiet zones are drawn the frame reaches across them, and the quiet zones absorb the fraction of a pixel between the snapped outer edge
            // and the exact one. Without them the frame ends on the bars. The vertical sections run between the horizontal ones so no two rectangles overlap.
            float left;
            float right;
            if (frameSpansQuietZones)
            {
                left = MathF.Floor(frameLeft);
                right = MathF.Ceiling(frameLeft + ((widthInModules - symbol.WidthInModules) * moduleWidth) + blockWidth);
            }
            else
            {
                left = MathF.Round(symbolLeft);
                right = MathF.Round(symbolLeft + blockWidth);
            }

            bars[index++] = new RectangleF(left, frameTop, right - left, thickness);
            bars[index++] = new RectangleF(left, lowerTop, right - left, thickness);
            if (sides)
            {
                bars[index++] = new RectangleF(left, upperBottom, thickness, lowerTop - upperBottom);
                bars[index++] = new RectangleF(right - thickness, upperBottom, thickness, lowerTop - upperBottom);
            }

            // The frame spans the bars, so it sets the horizontal extent and the top and bottom.
            barsLeft = MathF.Min(barsLeft, left);
            barsTop = MathF.Min(barsTop, frameTop);
            barsRight = MathF.Max(barsRight, right);
            barsBottom = MathF.Max(barsBottom, lowerTop + thickness);
        }

        if (identificationCount > 0)
        {
            // The bars stand on the text edges the lines draw at, so the clearance holds to the drawn ink. The edge next to the text rounds away from it, and
            // both bars share one pixel thickness.
            float left = MathF.Floor(symbolLeft - (symbol.LeadingQuietZone * moduleWidth));
            float right = MathF.Ceiling(symbolLeft + blockWidth + (symbol.TrailingQuietZone * moduleWidth));
            if (upperIdentificationBar)
            {
                float edge = MathF.Round(origin.Y + ((topBand + aboveEdge) * moduleWidth));
                float bottom = MathF.Floor(edge - (aboveInk - aboveInkTop) - identificationClearance);
                bars[index++] = RectangleF.FromLTRB(left, bottom - identificationThickness, right, bottom);
                barsTop = MathF.Min(barsTop, bottom - identificationThickness);
            }

            if (lowerIdentificationBar)
            {
                float edge = MathF.Round(origin.Y + ((topBand + belowEdge) * moduleWidth));
                float top = MathF.Ceiling(edge + (belowInkBottom - belowInk) + identificationClearance);
                bars[index] = RectangleF.FromLTRB(left, top, right, top + identificationThickness);
                barsBottom = MathF.Max(barsBottom, top + identificationThickness);
            }

            barsLeft = MathF.Min(barsLeft, left);
            barsRight = MathF.Max(barsRight, right);
        }

        canvas.Fill(options.BarBrush, new BarcodePath(bars, RectangleF.FromLTRB(barsLeft, barsTop, barsRight, barsBottom)));

        if (digitFont is null)
        {
            return bounds;
        }

        for (int i = 0; i < text.Length; i++)
        {
            BarcodeTextPlacement placement = text[i];
            ReadOnlySpan<char> characters = symbol.TextOf(in placement);

            // The edge the reader sees is what rounds onto the device pixel grid, as the bar edges do. The ink of a line below the bars starts on the text
            // edge, and the ink of a line above the bars ends on it.
            float textEdge = MathF.Round(origin.Y + ((topBand + placement.TextEdge) * moduleWidth));
            float baseline = textEdge - belowInk + belowBaseline;

            // The measuring pass built the options of the caption and the digits. The scaled options follow the scaled font as they did there.
            RichTextOptions textOptions = fonts.OptionsFor(in placement, characters);
            MeasuredLine line = lines[i];
            float textY = placement.Side == BarcodeTextSide.AboveBars
                ? textEdge - aboveInk
                : baseline - line.Baseline;

            textOptions.Origin = new PointF(TextOrigin(placement, line.InkLeft, line.RenderableWidth, symbolLeft, textModuleWidth), textY);
            canvas.DrawText(textOptions, characters, options.BarBrush, null);
        }

        return bounds;
    }

    /// <summary>
    /// Returns the x origin of a line. The line stands on the cell edges the bars actually drew on, not on the exact fractional position, so a digit stays over
    /// its own symbol character; the unrounded edges would let the text and the bars disagree by up to half a module. A centred line straddles the middle of
    /// its span, and a left aligned line puts the left edge of its ink on the left edge of its span.
    /// </summary>
    /// <param name="placement">The placement.</param>
    /// <param name="inkLeft">The left edge of the ink of the measured line, from <see cref="TextMetrics.Bounds"/>.</param>
    /// <param name="renderableWidth">The width of the measured line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
    /// <param name="symbolLeft">The x of the first bar, in pixels.</param>
    /// <param name="moduleWidth">The module width in pixels, stretched to the drawn bar block.</param>
    /// <returns>The x origin in pixels.</returns>
    public static float TextOrigin(BarcodeTextPlacement placement, float inkLeft, float renderableWidth, float symbolLeft, float moduleWidth)
    {
        float left = MathF.Round(symbolLeft + (placement.Left * moduleWidth));
        if (placement.Alignment == BarcodeTextAlignment.Left)
        {
            return left - inkLeft;
        }

        float right = MathF.Round(symbolLeft + (placement.Right * moduleWidth));
        return ((left + right) * 0.5F) - (renderableWidth * 0.5F);
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
    /// <param name="options">The options that carry the fonts and the resolution.</param>
    /// <returns>The font to print the caption with.</returns>
    private static Font ResolveCaptionFont(ReadOnlySpan<char> caption, float spanModules, float moduleWidth, BarcodeOptions options)
    {
        Font font = options.CaptionFont ?? options.Font!;
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
    /// What the measuring pass measured of one line, so the drawing pass places the line without measuring it again.
    /// </summary>
    private readonly struct MeasuredLine
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MeasuredLine"/> struct.
        /// </summary>
        /// <param name="inkLeft">The left edge of the ink, from <see cref="TextMetrics.Bounds"/>.</param>
        /// <param name="renderableWidth">The width of the line, from <see cref="TextMetrics.RenderableBounds"/>.</param>
        /// <param name="baseline">The baseline of a line below the bars, or 0 for a line above them.</param>
        public MeasuredLine(float inkLeft, float renderableWidth, float baseline)
        {
            this.InkLeft = inkLeft;
            this.RenderableWidth = renderableWidth;
            this.Baseline = baseline;
        }

        /// <summary>
        /// Gets the left edge of the ink, from <see cref="TextMetrics.Bounds"/>.
        /// </summary>
        public float InkLeft { get; }

        /// <summary>
        /// Gets the width of the line, from <see cref="TextMetrics.RenderableBounds"/>.
        /// </summary>
        public float RenderableWidth { get; }

        /// <summary>
        /// Gets the baseline of a line below the bars, or 0 for a line above them.
        /// </summary>
        public float Baseline { get; }
    }

    /// <summary>
    /// The text options the lines of one symbol render with. A symbol resolves to at most three fonts: the digits, the scaled quiet zone digits and the
    /// caption. Each font keeps one options instance, built on first use, and the measuring pass and the drawing pass both read it from here.
    /// </summary>
    private struct LineFonts
    {
        /// <summary>
        /// The font the digit lines render in, after the size floor and the cell cap.
        /// </summary>
        private readonly Font digitFont;

        /// <summary>
        /// The sizing and painting options, which carry the caption font and the resolution.
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
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="moduleWidth">The width of one module in pixels, stretched to the drawn bar block.</param>
        public LineFonts(Font digitFont, BarcodeOptions options, float moduleWidth)
        {
            this.digitFont = digitFont;
            this.options = options;
            this.moduleWidth = moduleWidth;
        }

        /// <summary>
        /// Returns the text options a placement renders with. A caption renders in the caption font, resolved once for the symbol so that both the band and the
        /// drawing use one size.
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
                    ResolveCaptionFont(characters, placement.Right - placement.Left, this.moduleWidth, this.options),
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
