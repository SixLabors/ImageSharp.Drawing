// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.Fonts;
using SixLabors.Primitives;
using SixLabors.Shapes.Text;

namespace SixLabors.Shapes
{
    /// <summary>
    /// Text drawing extensions for a PathBuilder
    /// </summary>
    public static class TextBuilder
    {
        /// <summary>
        /// Generates the shapes corresponding the glyphs described by the font and with the settings withing the FontSpan
        /// </summary>
        /// <param name="text">The text to generate glyphs for</param>
        /// <param name="location">The location</param>
        /// <param name="style">The style and settings to use while rendering the glyphs</param>
        /// <returns>The <see cref="IPathCollection"/></returns>
        public static IPathCollection GenerateGlyphs(string text, PointF location, RendererOptions style)
        {
            var glyphBuilder = new GlyphBuilder(location);

            TextRenderer renderer = new TextRenderer(glyphBuilder);

            renderer.RenderText(text, style);

            return glyphBuilder.Paths;
        }

        /// <summary>
        /// Generates the shapes corresponding the glyphs described by the font and with the settings withing the FontSpan
        /// </summary>
        /// <param name="text">The text to generate glyphs for</param>
        /// <param name="style">The style and settings to use while rendering the glyphs</param>
        /// <returns>The <see cref="IPathCollection"/></returns>
        public static IPathCollection GenerateGlyphs(string text, RendererOptions style)
        {
            return GenerateGlyphs(text, PointF.Empty, style);
        }

        /// <summary>
        /// Generates the shapes corresponding the glyphs described by the font and with the setting in within the FontSpan along the described path.
        /// </summary>
        /// <param name="text">The text to generate glyphs for</param>
        /// <param name="path">The path to draw the text in relation to</param>
        /// <param name="style">The style and settings to use while rendering the glyphs</param>
        /// <returns>The <see cref="IPathCollection"/></returns>
        public static IPathCollection GenerateGlyphs(string text, IPath path, RendererOptions style)
        {
            var glyphBuilder = new PathGlyphBuilder(path);

            TextRenderer renderer = new TextRenderer(glyphBuilder);

            renderer.RenderText(text, style);

            return glyphBuilder.Paths;
        }
    }
}
