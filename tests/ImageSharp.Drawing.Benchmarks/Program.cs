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
        AddLogger(ConsoleLogger.Default);

        AddColumnProvider(DefaultColumnProviders.Instance);

        AddExporter(DefaultExporters.Html, DefaultExporters.Csv);

        this.AddJob(Job.MediumRun
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
