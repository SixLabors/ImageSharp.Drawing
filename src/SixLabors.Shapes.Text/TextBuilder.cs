using SixLabors.Fonts;
using SixLabors.Shapes.Text;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SixLabors.Shapes
{
    /// <summary>
    /// Text drawing extensions for a PathBuilder
    /// </summary>
    public static class TextBuilder
    {
        /// <summary>
        /// Generates the shapes corresponding the glyphs described by the font and with the setting ing withing the FontSpan
        /// </summary>
        /// <param name="text">The text to generate glyphs for</param>
        /// <param name="location">The location</param>
        /// <param name="style">The style and settings to use while rendering the glyphs</param>
        /// <returns></returns>
        public static IPathCollection GenerateGlyphs(string text, Vector2 location, FontSpan style)
        {
            var glyphBuilder = new GlyphBuilder(location);

            TextRenderer renderer = new TextRenderer(glyphBuilder);

            renderer.RenderText(text, style);

            return new PathCollection(glyphBuilder.Paths);
        }

        /// <summary>
        /// Generates the shapes corresponding the glyphs described by the font and with the setting ing withing the FontSpan
        /// </summary>
        /// <param name="text">The text to generate glyphs for</param>
        /// <param name="style">The style and settings to use while rendering the glyphs</param>
        /// <returns></returns>
        public static IPathCollection GenerateGlyphs(string text, FontSpan style)
        {
            return GenerateGlyphs(text, Vector2.Zero, style);
        }
    }

}
