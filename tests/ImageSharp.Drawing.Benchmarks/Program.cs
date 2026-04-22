// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Reflection;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

namespace SixLabors.ImageSharp.Drawing.Benchmarks;

public class InProcessConfig : ManualConfig
{
    public InProcessConfig()
    {
        this.AddLogger(ConsoleLogger.Default);

        this.AddColumnProvider(DefaultColumnProviders.Instance);

        this.AddExporter(DefaultExporters.Html, DefaultExporters.Csv);

        // Use high warmup to ensure tiered JIT has fully promoted all hot paths.
        // Server GC reduces pause times for allocation-heavy rasterization benchmarks.
        this.AddJob(
            Job.Default
                .WithLaunchCount(3)
                .WithWarmupCount(40)
                .WithIterationCount(40)
                .WithGcServer(true)
                .WithGcForce(false));
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--verify")
        {
            string target = args.Length > 1 ? args[1] : "tiger";
            switch (target.ToLowerInvariant())
            {
                case "tiger":
                    int dimensions = args.Length > 2 && int.TryParse(args[2], out int parsedDimensions)
                        ? parsedDimensions
                        : 1000;
                    Drawing.FillTiger.VerifyOutput(dimensions);
                    break;
                case "tiger-xform":
                case "tiger-baked":
                    // Usage: --verify tiger-xform|tiger-baked <width> <height> <zoom> <panX> <panY>
                    int width = args.Length > 2 && int.TryParse(args[2], out int w) ? w : 1280;
                    int height = args.Length > 3 && int.TryParse(args[3], out int h) ? h : 800;
                    float zoom = args.Length > 4 && float.TryParse(args[4], System.Globalization.CultureInfo.InvariantCulture, out float z) ? z : 11.2086f;
                    float panX = args.Length > 5 && float.TryParse(args[5], System.Globalization.CultureInfo.InvariantCulture, out float px) ? px : 81.19f;
                    float panY = args.Length > 6 && float.TryParse(args[6], System.Globalization.CultureInfo.InvariantCulture, out float py) ? py : 80.13f;
                    if (target == "tiger-baked")
                    {
                        Drawing.FillTiger.VerifyTransformBaked(width, height, zoom, panX, panY);
                    }
                    else
                    {
                        Drawing.FillTiger.VerifyTransform(width, height, zoom, panX, panY);
                    }

                    break;
                case "paris":
                    Drawing.FillParis.VerifyOutput();
                    break;
                default:
                    Console.WriteLine($"Unknown verify target: {target}. Use 'tiger', 'tiger-xform', 'tiger-baked', or 'paris'.");
                    break;
            }

            return;
        }

        if (args.Length > 0 && args[0] == "--profile")
        {
            string target = args.Length > 1 ? args[1] : string.Empty;
            int iterations = args.Length > 2 && int.TryParse(args[2], out int parsedIterations)
                ? parsedIterations
                : 20;
            switch (target.ToLowerInvariant())
            {
                case "paris-cpu":
                    Drawing.FillParis.ProfileCpu(iterations);
                    break;
                case "paris-webgpu":
                    Drawing.FillParis.ProfileWebGpu(iterations);
                    break;
                default:
                    Console.WriteLine($"Unknown profile target: {target}. Use 'paris-cpu' or 'paris-webgpu'.");
                    break;
            }

            return;
        }

        new BenchmarkSwitcher(typeof(Program).GetTypeInfo().Assembly).Run(args, new InProcessConfig());
    }
}
