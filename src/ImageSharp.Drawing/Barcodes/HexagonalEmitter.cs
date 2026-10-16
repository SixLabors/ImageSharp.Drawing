// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Converts a hexagonal <see cref="BarcodeSymbol"/>, the form of MaxiCode, into canvas commands: the dark hexagons and the three dark rings of the finder in
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
    /// The row and column of the module the finder is centred on, section 4.11.4: "16Y above the center of modules in the bottom-most row of the symbol, and
    /// 14W to the right from the center of the left-most module of the row".
    /// </summary>
    private const int FinderRow = 16;
    private const int FinderColumn = 14;

    /// <summary>
    /// Gets the radii of the finder transitions in millimetres at the nominal size, Table 8: the light centre out to R1, then dark to R2, light to R3, dark to
    /// R4, light to R5 and dark to R6.
    /// </summary>
    private static ReadOnlySpan<double> FinderRadii => [0.51, 1.18, 1.86, 2.53, 3.20, 3.87];

    /// <summary>
    /// Renders the symbol onto the canvas.
    /// </summary>
    /// <param name="canvas">The canvas to render onto.</param>
    /// <param name="symbol">The encoded hexagonal symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the symbol and its quiet zones, in pixels.</param>
    public static void Emit(DrawingCanvas canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(canvas, ref symbol, options, origin);

    /// <summary>
    /// Measures the area the symbol covers without drawing anything.
    /// </summary>
    /// <param name="symbol">The encoded hexagonal symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner the symbol would draw from, in pixels.</param>
    /// <returns>The area the symbol and its quiet zones cover.</returns>
    public static RectangleF Measure(ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
        => Layout(null, ref symbol, options, origin);

    /// <summary>
    /// Lays the symbol out and, when a canvas is supplied, draws it, so the measured area and the drawn area are the same.
    /// </summary>
    /// <param name="canvas">The canvas to render onto, or <see langword="null"/> to measure only.</param>
    /// <param name="symbol">The encoded hexagonal symbol.</param>
    /// <param name="options">The sizing and painting options.</param>
    /// <param name="origin">The top left corner of the symbol and its quiet zones, in pixels.</param>
    /// <returns>The area the symbol and its quiet zones cover.</returns>
    private static RectangleF Layout(DrawingCanvas? canvas, ref BarcodeSymbol symbol, BarcodeOptions options, PointF origin)
    {
        double pixelsPerMillimetre = options.Dpi / BarcodeOptions.MillimetresPerInch;
        double x = symbol.XDimension * pixelsPerMillimetre;

        // Table 7: V = (2/√3)W, the height of a module, and Y = (1.5/√3)W, the distance between rows.
        double v = 2 / Math.Sqrt(3) * x;
        double y = 1.5 / Math.Sqrt(3) * x;

        float sides = options.IncludeQuietZones ? symbol.LeadingQuietZone + symbol.TrailingQuietZone : 0;
        float ends = options.IncludeQuietZones ? symbol.TopQuietZone + symbol.BottomQuietZone : 0;
        double left = origin.X + (options.IncludeQuietZones ? symbol.LeadingQuietZone * x : 0);
        double top = origin.Y + (options.IncludeQuietZones ? symbol.TopQuietZone * x : 0);
        RectangleF bounds = RectangleF.FromLTRB(
            MathF.Floor(origin.X),
            MathF.Floor(origin.Y),
            MathF.Ceiling((float)(origin.X + ((symbol.WidthInModules + sides) * x))),
            MathF.Ceiling((float)(origin.Y + ((symbol.HeightInModules + ends) * x))));

        if (canvas is null)
        {
            return bounds;
        }

        if (options.Background is not null)
        {
            canvas.Fill(options.Background, new RectanglePolygon(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        ReadOnlySpan<bool> modules = symbol.Modules;
        int columns = symbol.Columns;

        // The pointed hexagons stand on their centres, the odd rows half a module to the right. A dark hexagon keeps the shape of its module at the reduced
        // width and height. The path retains the centres until the canvas flushes, so it owns the array, sized by a count of the dark modules.
        double reduction = DarkHexagonReduction * pixelsPerMillimetre;
        SizeF halfSize = new((float)((x - reduction) / 2), (float)((v - reduction) / 2));
        PointF[] centres = new PointF[modules.Count(true)];
        int index = 0;
        for (int row = 0; row < symbol.MatrixRows; row++)
        {
            ReadOnlySpan<bool> rowModules = modules.Slice(row * columns, columns);
            double centreY = top + (v / 2) + (row * y);
            double shift = (row & 1) == 1 ? x / 2 : 0;
            for (int column = 0; column < columns; column++)
            {
                if (rowModules[column])
                {
                    centres[index++] = new PointF((float)(left + shift + ((column + 0.5) * x)), (float)centreY);
                }
            }
        }

        // The symbol region holds every hexagon, so it bounds the path. The path of the hexagons comes first, then one path for each dark ring.
        RectangleF symbolRegion = new((float)left, (float)top, (float)(symbol.WidthInModules * x), (float)(symbol.HeightInModules * x));
        ReadOnlySpan<double> radii = FinderRadii;
        IPath[] paths = new IPath[1 + (radii.Length / 2)];
        paths[0] = new BarcodePath(centres, halfSize, symbolRegion);

        // Each dark ring is its outer circle with the inner circle wound the other way, which the nonzero rule leaves light. The radii of Table 8 scale with
        // the X dimension, and each ring takes the next pair of transitions.
        PointF centre = new((float)(left + ((FinderColumn + 0.5) * x)), (float)(top + (v / 2) + (FinderRow * y)));
        double radiusScale = symbol.XDimension / TableXDimension * pixelsPerMillimetre;
        for (int ring = 1; ring < paths.Length; ring++)
        {
            float inner = (float)(radii[(ring * 2) - 2] * radiusScale);
            float outer = (float)(radii[(ring * 2) - 1] * radiusScale);
            paths[ring] = new ComplexPolygon(new EllipsePolygon(centre, outer), new EllipsePolygon(centre, inner).Reverse());
        }

        canvas.Fill(options.BarBrush, new ComplexPolygon(paths));
        return bounds;
    }
}
