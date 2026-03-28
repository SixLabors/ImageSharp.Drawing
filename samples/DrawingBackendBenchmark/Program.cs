// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;

namespace DrawingBackendBenchmark;

/// <summary>
/// Entry point for the line-drawing backend benchmark sample.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Starts the Windows Forms benchmark host.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BenchmarkForm());
    }
}

/// <summary>
/// Running statistics for the render-time samples collected during one benchmark run.
/// </summary>
internal readonly record struct BenchmarkStatistics(double MeanMilliseconds, double StdDevMilliseconds)
{
    /// <summary>
    /// Computes the mean and standard deviation for the current sample window.
    /// </summary>
    public static BenchmarkStatistics FromSamples(IReadOnlyList<double> samples)
    {
        double mean = samples.Average();
        double variance = samples.Sum(x => Math.Pow(x - mean, 2)) / samples.Count;
        double stdDev = Math.Sqrt(variance);
        return new BenchmarkStatistics(mean, stdDev);
    }
}
