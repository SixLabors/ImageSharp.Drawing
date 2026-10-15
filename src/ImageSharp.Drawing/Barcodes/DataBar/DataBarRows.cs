// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Builds the rows of the stacked GS1 DataBar symbols from their modules. A row or a separator row can
/// start and end with a light module, which a bar sequence cannot, so a zero width bar stands at a light end.
/// </summary>
internal static class DataBarRows
{
    /// <summary>
    /// Returns the number of runs of a row: one for each change of module color and one more, and a zero
    /// width bar at an end that is light, so even run indexes stay bars.
    /// </summary>
    /// <param name="modules">The modules of the row, 1 for dark.</param>
    /// <returns>The run count.</returns>
    public static int RunCount(ReadOnlySpan<byte> modules)
    {
        int count = 1 + (modules[0] == 0 ? 1 : 0) + (modules[^1] == 0 ? 1 : 0);
        for (int i = 1; i < modules.Length; i++)
        {
            if (modules[i] != modules[i - 1])
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Adds one row to a stacked symbol from its modules.
    /// </summary>
    /// <param name="modules">The modules of the row, 1 for dark.</param>
    /// <param name="height">The height of the row in modules.</param>
    /// <param name="symbol">The symbol to write.</param>
    public static void AddRow(ReadOnlySpan<byte> modules, float height, ref BarcodeSymbol symbol)
    {
        // A row that starts or ends light gets a zero width bar at that end, so even run indexes stay
        // bars. The zero width bars are the zeros the symbol storage starts with.
        int leading = modules[0] == 0 ? 1 : 0;
        Span<int> runs = symbol.AddRow(RunCount(modules), out Span<float> heights, out _);
        int run = leading;
        int width = 1;
        for (int i = 1; i < modules.Length; i++)
        {
            if (modules[i] != modules[i - 1])
            {
                runs[run++] = width;
                width = 1;
            }
            else
            {
                width++;
            }
        }

        runs[run] = width;
        heights.Fill(height);
    }
}
