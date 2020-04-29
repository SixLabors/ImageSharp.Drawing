// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the processing of images to the <see cref="Image{TPixel}"/> type.
    /// </summary>
    public static class TextGraphicOptionsDefaultsExtensions
    {
        /// <summary>
        /// Sets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to store default against.</param>
        /// <param name="optionsBuilder">The action to update instance of the default options used.</param>
        /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
        public static IImageProcessingContext SetTextGraphicsOptions(this IImageProcessingContext context, Action<TextGraphicsOptions> optionsBuilder)
        {
            var cloned = context.GetTextGraphicsOptions().DeepClone();
            optionsBuilder(cloned);
            context.Properties[typeof(TextGraphicsOptions)] = cloned;
            return context;
        }

        /// <summary>
        /// Sets the default shape processing options against the configuration.
        /// </summary>
        /// <param name="configuration">The configuration to store default against.</param>
        /// <param name="optionsBuilder">The default options to use.</param>
        public static void SetTextGraphicsOptions(this Configuration configuration, Action<TextGraphicsOptions> optionsBuilder)
        {
            var cloned = configuration.GetTextGraphicsOptions().DeepClone();
            optionsBuilder(cloned);
            configuration.Properties[typeof(TextGraphicsOptions)] = cloned;
        }

        /// <summary>
        /// Sets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to store default against.</param>
        /// <param name="options">The default options to use.</param>
        /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
        public static IImageProcessingContext SetTextGraphicsOptions(this IImageProcessingContext context, TextGraphicsOptions options)
        {
            context.Properties[typeof(TextGraphicsOptions)] = options;
            return context;
        }

        /// <summary>
        /// Sets the default shape processing options against the configuration.
        /// </summary>
        /// <param name="configuration">The configuration to store default against.</param>
        /// <param name="options">The default options to use.</param>
        public static void SetTextGraphicsOptions(this Configuration configuration, TextGraphicsOptions options)
        {
            configuration.Properties[typeof(TextGraphicsOptions)] = options;
        }

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to retrieve defaults from.</param>
        /// <returns>The globaly configued default options.</returns>
        public static TextGraphicsOptions GetTextGraphicsOptions(this IImageProcessingContext context)
        {
            if (context.Properties.TryGetValue(typeof(TextGraphicsOptions), out var options) && options is TextGraphicsOptions go)
            {
                return go;
            }

            var configOptions = context.Configuration.GetTextGraphicsOptions();

            // do not cache the fall back to config into the the processing context
            // in case someone want to change the value on the config and expects it re trflow thru
            return configOptions;
        }

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="configuration">The configuration to retrieve defaults from.</param>
        /// <returns>The globaly configued default options.</returns>
        public static TextGraphicsOptions GetTextGraphicsOptions(this Configuration configuration)
        {
            if (configuration.Properties.TryGetValue(typeof(TextGraphicsOptions), out var options) && options is TextGraphicsOptions go)
            {
                return go;
            }

            var configOptions = new TextGraphicsOptions(configuration.GetGraphicsOptions());

            // capture the fallback so the same instance will always be returned in case its mutated
            configuration.Properties[typeof(TextGraphicsOptions)] = configOptions;
            return configOptions;
        }
    }
}
