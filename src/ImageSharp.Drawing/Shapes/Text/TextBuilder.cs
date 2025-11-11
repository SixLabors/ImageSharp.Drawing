// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;

namespace SixLabors.ImageSharp.Drawing.Text;

/// <summary>
/// Builds vector shapes from text using the provided layout and rendering options.
/// </summary>
public static class TextBuilder
{
    /// <summary>
    /// Generates the combined outline paths for all rendered glyphs in <paramref name="text"/>.
    /// The result merges per-glyph outlines into a single <see cref="IPathCollection"/> suitable for filling or stroking as one unit.
    /// </summary>
    /// <param name="text">The text to shape and render.</param>
    /// <param name="textOptions">The text rendering and layout options.</param>
    /// <returns>The combined <see cref="IPathCollection"/> for the rendered glyphs.</returns>
    public static IPathCollection GeneratePaths(string text, TextOptions textOptions)
    {
        GlyphBuilder glyphBuilder = new();
        TextRenderer renderer = new(glyphBuilder);

        renderer.RenderText(text, textOptions);

        return glyphBuilder.Paths;
    }

    /// <summary>
    /// Generates per-glyph path data and metadata for the rendered <paramref name="text"/>.
    /// Each entry contains the combined outline paths for a glyph and associated metadata that enables intelligent fill or stroke decisions at the glyph level.
    /// </summary>
    /// <param name="text">The text to shape and render.</param>
    /// <param name="textOptions">The text rendering and layout options.</param>
    /// <returns>A read-only list of <see cref="GlyphPathCollection"/> entries, one for each rendered glyph.</returns>
    public static IReadOnlyList<GlyphPathCollection> GenerateGlyphs(string text, TextOptions textOptions)
    {
        GlyphBuilder glyphBuilder = new();
        TextRenderer renderer = new(glyphBuilder);

        renderer.RenderText(text, textOptions);

        return glyphBuilder.Glyphs;
    }

    /// <summary>
    /// Generates the combined outline paths for all rendered glyphs in <paramref name="text"/>,
    /// laid out along the supplied <paramref name="path"/> baseline.
    /// The result merges per-glyph outlines into a single <see cref="IPathCollection"/>.
    /// </summary>
    /// <param name="text">The text to shape and render.</param>
    /// <param name="path">The path that defines the text baseline.</param>
    /// <param name="textOptions">The text rendering and layout options.</param>
    /// <returns>The combined <see cref="IPathCollection"/> for the rendered glyphs.</returns>
    public static IPathCollection GeneratePaths(string text, IPath path, TextOptions textOptions)
    {
        (IPath Path, TextOptions TextOptions) transformed = ConfigureOptions(textOptions, path);
        PathGlyphBuilder glyphBuilder = new(transformed.Path);
        TextRenderer renderer = new(glyphBuilder);

        renderer.RenderText(text, transformed.TextOptions);

        return glyphBuilder.Paths;
    }

    /// <summary>
    /// Generates per-glyph path data and metadata for the rendered <paramref name="text"/>,
    /// laid out along the supplied <paramref name="path"/> baseline.
    /// Each entry contains the combined outline paths for a glyph and associated metadata.
    /// </summary>
    /// <param name="text">The text to shape and render.</param>
    /// <param name="path">The path that defines the text baseline.</param>
    /// <param name="textOptions">The text rendering and layout options.</param>
    /// <returns>A read-only list of <see cref="GlyphPathCollection"/> entries, one for each rendered glyph.</returns>
    public static IReadOnlyList<GlyphPathCollection> GenerateGlyphs(string text, IPath path, TextOptions textOptions)
    {
        (IPath Path, TextOptions TextOptions) transformed = ConfigureOptions(textOptions, path);
        PathGlyphBuilder glyphBuilder = new(transformed.Path);
        TextRenderer renderer = new(glyphBuilder);

        renderer.RenderText(text, transformed.TextOptions);

        return glyphBuilder.Glyphs;
    }

    private static (IPath Path, TextOptions TextOptions) ConfigureOptions(TextOptions options, IPath path)
    {
        // When a path is specified we should explicitly follow that path
        // and not adjust the origin. Any translation should be applied to the path.
        if (options.Origin != Vector2.Zero)
        {
            TextOptions clone = new(options)
            {
                Origin = Vector2.Zero
            };

            return (path.Translate(options.Origin), clone);
        }

        return (path, options);
    }
}
