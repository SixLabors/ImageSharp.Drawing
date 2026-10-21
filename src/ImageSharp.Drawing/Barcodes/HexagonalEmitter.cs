// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Measures and draws a hexagonal <see cref="BarcodeSymbol"/>, the form of MaxiCode. The draw puts the dark hexagons and the three dark rings of the finder in
/// one fill of one path. The dimensions are section 4.11 of ISO/IEC 16023.
/// </summary>
internal static class HexagonalEmitter
{
    /// <summary>
    /// The nominal X dimension in millimetres that the finder radii of Table 8 are given at: W = L/29 with L = 25.50 mm, Table 7.
    /// </summary>
    private const double TableXDimension = 0.88;

    /// <summary>
    /// The amount in millimetres by which a dark hexagon is narrower and shorter than its module, Table 6: a width of (X - 0.12 mm) and a height of (V - 0.12
    /// mm), so that "dark hexagons should not abut contiguously but instead should be distinct from one another".
    /// </summary>
    private const double DarkHexagonReduction = 0.12;

    /// <summary>
    /// The row of the module the finder is centred on, section 4.11.4: "16Y above the center of modules in the bottom-most row of the symbol, and 14W to the
    /// right from the center of the left-most module of the row".
    /// </summary>
    private const int FinderRow = 16;

    /// <summary>
    /// The column of the module the finder is centred on, section 4.11.4. See <see cref="FinderRow"/>.
    /// </summary>
    private const int FinderColumn = 14;

    /// <summary>
    /// Gets the radii of the finder transitions in millimetres at the nominal size, Table 8: the light centre out to R1, then dark to R2, light to R3, dark to
    /// R4, light to R5 and dark to R6.
    /// </summary>
    private static ReadOnlySpan<double> FinderRadii => [0.51, 1.18, 1.86, 2.53, 3.20, 3.87];

