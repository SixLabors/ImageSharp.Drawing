// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow configuring the drawing backend implementation.
/// </summary>
internal static class RasterizerDefaultsExtensions
{
    /// <summary>
    /// Sets the drawing backend against the source image processing context.
    /// </summary>
    /// <param name="context">The image processing context to store the backend against.</param>
    /// <param name="backend">The backend to use.</param>
    /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
    internal static IImageProcessingContext SetDrawingBackend(this IImageProcessingContext context, IDrawingBackend backend)
    {
        Guard.NotNull(backend, nameof(backend));
        context.Properties[typeof(IDrawingBackend)] = backend;

        return context;
    }

    /// <summary>
    /// Sets the default drawing backend against the configuration.
    /// </summary>
    /// <param name="configuration">The configuration to store the backend against.</param>
    /// <param name="backend">The backend to use.</param>
    internal static void SetDrawingBackend(this Configuration configuration, IDrawingBackend backend)
    {
        Guard.NotNull(backend, nameof(backend));
        configuration.Properties[typeof(IDrawingBackend)] = backend;
    }

    /// <summary>
    /// Gets the drawing backend from the source image processing context.
    /// </summary>
    /// <param name="context">The image processing context to retrieve the backend from.</param>
    /// <returns>The configured backend.</returns>
    internal static IDrawingBackend GetDrawingBackend(this IImageProcessingContext context)
    {
        if (context.Properties.TryGetValue(typeof(IDrawingBackend), out object? backend) &&
            backend is IDrawingBackend configured)
        {
            return configured;
        }

        return context.Configuration.GetDrawingBackend();
    }

    /// <summary>
    /// Gets the default drawing backend from the configuration.
    /// </summary>
    /// <param name="configuration">The configuration to retrieve the backend from.</param>
    /// <returns>The configured backend.</returns>
    internal static IDrawingBackend GetDrawingBackend(this Configuration configuration)
    {
        if (configuration.Properties.TryGetValue(typeof(IDrawingBackend), out object? backend) &&
            backend is IDrawingBackend configured)
        {
            return configured;
        }

        IDrawingBackend defaultBackend = DefaultDrawingBackend.Instance;
        configuration.Properties[typeof(IDrawingBackend)] = defaultBackend;
        return defaultBackend;
    }
}
