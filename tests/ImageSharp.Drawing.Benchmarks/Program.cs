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
                    Drawing.FillTiger.VerifyOutput();
                    break;
                case "paris":
                    Drawing.FillParis.VerifyOutput();
                    break;
                default:
                    Console.WriteLine($"Unknown verify target: {target}. Use 'tiger' or 'paris'.");
                    break;
            }

            return;
        }

        new BenchmarkSwitcher(typeof(Program).GetTypeInfo().Assembly).Run(args, new InProcessConfig());
    }
}
