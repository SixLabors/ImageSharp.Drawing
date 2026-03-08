// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Reflection;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace SixLabors.ImageSharp.Drawing.Benchmarks;

public class InProcessConfig : ManualConfig
{
    public InProcessConfig()
    {
        this.AddLogger(ConsoleLogger.Default);

        this.AddColumnProvider(DefaultColumnProviders.Instance);

        this.AddExporter(DefaultExporters.Html, DefaultExporters.Csv);

        // Use a long, stable job for rasterization benchmarks where scheduler noise and
        // thread-pool startup can otherwise dominate short in-process runs.
        this.AddJob(
            Job.Default
                .WithLaunchCount(3)
                .WithWarmupCount(40)
                .WithIterationCount(40)
                .WithToolchain(InProcessEmitToolchain.Instance));
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        new BenchmarkSwitcher(typeof(Program).GetTypeInfo().Assembly).Run(args, new InProcessConfig());
    }
}
