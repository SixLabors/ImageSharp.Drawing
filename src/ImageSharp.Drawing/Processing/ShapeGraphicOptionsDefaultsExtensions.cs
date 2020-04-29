// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the processing of images to the <see cref="Image{TPixel}"/> type.
    /// </summary>
    public static class ShapeGraphicOptionsDefaultsExtensions
    {
        /// <summary>
        /// Sets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to store default against.</param>
        /// <param name="optionsBuilder">The action to update instance of the default options used.</param>
        /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
        public static IImageProcessingContext SetShapeGraphicsOptions(this IImageProcessingContext context, Action<ShapeGraphicsOptions> optionsBuilder)
        {
            var cloned = context.GetShapeGraphicsOptions().DeepClone();
            optionsBuilder(cloned);
            context.Properties[typeof(ShapeGraphicsOptions)] = cloned;
            return context;
        }

        /// <summary>
        /// Sets the default shape processing options against the configuration.
        /// </summary>
        /// <param name="configuration">The configuration to store default against.</param>
        /// <param name="optionsBuilder">The default options to use.</param>
        public static void SetShapeGraphicsOptions(this Configuration configuration, Action<ShapeGraphicsOptions> optionsBuilder)
        {
            var cloned = configuration.GetShapeGraphicsOptions().DeepClone();
            optionsBuilder(cloned);
            configuration.Properties[typeof(ShapeGraphicsOptions)] = cloned;
        }

        /// <summary>
        /// Sets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to store default against.</param>
        /// <param name="options">The default options to use.</param>
        /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
        public static IImageProcessingContext SetShapeGraphicsOptions(this IImageProcessingContext context, ShapeGraphicsOptions options)
        {
            context.Properties[typeof(ShapeGraphicsOptions)] = options;
            return context;
        }

        /// <summary>
        /// Sets the default shape processing options against the configuration.
        /// </summary>
        /// <param name="configuration">The configuration to store default against.</param>
        /// <param name="options">The default options to use.</param>
        public static void SetShapeGraphicsOptions(this Configuration configuration, ShapeGraphicsOptions options)
        {
            configuration.Properties[typeof(ShapeGraphicsOptions)] = options;
        }

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to retrieve defaults from.</param>
        /// <returns>The globaly configued default options.</returns>
        public static ShapeGraphicsOptions GetShapeGraphicsOptions(this IImageProcessingContext context)
        {
            if (context.Properties.TryGetValue(typeof(ShapeGraphicsOptions), out var options) && options is ShapeGraphicsOptions go)
            {
                return go;
            }

            var configOptions = context.Configuration.GetShapeGraphicsOptions();

            // do not cache the fall back to config into the the processing context
            // in case someone want to change the value on the config and expects it re trflow thru
            return configOptions;
        }

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="configuration">The configuration to retrieve defaults from.</param>
        /// <returns>The globaly configued default options.</returns>
        public static ShapeGraphicsOptions GetShapeGraphicsOptions(this Configuration configuration)
        {
            if (configuration.Properties.TryGetValue(typeof(ShapeGraphicsOptions), out var options) && options is ShapeGraphicsOptions go)
            {
                return go;
            }

            var configOptions = new ShapeGraphicsOptions(configuration.GetGraphicsOptions());

            // capture the fallback so the same instance will always be returned in case its mutated
            configuration.Properties[typeof(ShapeGraphicsOptions)] = configOptions;
            return configOptions;
        }
    }
}
