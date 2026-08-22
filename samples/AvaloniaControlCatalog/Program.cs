// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System;
using Avalonia;
using ControlCatalog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

namespace AvaloniaControlCatalog;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    private static AppBuilder BuildAvaloniaApp()
    {
        DrawingBackendMode backendMode = Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_BACKEND")?.ToLowerInvariant() switch
        {
            "cpu" => DrawingBackendMode.Cpu,
            "webgpu" => DrawingBackendMode.WebGpu,
            _ => DrawingBackendMode.Auto
        };

        if (int.TryParse(Environment.GetEnvironmentVariable("IMAGESHARP_DRAWING_MAX_PARALLELISM"), out int maxParallelism))
        {
            Configuration.Default.MaxDegreeOfParallelism = maxParallelism;
        }

        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

        if (backendMode == DrawingBackendMode.Cpu)
        {
            // This sample-only override must run before Avalonia creates the backend context;
            // otherwise Win32 creates a platform graphics context and the top-level surfaces
            // represent the GPU/composited path instead of the framebuffer path we need to test.
            builder = builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software]
            });
        }

        // Temporary backend switch for visual comparison: ISD_BACKEND=skia uses Avalonia's default (Skia).
        if (!string.Equals(Environment.GetEnvironmentVariable("ISD_BACKEND"), "skia", StringComparison.OrdinalIgnoreCase))
        {
            builder = builder.UseImageSharpDrawing(backendMode);
        }

        return builder;
    }
}
