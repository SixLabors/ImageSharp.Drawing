// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AvaloniaControlCatalog.Tests;

public partial class CalendarCommandCaptureTests
{
    [WindowsTheory]
    [InlineData("cpu")]
    [InlineData("webgpu")]
    public async Task CalendarPage_XamlCommands_RenderDarkCalendarHeaders(string backend)
    {
        string repositoryRoot = FindRepositoryRoot();
        string configuration = GetCurrentConfiguration();
        string samplePath = Path.Combine(
            repositoryRoot,
            "artifacts",
            "bin",
            "samples",
            "AvaloniaControlCatalog",
            configuration,
            "net8.0-windows",
            "ControlCatalog.dll");

        Assert.True(File.Exists(samplePath), $"ControlCatalog sample was not built at '{samplePath}'.");

        string captureDirectory = Path.Combine(Path.GetTempPath(), $"imagesharp-calendar-{Guid.NewGuid():N}");
        Directory.CreateDirectory(captureDirectory);

        await RunControlCatalogAsync(samplePath, captureDirectory, backend);

        CalendarContext[] contexts = CaptureCalendarContexts(captureDirectory)
            .ToArray();

        Assert.NotEmpty(contexts);

        foreach (CalendarContext context in contexts)
        {
            using Image<Bgra32> image = Image.Load<Bgra32>(context.ImagePath);

            foreach (CalendarHeader header in context.Headers)
            {
                Rectangle bounds = header.Bounds;
                int darkPixels = CountDarkPixels(image, bounds);
                int area = bounds.Width * bounds.Height;

                Assert.True(
                    darkPixels * 4 > area * 3,
                    $"Expected Calendar header bounds {bounds} to be dark but found {darkPixels}/{area} dark pixels. Image: {context.ImagePath}. Captures: {captureDirectory}");
            }
        }
    }

    private static async Task RunControlCatalogAsync(string samplePath, string captureDirectory, string backend)
    {
        ProcessStartInfo startInfo = new("dotnet", $"\"{samplePath}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(samplePath)!
        };

        startInfo.Environment["IMAGESHARP_CONTROL_CATALOG_PAGE"] = "Calendar";
        startInfo.Environment["IMAGESHARP_CONTROL_CATALOG_CALENDAR_SCROLL_MONTHS"] = "4";
        startInfo.Environment["IMAGESHARP_CONTROL_CATALOG_EXIT_AFTER_MS"] = "5000";
        startInfo.Environment["IMAGESHARP_DRAWING_BACKEND"] = backend;
        startInfo.Environment["IMAGESHARP_DRAWING_CAPTURE_DIRECTORY"] = captureDirectory;
        startInfo.Environment["IMAGESHARP_DRAWING_SAVE_ALL_CONTEXTS"] = "1";
        startInfo.Environment["IMAGESHARP_DRAWING_TRACE_ALL_CONTEXTS"] = "1";

        using Process process = Process.Start(startInfo)!;
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);

            Assert.Fail($"ControlCatalog did not exit after rendering Calendar. Captures: {captureDirectory}");
        }

        string output = await standardOutput;
        string error = await standardError;

        Assert.True(
            process.ExitCode == 0,
            $"ControlCatalog exited with {process.ExitCode}.{Environment.NewLine}{output}{Environment.NewLine}{error}");
    }

    private static IEnumerable<CalendarHeader> CaptureCalendarHeaders(string trace)
    {
        foreach (Match match in CalendarHeaderCommandRegex().Matches(trace))
        {
            float width = ParseSingle(match.Groups["width"].Value);
            float height = ParseSingle(match.Groups["height"].Value);

            if (width > 150 && height >= 35 && height <= 45)
            {
                yield return new CalendarHeader(
                    ParseSingle(match.Groups["x"].Value),
                    ParseSingle(match.Groups["y"].Value),
                    width,
                    height);
            }
        }
    }

    private static IEnumerable<CalendarContext> CaptureCalendarContexts(string captureDirectory)
    {
        foreach (string tracePath in Directory.GetFiles(captureDirectory, "imagesharp-trace-*.log"))
        {
            Match traceMatch = TraceContextRegex().Match(Path.GetFileNameWithoutExtension(tracePath));
            if (!traceMatch.Success)
            {
                continue;
            }

            CalendarHeader[] headers = CaptureCalendarHeaders(File.ReadAllText(tracePath))
                .OrderBy(x => x.Y)
                .ThenBy(x => x.X)
                .ToArray();

            if (headers.Length == 0)
            {
                continue;
            }

            string contextId = traceMatch.Groups["context"].Value;
            string[] imagePaths = Directory.GetFiles(captureDirectory, $"imagesharp-context-*-{contextId}-*.png");
            Assert.NotEmpty(imagePaths);

            yield return new CalendarContext(
                imagePaths.OrderByDescending(File.GetLastWriteTimeUtc).First(),
                headers);
        }
    }

    private static int CountDarkPixels(Image<Bgra32> image, Rectangle bounds)
    {
        int count = 0;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                Bgra32 pixel = image[x, y];
                if (pixel.R <= 40 && pixel.G <= 40 && pixel.B <= 40 && pixel.A == byte.MaxValue)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            string sampleProjectPath = Path.Combine(directory.FullName, "samples", "AvaloniaControlCatalog", "AvaloniaControlCatalog.csproj");

            if (File.Exists(sampleProjectPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate the ImageSharp.Drawing repository root.");
    }

    private static string GetCurrentConfiguration()
        => Directory.GetParent(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!.Name;

    private static float ParseSingle(string value)
        => float.Parse(value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"DrawRectangle rect=\(0,0,(?<width>[-0-9.]+),(?<height>[-0-9.]+)\).*?brush=Solid\(#00FFFFFF.*?transform=\[1,0,0,1,(?<x>[-0-9.]+),(?<y>[-0-9.]+)\]")]
    private static partial Regex CalendarHeaderCommandRegex();

    [GeneratedRegex(@"-(?<context>\d+)$")]
    private static partial Regex TraceContextRegex();

    private readonly struct CalendarContext
    {
        public CalendarContext(string imagePath, CalendarHeader[] headers)
        {
            this.ImagePath = imagePath;
            this.Headers = headers;
        }

        public string ImagePath { get; }

        public CalendarHeader[] Headers { get; }
    }

    private readonly struct CalendarHeader
    {
        public CalendarHeader(float x, float y, float width, float height)
        {
            this.X = x;
            this.Y = y;
            this.Width = width;
            this.Height = height;
        }

        public float X { get; }

        public float Y { get; }

        public float Width { get; }

        public float Height { get; }

        public Rectangle Bounds
            => Rectangle.FromLTRB(
                (int)MathF.Round(this.X),
                (int)MathF.Round(this.Y),
                (int)MathF.Round(this.X + this.Width),
                (int)MathF.Round(this.Y + this.Height));
    }

    private sealed class WindowsTheoryAttribute : TheoryAttribute
    {
        public WindowsTheoryAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                this.Skip = "Calendar command capture runs only on Windows.";
            }
        }
    }
}
