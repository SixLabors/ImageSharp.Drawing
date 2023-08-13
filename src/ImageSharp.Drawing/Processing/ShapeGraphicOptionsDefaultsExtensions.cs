// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the configuration of <see cref="ShapeOptions"/>.
    /// </summary>
    public static class ShapeGraphicOptionsDefaultsExtensions
    {
        /// <summary>
        /// Sets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to store default against.</param>
        /// <param name="optionsBuilder">The action to update instance of the default options used.</param>
        /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
        public static IImageProcessingContext SetShapeOptions(this IImageProcessingContext context, Action<ShapeOptions> optionsBuilder)
        {
            ShapeOptions cloned = context.GetShapeOptions().DeepClone();
            optionsBuilder(cloned);
            context.Properties[typeof(ShapeOptions)] = cloned;
            return context;
        }

        /// <summary>
        /// Sets the default shape processing options against the configuration.
        /// </summary>
        /// <param name="configuration">The configuration to store default against.</param>
        /// <param name="optionsBuilder">The default options to use.</param>
        public static void SetShapeOptions(this Configuration configuration, Action<ShapeOptions> optionsBuilder)
        {
            ShapeOptions cloned = configuration.GetShapeOptions().DeepClone();
            optionsBuilder(cloned);
            configuration.Properties[typeof(ShapeOptions)] = cloned;
        }

        /// <summary>
        /// Sets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to store default against.</param>
        /// <param name="options">The default options to use.</param>
        /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
        public static IImageProcessingContext SetShapeOptions(this IImageProcessingContext context, ShapeOptions options)
        {
            context.Properties[typeof(ShapeOptions)] = options;
            return context;
        }

        /// <summary>
        /// Sets the default shape processing options against the configuration.
        /// </summary>
        /// <param name="configuration">The configuration to store default against.</param>
        /// <param name="options">The default options to use.</param>
        public static void SetShapeOptions(this Configuration configuration, ShapeOptions options)
            => configuration.Properties[typeof(ShapeOptions)] = options;

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to retrieve defaults from.</param>
        /// <returns>The globally configured default options.</returns>
        public static ShapeOptions GetShapeOptions(this IImageProcessingContext context)
        {
            if (context.Properties.TryGetValue(typeof(ShapeOptions), out object options) && options is ShapeOptions go)
            {
                return go;
            }

            ShapeOptions configOptions = context.Configuration.GetShapeOptions();

            // do not cache the fall back to config into the the processing context
            // in case someone want to change the value on the config and expects it reflow thru
            return configOptions;
        }

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="configuration">The configuration to retrieve defaults from.</param>
        /// <returns>The globally configured default options.</returns>
        public static ShapeOptions GetShapeOptions(this Configuration configuration)
        {
            if (configuration.Properties.TryGetValue(typeof(ShapeOptions), out object options) && options is ShapeOptions go)
            {
                return go;
            }

            var configOptions = new ShapeOptions();

            // capture the fallback so the same instance will always be returned in case its mutated
            configuration.Properties[typeof(ShapeOptions)] = configOptions;
            return configOptions;
        }
    }
}
