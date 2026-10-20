// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Text;
using SixLabors.ImageSharp.Drawing.Barcodes;

namespace SixLabors.ImageSharp.Drawing.Tests.Barcodes;

/// <summary>
/// Encodes text into a <see cref="BarcodeSymbol"/> the way the drawing and measuring paths do. The symbol is a local, the symbology encodes into it, the test
/// reads it, and then the symbol releases each array it rented.
/// </summary>
internal static class SymbolEncoder
{
    /// <summary>
    /// Reads a value from an encoded symbol.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The value.</returns>
    public delegate T Reader<T>(ref BarcodeSymbol symbol);

    /// <summary>
    /// Checks an encoded symbol.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    public delegate void Check(ref BarcodeSymbol symbol);

    /// <summary>
    /// A resolution of 72 dots per inch, at which one point is one pixel, so a symbol of an X dimension of whole points draws at that X dimension.
    /// </summary>
    public const float PointDpi = 72F;

    /// <summary>
    /// An X dimension of one point in millimetres, which draws at one pixel at <see cref="PointDpi"/> and at a whole number of pixels at its multiples.
    /// </summary>
    public const double PointXDimension = 25.4 / 72;

    /// <summary>
    /// A resolution of 1000 dots per inch, at which a symbol of an X dimension of whole thousandths of an inch draws at that X dimension.
    /// </summary>
    public const float InchDpi = 1000F;

    /// <summary>
    /// A resolution of 100 pixels a millimetre, at which a symbol of an X dimension of whole hundredths of a millimetre draws at that X dimension.
    /// </summary>
    public const float MillimetreDpi = 2540F;

    /// <summary>
    /// A resolution of 1000 pixels a millimetre, at which a symbol of an X dimension of whole thousandths of a millimetre draws at that X dimension.
    /// </summary>
    public const float MicrometreDpi = 25400F;

    /// <summary>
    /// Encodes the text, then releases the storage the symbol rented.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options that control layout choices.</param>
    public static void Encode(BarcodeSymbology symbology, string text, BarcodeOptions options)
        => Encode(symbology, text, options, static (ref _) => { });

    /// <summary>
    /// Encodes the text, checks the symbol, then releases the storage the symbol rented.
    /// </summary>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="check">The check to run on the encoded symbol.</param>
    public static void Encode(BarcodeSymbology symbology, string text, BarcodeOptions options, Check check)
    {
        BarcodeSymbol symbol = default;
        try
        {
            symbology.Encode(text, options, ref symbol);
            check(ref symbol);
        }
        finally
        {
            symbol.Dispose();
        }
    }

    /// <summary>
    /// Encodes the text, reads a value from the symbol, then releases the storage the symbol rented.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="symbology">The symbology to encode with.</param>
    /// <param name="text">The text to encode.</param>
    /// <param name="options">The options that control layout choices.</param>
    /// <param name="read">The reader that takes the value from the encoded symbol.</param>
    /// <returns>The value.</returns>
    public static T Read<T>(BarcodeSymbology symbology, string text, BarcodeOptions options, Reader<T> read)
    {
        BarcodeSymbol symbol = default;
        try
        {
            symbology.Encode(text, options, ref symbol);
            return read(ref symbol);
        }
        finally
        {
            symbol.Dispose();
        }
    }

    /// <summary>
    /// Returns the run widths of a symbol as one string of digits, the form the reference run strings take.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The run widths.</returns>
    public static string RunString(ref BarcodeSymbol symbol) => string.Concat(symbol.Runs.ToArray());

    /// <summary>
    /// Returns the run widths of a symbol in whole modules, separated by commas. A run that differs from whole modules by less than half a module rounds to its
    /// modules. An example is a bar or space of an EAN/UPC character that Table 5-10 of the GS1 General Specifications corrects by one thirteenth.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The run widths in modules.</returns>
    public static string ModuleRuns(ref BarcodeSymbol symbol)
    {
        float runUnit = symbol.RunUnit;
        ReadOnlySpan<int> runs = symbol.Runs;
        string[] modules = new string[runs.Length];
        for (int i = 0; i < runs.Length; i++)
        {
            modules[i] = ((int)MathF.Round(runs[i] * runUnit)).ToString(CultureInfo.InvariantCulture);
        }

        return string.Join(',', modules);
    }

    /// <summary>
    /// Returns the run widths of one row of a symbol as one string of digits.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="row">The row.</param>
    /// <returns>The run widths of the row.</returns>
    public static string RunString(ref BarcodeSymbol symbol, in BarcodeRow row)
        => string.Concat(symbol.Runs.Slice(row.RunStart, row.RunCount).ToArray());

    /// <summary>
    /// Returns the run widths of every row of a symbol, from the top, the widths of a row separated by commas and the rows separated by a vertical bar.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The run widths of the rows.</returns>
    public static string RowRunLists(ref BarcodeSymbol symbol)
    {
        ReadOnlySpan<BarcodeRow> rows = symbol.Rows;
        string[] lists = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            lists[i] = string.Join(',', symbol.Runs.Slice(rows[i].RunStart, rows[i].RunCount).ToArray());
        }

