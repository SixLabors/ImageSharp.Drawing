// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using Avalonia.Platform;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// Provides application builder extensions for the ImageSharp.Drawing renderer.
/// </summary>
public static class ImageSharpDrawingAppBuilderExtensions
{
    /// <summary>
    /// Registers the ImageSharp.Drawing rendering and SixLabors.Fonts text shaping
    /// subsystems with the application builder.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="backendMode">The rendering backend selection.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    public static AppBuilder UseImageSharpDrawing(this AppBuilder builder, DrawingBackendMode backendMode)
    {
        // Avalonia registers one rendering subsystem for the application, so the backend selection
        // must be fixed before Avalonia creates any platform rendering contexts.
        DrawingContextImpl.BackendMode = backendMode;

        return builder
            .UseRenderingSubsystem(PlatformRenderInterface.Initialize, "ImageSharp.Drawing")
            .UseTextShapingSubsystem(
                () => AvaloniaLocator.CurrentMutable.Bind<ITextShaperImpl>().ToConstant(new TextShaperImpl()),
                "SixLabors.Fonts");
    }
}
