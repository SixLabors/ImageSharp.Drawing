// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Text;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Provides mechanisms for building <see cref="IPathCollection"/> instances from text strings.
    /// </summary>
    public static class TextBuilder
    {
        /// <summary>
        /// Generates the shapes corresponding the glyphs described by the text options.
        /// </summary>
        /// <param name="text">The text to generate glyphs for.</param>
        /// <param name="textOptions">The text rendering options.</param>
        /// <returns>The <see cref="IPathCollection"/></returns>
        public static IPathCollection GenerateGlyphs(string text, TextOptions textOptions)
        {
            GlyphBuilder glyphBuilder = new();
            TextRenderer renderer = new(glyphBuilder);

            renderer.RenderText(text, textOptions);

            return glyphBuilder.Paths;
        }

        /// <summary>
        /// Generates the shapes corresponding the glyphs described by the text options along the described path.
        /// </summary>
        /// <param name="text">The text to generate glyphs for</param>
        /// <param name="path">The path to draw the text in relation to</param>
        /// <param name="textOptions">The text rendering options.</param>
        /// <returns>The <see cref="IPathCollection"/></returns>
        public static IPathCollection GenerateGlyphs(string text, IPath path, TextOptions textOptions)
        {
            TextOptions options = ConfigureOptions(textOptions, path);

            PathGlyphBuilder glyphBuilder = new(path, options);
            TextRenderer renderer = new(glyphBuilder);

            renderer.RenderText(text, options);

            return glyphBuilder.Paths;
        }

        private static TextOptions ConfigureOptions(TextOptions options, IPath path)
        {
            // When a path is specified we should explicitly follow that path
            // and not adjust the origin. Any translation should be applied to the path.
            if (path is not null && options.Origin != Vector2.Zero)
            {
                return new(options)
                {
                    Origin = Vector2.Zero
                };
            }

            return options;
        }
    }
}
