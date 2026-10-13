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
    /// Builds one row from its modules.
    /// </summary>
    /// <param name="modules">The modules of the row, 1 for dark.</param>
    /// <param name="height">The height of the row in modules.</param>
    /// <returns>The row.</returns>
    public static LinearBarcodeSymbol Row(ReadOnlySpan<byte> modules, float height)
    {
        // A row that starts or ends light gets a zero width bar at that end, so even run indexes stay
        // bars. The zero width bars are the default zeros of the array.
        int leading = modules[0] == 0 ? 1 : 0;
        int trailing = modules[^1] == 0 ? 1 : 0;
        int count = 1 + leading + trailing;
        for (int i = 1; i < modules.Length; i++)
        {
            if (modules[i] != modules[i - 1])
            {
                count++;
            }
        }

        int[] runs = new int[count];
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
        int barCount = (runs.Length + 1) / 2;
        float[] heights = new float[barCount];
        heights.AsSpan().Fill(height);
        return new LinearBarcodeSymbol(runs, heights, new float[barCount], [], 0, 0);
    }
}
