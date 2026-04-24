// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using CommandLine;
using CommandLine.Text;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.ManualBenchmarks;

public sealed class DrawingThroughputBenchmark
{
    private readonly CommandLineOptions options;
    private readonly Configuration configuration;
    private readonly List<(IPath Path, SolidBrush Fill, SolidPen Stroke)> elements;
    private ulong totalProcessedPixels;

    private DrawingThroughputBenchmark(CommandLineOptions options)
    {
        this.options = options;
        this.configuration = Configuration.Default.Clone();
        this.configuration.MaxDegreeOfParallelism = options.ProcessorParallelism > 0
            ? options.ProcessorParallelism
            : Environment.ProcessorCount;
        List<SvgBenchmarkHelper.SvgElement> elements = SvgBenchmarkHelper.ParseSvg(
            TestFile.GetInputFileFullPath(TestImages.Svg.GhostscriptTiger));
        float size = (options.Width + options.Height) * 0.5f;
        this.elements = SvgBenchmarkHelper.BuildImageSharpElements(elements, size / 200f);
    }

    public static Task RunAsync(string[] args)
    {
        CommandLineOptions? options = null;
        if (args.Length > 0)
        {
            options = CommandLineOptions.Parse(args);
            if (options == null)
            {
                return Task.CompletedTask;
            }
        }

        options ??= new CommandLineOptions();
        return new DrawingThroughputBenchmark(options.Normalize())
            .RunAsync();
    }

    private async Task RunAsync()
    {
        SemaphoreSlim semaphore = new(this.options.ConcurrentRequests);
        Console.WriteLine(this.options.Method);
        Func<int> action = this.options.Method switch
        {
            Method.Tiger => this.Tiger,
            _ => throw new NotImplementedException(),
        };

        Console.WriteLine(this.options);
        Console.WriteLine($"Running {this.options.Method} for {this.options.Seconds} seconds ...");
        TimeSpan runFor = TimeSpan.FromSeconds(this.options.Seconds);

        // inFlight starts at 1 to represent the dispatch loop itself
        int inFlight = 1;
        TaskCompletionSource drainTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < runFor && !drainTcs.Task.IsCompleted)
        {
            await semaphore.WaitAsync();

            if (stopwatch.Elapsed >= runFor)
            {
                semaphore.Release();
                break;
            }

            Interlocked.Increment(ref inFlight);

            _ = ProcessImage();

            async Task ProcessImage()
            {
                try
                {
                    if (stopwatch.Elapsed >= runFor || drainTcs.Task.IsCompleted)
                    {
                        return;
                    }

                    await Task.Yield(); // "emulate IO", i.e., make sure the processing code is async
                    ulong pixels = (ulong)action();
                    Interlocked.Add(ref this.totalProcessedPixels, pixels);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                    drainTcs.TrySetException(ex);
                }
                finally
                {
                    semaphore.Release();
                    if (Interlocked.Decrement(ref inFlight) == 0)
                    {
                        drainTcs.TrySetResult();
                    }
                }
            }
        }

        // Release the dispatch loop's own count; if no work is in flight, this completes immediately
        if (Interlocked.Decrement(ref inFlight) == 0)
        {
            drainTcs.TrySetResult();
        }

        await drainTcs.Task;
        stopwatch.Stop();

        double totalMegaPixels = this.totalProcessedPixels / 1_000_000.0;
        double totalSeconds = stopwatch.ElapsedMilliseconds / 1000.0;
        double megapixelsPerSec = totalMegaPixels / totalSeconds;
        Console.WriteLine($"TotalSeconds: {totalSeconds:F2}");
        Console.WriteLine($"MegaPixelsPerSec: {megapixelsPerSec:F2}");
    }

    private int Tiger()
    {
        using Image<Rgba32> image = new(this.options.Width, this.options.Height);
        image.Mutate(this.configuration, c => c.Paint(canvas =>
        {
            foreach ((IPath path, SolidBrush fill, SolidPen stroke) in this.elements)
            {
                if (fill is not null)
                {
                    canvas.Fill(fill, path);
                }

                if (stroke is not null)
                {
                    canvas.Draw(stroke, path);
                }
            }
        }));
        return image.Width * image.Height;
    }

    private enum Method
    {
        Tiger,
    }

    private sealed class CommandLineOptions
    {
        private const int DefaultSize = 2000;

        [Option('m', "method", Required = false, Default = Method.Tiger, HelpText = "The stress test method to run (Edges, Crop)")]
        public Method Method { get; set; } = Method.Tiger;

        [Option('p', "drawing-parallelism", Required = false, Default = -1, HelpText = "Level of parallelism for the image processor")]
        public int ProcessorParallelism { get; set; } = -1;

        [Option('c', "concurrent-requests", Required = false, Default = -1, HelpText = "Number of concurrent in-flight requests")]
        public int ConcurrentRequests { get; set; } = -1;

        [Option('w', "width", Required = false, Default = DefaultSize, HelpText = "Width of the test image")]
        public int Width { get; set; } = DefaultSize;

        [Option('h', "height", Required = false, Default = DefaultSize, HelpText = "Height of the test image")]
        public int Height { get; set; } = DefaultSize;

        [Option('s', "seconds", Required = false, Default = 5, HelpText = "Duration of the stress test in seconds")]
        public int Seconds { get; set; } = 5;

        public override string ToString() => string.Join(
            "|",
            $"method: {this.Method}",
            $"processor-parallelism: {this.ProcessorParallelism}",
            $"concurrent-requests: {this.ConcurrentRequests}",
            $"width: {this.Width}",
            $"height: {this.Height}",
            $"seconds: {this.Seconds}");

        public CommandLineOptions Normalize()
        {
            if (this.ProcessorParallelism < 0)
            {
                this.ProcessorParallelism = Environment.ProcessorCount;
            }

            if (this.ConcurrentRequests < 0)
            {
                this.ConcurrentRequests = Environment.ProcessorCount;
            }

            return this;
        }

        public static CommandLineOptions? Parse(string[] args)
        {
            CommandLineOptions? result = null;
            using Parser parser = new(settings => settings.CaseInsensitiveEnumValues = true);
            ParserResult<CommandLineOptions> parserResult = parser.ParseArguments<CommandLineOptions>(args).WithParsed(o => result = o);

            if (result == null)
            {
                Console.WriteLine(HelpText.RenderUsageText(parserResult));
            }

            return result;
        }
    }
}
