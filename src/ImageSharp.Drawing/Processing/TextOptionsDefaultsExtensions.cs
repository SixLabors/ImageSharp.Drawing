// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Processing
{
    /// <summary>
    /// Adds extensions that allow the processing of images to the <see cref="Image{TPixel}"/> type.
    /// </summary>
    public static class TextOptionsDefaultsExtensions
    {
        /// <summary>
        /// Sets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to store default against.</param>
        /// <param name="optionsBuilder">The action to update instance of the default options used.</param>
        /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
        public static IImageProcessingContext SetTextOptions(this IImageProcessingContext context, Action<TextOptions> optionsBuilder)
        {
            var cloned = context.GetTextOptions().DeepClone();
            optionsBuilder(cloned);
            context.Properties[typeof(TextOptions)] = cloned;
            return context;
        }

        /// <summary>
        /// Sets the default shape processing options against the configuration.
        /// </summary>
        /// <param name="configuration">The configuration to store default against.</param>
        /// <param name="optionsBuilder">The default options to use.</param>
        public static void SetTextOptions(this Configuration configuration, Action<TextOptions> optionsBuilder)
        {
            var cloned = configuration.GetTextOptions().DeepClone();
            optionsBuilder(cloned);
            configuration.Properties[typeof(TextOptions)] = cloned;
        }

        /// <summary>
        /// Sets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to store default against.</param>
        /// <param name="options">The default options to use.</param>
        /// <returns>The passed in <paramref name="context"/> to allow chaining.</returns>
        public static IImageProcessingContext SetTextOptions(this IImageProcessingContext context, TextOptions options)
        {
            context.Properties[typeof(TextOptions)] = options;
            return context;
        }

        /// <summary>
        /// Sets the default shape processing options against the configuration.
        /// </summary>
        /// <param name="configuration">The configuration to store default against.</param>
        /// <param name="options">The default options to use.</param>
        public static void SetTextOptions(this Configuration configuration, TextOptions options)
        {
            configuration.Properties[typeof(TextOptions)] = options;
        }

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to retrieve defaults from.</param>
        /// <returns>The globaly configued default options.</returns>
        public static TextGraphicsOptions GetTextGraphicsOptions(this IImageProcessingContext context)
            => new TextGraphicsOptions(context.GetGraphicsOptions(), context.GetTextOptions());

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="context">The image processing context to retrieve defaults from.</param>
        /// <returns>The globaly configued default options.</returns>
        public static TextOptions GetTextOptions(this IImageProcessingContext context)
        {
            if (context.Properties.TryGetValue(typeof(TextOptions), out var options) && options is TextOptions go)
            {
                return go;
            }

            var configOptions = context.Configuration.GetTextOptions();

            // do not cache the fall back to config into the the processing context
            // in case someone want to change the value on the config and expects it re trflow thru
            return configOptions;
        }

        /// <summary>
        /// Gets the default shape processing options against the image processing context.
        /// </summary>
        /// <param name="configuration">The configuration to retrieve defaults from.</param>
        /// <returns>The globaly configued default options.</returns>
        public static TextOptions GetTextOptions(this Configuration configuration)
        {
            if (configuration.Properties.TryGetValue(typeof(TextOptions), out var options) && options is TextOptions go)
            {
                return go;
            }

            var configOptions = new TextOptions();

            // capture the fallback so the same instance will always be returned in case its mutated
            configuration.Properties[typeof(TextOptions)] = configOptions;
            return configOptions;
        }
    }
}