    /// <summary>
    /// Measures the symbol without drawing anything.
    /// </summary>
    /// <param name="symbol">The encoded hexagonal symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="symbolOrigin">The top left corner of the first module of the symbol, in pixels. The hexagons do not round onto the pixel grid.</param>
    /// <returns>The measured layout of the symbol.</returns>
    public static BarcodeMetrics Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, out PointF symbolOrigin)
    {
        HexagonLayout layout = new(ref symbol, options, origin);
        symbolOrigin = layout.SymbolOrigin;
        return layout.Metrics;
    }

    /// <summary>
    /// Draws the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded hexagonal symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the barcode, in pixels.</param>
    /// <param name="background">The brush that fills the renderable bounds before the modules draw, or <see langword="null"/> for none.</param>
    /// <returns>The measured layout of the symbol, the same as <see cref="Measure"/> returns.</returns>
    public static BarcodeMetrics Draw(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin, Brush? background)
    {
        HexagonLayout layout = new(ref symbol, options, origin);

        BarcodeMetrics metrics = layout.Metrics;
        if (background is not null)
        {
            RectangleF renderable = metrics.RenderableBounds;
            canvas.Fill(background, new RectanglePolygon(renderable.X, renderable.Y, renderable.Width, renderable.Height));
        }

        // The path of the hexagons comes first, then one path for each dark ring. Each dark ring is its outer circle with the inner circle wound the other
        // way, which the nonzero rule leaves light. Each ring takes the next pair of transitions of Table 8.
        ReadOnlySpan<double> radii = FinderRadii;
        IPath[] paths = new IPath[1 + (radii.Length / 2)];
        paths[0] = new BarcodePath(layout.Centres(ref symbol), layout.HalfSize, layout.HexagonInk);
        int ring = 1;
        for (int i = 0; i < radii.Length; i += 2)
        {
            float inner = (float)(radii[i] * layout.RadiusScale);
            float outer = (float)(radii[i + 1] * layout.RadiusScale);
            paths[ring++] = new ComplexPolygon(new EllipsePolygon(layout.FinderCentre, outer), new EllipsePolygon(layout.FinderCentre, inner).Reverse());
        }

        canvas.Fill(options.BarBrush, new ComplexPolygon(paths));
        return metrics;
    }

    /// <summary>
    /// The layout of a hexagonal symbol: where its hexagons and its finder stand, from one origin. The measure and the draw read one layout.
    /// </summary>
    private readonly ref struct HexagonLayout
    {
        /// <summary>
        /// The left edge of the symbol, in pixels.
        /// </summary>
        private readonly double left;

        /// <summary>
        /// The top edge of the symbol, in pixels.
        /// </summary>
        private readonly double top;

        /// <summary>
        /// The width of a module, W, in pixels.
        /// </summary>
        private readonly double x;

        /// <summary>
        /// The height of a module, V, in pixels.
        /// </summary>
        private readonly double v;

        /// <summary>
        /// The distance between rows, Y, in pixels.
        /// </summary>
        private readonly double y;

        /// <summary>
        /// Initializes a new instance of the <see cref="HexagonLayout"/> struct.
        /// </summary>
        /// <param name="symbol">The encoded hexagonal symbol.</param>
        /// <param name="options">The sizing and painting options.</param>
        /// <param name="origin">The top left corner of the barcode, in pixels.</param>
        public HexagonLayout(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        {
            double pixelsPerMillimetre = options.Dpi / BarcodeOptions.MillimetresPerInch;
            double x = symbol.XDimension * pixelsPerMillimetre;
            this.x = x;

            // Table 7: V = (2/√3)W, the height of a module, and Y = (1.5/√3)W, the distance between rows.
            this.v = 2 / Math.Sqrt(3) * x;
            this.y = 1.5 / Math.Sqrt(3) * x;

            float sides = options.IncludeQuietZones ? symbol.LeadingQuietZone + symbol.TrailingQuietZone : 0;
            float ends = options.IncludeQuietZones ? symbol.TopQuietZone + symbol.BottomQuietZone : 0;
            this.left = origin.X + (options.IncludeQuietZones ? symbol.LeadingQuietZone * x : 0);
            this.top = origin.Y + (options.IncludeQuietZones ? symbol.TopQuietZone * x : 0);
            RectangleF advance = new(origin.X, origin.Y, (float)((symbol.WidthInModules + sides) * x), (float)((symbol.HeightInModules + ends) * x));

            // A dark hexagon keeps the shape of its module at the reduced width and height.
            double reduction = DarkHexagonReduction * pixelsPerMillimetre;
            this.HalfSize = new SizeF((float)((x - reduction) / 2), (float)((this.v - reduction) / 2));

            // The radii of Table 8 scale with the X dimension.
            this.FinderCentre = new PointF((float)(this.left + ((FinderColumn + 0.5) * x)), (float)(this.top + (this.v / 2) + (FinderRow * this.y)));
            this.RadiusScale = symbol.XDimension / TableXDimension * pixelsPerMillimetre;

            // The ink of the hexagons comes from the first and the last dark module of each row, which a vectorized search finds, so no hexagon is visited.
            // A hexagon reaches half its reduced width to each side of its centre and half its reduced height above and below it.
            ReadOnlySpan<bool> modules = symbol.Modules;
            int columns = symbol.Columns;
            float inkLeft = float.MaxValue;
            float inkRight = float.MinValue;
            int firstRow = -1;
            int lastRow = -1;
            for (int row = 0; row < symbol.MatrixRows; row++)
            {
                ReadOnlySpan<bool> rowModules = modules.Slice(row * columns, columns);
                int first = rowModules.IndexOf(true);
                if (first < 0)
                {
                    continue;
                }

                firstRow = firstRow < 0 ? row : firstRow;
                lastRow = row;
                inkLeft = MathF.Min(inkLeft, this.CentreX(row, first) - this.HalfSize.Width);
                inkRight = MathF.Max(inkRight, this.CentreX(row, rowModules.LastIndexOf(true)) + this.HalfSize.Width);
            }

            this.HexagonInk = RectangleF.FromLTRB(
                inkLeft,
                this.CentreY(firstRow) - this.HalfSize.Height,
                inkRight,
                this.CentreY(lastRow) + this.HalfSize.Height);

            // The ink is the union of the hexagons and the outermost dark ring of the finder. As in text measuring, the advance is relative to the origin,
            // and the bounds and the renderable bounds are absolute.
            float radius = (float)(FinderRadii[^1] * this.RadiusScale);
            RectangleF finder = new(this.FinderCentre.X - radius, this.FinderCentre.Y - radius, 2 * radius, 2 * radius);
            RectangleF bounds = RectangleF.Union(this.HexagonInk, finder);
            this.Metrics = new BarcodeMetrics(
                new RectangleF(0, 0, advance.Width, advance.Height),
                bounds,
                RectangleF.Union(advance, bounds));
        }

        /// <summary>
        /// Gets the union of the dark hexagons, in pixels.
        /// </summary>
        public RectangleF HexagonInk { get; }

        /// <summary>
        /// Gets the top left corner of the first module of the symbol, in pixels.
        /// </summary>
        public PointF SymbolOrigin => new((float)this.left, (float)this.top);

        /// <summary>
        /// Gets the metrics of the layout.
        /// </summary>
        public BarcodeMetrics Metrics { get; }

        /// <summary>
        /// Gets half the width and half the height of every dark hexagon, in pixels.
        /// </summary>
        public SizeF HalfSize { get; }

        /// <summary>
        /// Gets the centre of the finder, in pixels.
        /// </summary>
        public PointF FinderCentre { get; }

        /// <summary>
        /// Gets the factor from the radii of Table 8 in millimetres to pixels.
        /// </summary>
        public double RadiusScale { get; }

        /// <summary>
        /// Returns the centre of every dark hexagon. The pointed hexagons stand on their centres, the odd rows half a module to the right. The path retains the
        /// centres until the canvas flushes, so it owns the array, sized by a count of the dark modules.
        /// </summary>
        /// <param name="symbol">The encoded symbol.</param>
        /// <returns>The centres, in pixels.</returns>
        public PointF[] Centres(ref BarcodeSymbol symbol)
        {
            ReadOnlySpan<bool> modules = symbol.Modules;
            int columns = symbol.Columns;
            PointF[] centres = new PointF[modules.Count(true)];
            int index = 0;
            for (int row = 0; row < symbol.MatrixRows; row++)
            {
                ReadOnlySpan<bool> rowModules = modules.Slice(row * columns, columns);
                float centreY = this.CentreY(row);
                for (int column = 0; column < columns; column++)
                {
                    if (rowModules[column])
                    {
                        centres[index++] = new PointF(this.CentreX(row, column), centreY);
                    }
                }
            }

            return centres;
        }

        /// <summary>
        /// Returns the x of the centre of a module, in pixels. The odd rows stand half a module to the right.
        /// </summary>
        /// <param name="row">The row of the module.</param>
        /// <param name="column">The column of the module.</param>
        /// <returns>The x in pixels.</returns>
        private float CentreX(int row, int column)
            => (float)(this.left + ((row & 1) == 1 ? this.x / 2 : 0) + ((column + 0.5) * this.x));

        /// <summary>
        /// Returns the y of the centres of a row of modules, in pixels.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>The y in pixels.</returns>
        private float CentreY(int row)
            => (float)(this.top + (this.v / 2) + (row * this.y));
    }
}
