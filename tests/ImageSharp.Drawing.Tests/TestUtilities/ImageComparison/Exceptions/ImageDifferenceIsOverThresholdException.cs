// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;

public class ImageDifferenceIsOverThresholdException : ImagesSimilarityException
{
    public ImageSimilarityReport[] Reports { get; }

    public ImageDifferenceIsOverThresholdException(IEnumerable<ImageSimilarityReport> reports)
        : base("Image difference is over threshold!" + StringifyReports(reports))
        => this.Reports = reports.ToArray();

    private static string StringifyReports(IEnumerable<ImageSimilarityReport> reports)
    {
        StringBuilder sb = new();

        sb.Append(Environment.NewLine);

        int i = 0;
        foreach (ImageSimilarityReport r in reports)
        {
            sb.Append($"Report ImageFrame {i}: ");
            sb.Append(r);
            sb.Append(Environment.NewLine);
            i++;
        }

        return sb.ToString();
    }
}