        return string.Join('|', lists);
    }

    /// <summary>
    /// Returns the run widths of one row of a symbol.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="row">The row.</param>
    /// <returns>The run widths of the row.</returns>
    public static int[] RowRuns(ref BarcodeSymbol symbol, in BarcodeRow row)
        => symbol.Runs.Slice(row.RunStart, row.RunCount).ToArray();

    /// <summary>
    /// Returns the bar heights of one row of a symbol.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <param name="row">The row.</param>
    /// <returns>The bar heights of the row.</returns>
    public static float[] RowBarHeights(ref BarcodeSymbol symbol, in BarcodeRow row)
        => symbol.BarHeights.Slice(row.BarStart, row.BarCount).ToArray();

    /// <summary>
    /// Returns the heights of the rows of a symbol, from the top.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The row heights.</returns>
    public static float[] RowHeights(ref BarcodeSymbol symbol)
    {
        ReadOnlySpan<BarcodeRow> rows = symbol.Rows;
        float[] heights = new float[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            heights[i] = rows[i].Height;
        }

        return heights;
    }

    /// <summary>
    /// Returns the modules of a run sequence as a string of 1 for a dark module and 0 for a light module. The runs start with a bar.
    /// </summary>
    /// <param name="runs">The run widths.</param>
    /// <returns>The modules.</returns>
    public static string ModuleString(ReadOnlySpan<int> runs)
    {
        StringBuilder modules = new();
        for (int i = 0; i < runs.Length; i++)
        {
            modules.Append((i & 1) == 0 ? '1' : '0', runs[i]);
        }

        return modules.ToString();
    }

    /// <summary>
    /// Returns the modules of every row of a symbol, from the top, each row as <see cref="ModuleString"/> writes it and the rows separated by a vertical bar.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The modules of the rows.</returns>
    public static string RowModuleStrings(ref BarcodeSymbol symbol)
    {
        ReadOnlySpan<BarcodeRow> rows = symbol.Rows;
        string[] modules = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            modules[i] = ModuleString(symbol.Runs.Slice(rows[i].RunStart, rows[i].RunCount));
        }

        return string.Join('|', modules);
    }

    /// <summary>
    /// Returns the sum of the run widths of a symbol, which is its width in modules before the quiet zones when the run unit is one module.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The sum of the run widths.</returns>
    public static int RunSum(ref BarcodeSymbol symbol)
    {
        int sum = 0;
        foreach (int run in symbol.Runs)
        {
            sum += run;
        }

        return sum;
    }

    /// <summary>
    /// Returns the characters of every text placement of a symbol, in placement order.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The characters of each placement.</returns>
    public static string[] Texts(ref BarcodeSymbol symbol)
    {
        ReadOnlySpan<BarcodeTextPlacement> placements = symbol.Text;
        string[] texts = new string[placements.Length];
        for (int i = 0; i < placements.Length; i++)
        {
            texts[i] = symbol.TextOf(placements[i]).ToString();
        }

        return texts;
    }

    /// <summary>
    /// Reads the bar states of a height modulated postal symbol: F for a full bar, A for an ascender, D for a descender and T for a tracker. An ascender
    /// reaches the top of the bars and a descender reaches their bottom, so the states read the same at every module width.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The bar states.</returns>
    public static string FourStates(ref BarcodeSymbol symbol)
    {
        float bottom = BarBottom(ref symbol);
        char[] states = new char[symbol.BarHeights.Length];
        for (int i = 0; i < states.Length; i++)
        {
            bool up = symbol.BarTops[i] == 0F;
            bool down = symbol.BarTops[i] + symbol.BarHeights[i] > bottom - 0.001F;
            states[i] = up && down ? 'F' : up ? 'A' : down ? 'D' : 'T';
        }

        return new string(states);
    }

    /// <summary>
    /// Returns the bottom edge of the lowest bar of a symbol, in modules from the top of the bars.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The bottom edge in modules.</returns>
    public static float BarBottom(ref BarcodeSymbol symbol)
    {
        float bottom = 0F;
        for (int i = 0; i < symbol.BarHeights.Length; i++)
        {
            bottom = MathF.Max(bottom, symbol.BarTops[i] + symbol.BarHeights[i]);
        }

        return bottom;
    }

    /// <summary>
    /// Writes the modules of a matrix symbol as rows of hexadecimal digits separated by a vertical bar, the form the reference grids take.
    /// </summary>
    /// <param name="symbol">The encoded symbol.</param>
    /// <returns>The grid.</returns>
    public static string ModuleGrid(ref BarcodeSymbol symbol)
    {
        StringBuilder builder = new();
        for (int r = 0; r < symbol.MatrixRows; r++)
        {
            if (r > 0)
            {
                builder.Append('|');
            }

            // Four modules make one hexadecimal digit, the leftmost module the most significant bit. A row whose width is not a multiple of four pads its last
            // digit with light modules.
            for (int c = 0; c < symbol.Columns; c += 4)
            {
                int digit = 0;
                for (int b = 0; b < 4; b++)
                {
                    digit <<= 1;
                    if (c + b < symbol.Columns && symbol.Modules[(r * symbol.Columns) + c + b])
                    {
                        digit |= 1;
                    }
                }

                builder.Append("0123456789abcdef"[digit]);
            }
        }

        return builder.ToString();
    }
}
