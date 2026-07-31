// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using Avalonia.Platform;

namespace AvaloniaControlCatalog;

/// <summary>
/// Provides application builder extensions for the ImageSharp.Drawing sample renderer.
/// </summary>
internal static class ImageSharpDrawingAppBuilderExtensions
{
    /// <summary>
    /// Registers the ImageSharp.Drawing rendering and SixLabors.Fonts text shaping
    /// subsystems with the application builder.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The supplied <paramref name="builder"/>.</returns>
    public static AppBuilder UseImageSharpDrawing(this AppBuilder builder)
        => builder
            .UseRenderingSubsystem(PlatformRenderInterface.Initialize, "ImageSharp.Drawing")
            .UseTextShapingSubsystem(
                () => AvaloniaLocator.CurrentMutable.Bind<ITextShaperImpl>().ToConstant(new TextShaperImpl()),
                "SixLabors.Fonts");
}
