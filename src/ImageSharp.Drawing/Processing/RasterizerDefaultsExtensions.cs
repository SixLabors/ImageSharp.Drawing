// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;
using SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that allow configuring the path rasterizer implementation.
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

        if (backend is CpuDrawingBackend cpuBackend)
        {
            context.Properties[typeof(IRasterizer)] = cpuBackend.PrimaryRasterizer;
        }

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

        if (backend is CpuDrawingBackend cpuBackend)
        {
            configuration.Properties[typeof(IRasterizer)] = cpuBackend.PrimaryRasterizer;
        }
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

        if (context.Properties.TryGetValue(typeof(IRasterizer), out object? rasterizer) &&
            rasterizer is IRasterizer configuredRasterizer)
        {
            return CpuDrawingBackend.Create(configuredRasterizer);
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

        if (configuration.Properties.TryGetValue(typeof(IRasterizer), out object? rasterizer) &&
            rasterizer is IRasterizer configuredRasterizer)
        {
            IDrawingBackend rasterizerBackend = CpuDrawingBackend.Create(configuredRasterizer);
            configuration.Properties[typeof(IDrawingBackend)] = rasterizerBackend;
            return rasterizerBackend;
        }

        IDrawingBackend defaultBackend = CpuDrawingBackend.Instance;
        configuration.Properties[typeof(IDrawingBackend)] = defaultBackend;
        return defaultBackend;
    }

    /// <summary>
    /// Sets the rasterizer against the source image processing context.
    /// </summary>
    /// <param name="context">The image processing context to store the rasterizer against.</param>
    /// <param name="rasterizer">The rasterizer to use.</param>
    /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
    internal static IImageProcessingContext SetRasterizer(this IImageProcessingContext context, IRasterizer rasterizer)
    {
        Guard.NotNull(rasterizer, nameof(rasterizer));
        context.Properties[typeof(IRasterizer)] = rasterizer;
        context.Properties[typeof(IDrawingBackend)] = CpuDrawingBackend.Create(rasterizer);
        return context;
    }

    /// <summary>
    /// Sets the default rasterizer against the configuration.
    /// </summary>
    /// <param name="configuration">The configuration to store the rasterizer against.</param>
    /// <param name="rasterizer">The rasterizer to use.</param>
    internal static void SetRasterizer(this Configuration configuration, IRasterizer rasterizer)
    {
        Guard.NotNull(rasterizer, nameof(rasterizer));
        configuration.Properties[typeof(IRasterizer)] = rasterizer;
        configuration.Properties[typeof(IDrawingBackend)] = CpuDrawingBackend.Create(rasterizer);
    }

    /// <summary>
    /// Gets the rasterizer from the source image processing context.
    /// </summary>
    /// <param name="context">The image processing context to retrieve the rasterizer from.</param>
    /// <returns>The configured rasterizer.</returns>
    internal static IRasterizer GetRasterizer(this IImageProcessingContext context)
    {
        if (context.Properties.TryGetValue(typeof(IRasterizer), out object? rasterizer) &&
            rasterizer is IRasterizer configured)
        {
            return configured;
        }

        if (context.Properties.TryGetValue(typeof(IDrawingBackend), out object? backend) &&
            backend is CpuDrawingBackend cpuBackend)
        {
            return cpuBackend.PrimaryRasterizer;
        }

        // Do not cache config fallback in the context so changes on configuration reflow.
        return context.Configuration.GetRasterizer();
    }

    /// <summary>
    /// Gets the default rasterizer from the configuration.
    /// </summary>
    /// <param name="configuration">The configuration to retrieve the rasterizer from.</param>
    /// <returns>The configured rasterizer.</returns>
    internal static IRasterizer GetRasterizer(this Configuration configuration)
    {
        if (configuration.Properties.TryGetValue(typeof(IRasterizer), out object? rasterizer) &&
            rasterizer is IRasterizer configured)
        {
            return configured;
        }

        if (configuration.Properties.TryGetValue(typeof(IDrawingBackend), out object? backend) &&
            backend is CpuDrawingBackend cpuBackend)
        {
            return cpuBackend.PrimaryRasterizer;
        }

        IRasterizer defaultRasterizer = DefaultRasterizer.Instance;
        configuration.Properties[typeof(IRasterizer)] = defaultRasterizer;
        configuration.Properties[typeof(IDrawingBackend)] = CpuDrawingBackend.Instance;
        return defaultRasterizer;
    }
}
