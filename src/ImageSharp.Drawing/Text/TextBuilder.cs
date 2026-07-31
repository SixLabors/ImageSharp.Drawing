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
    /// <returns>
    /// The combined <see cref="IPathCollection"/> for the rendered glyphs.
    /// </returns>
    public static IPathCollection GeneratePaths(string text, TextOptions textOptions)
    {
        using GlyphBuilder glyphBuilder = new();
        TextRenderer renderer = new(glyphBuilder);

        renderer.Render(text, textOptions);

        return glyphBuilder.Paths;
    }

    /// <summary>
    /// Generates the combined outline paths for positioned glyphs.
    /// The result merges per-glyph outlines into a single <see cref="IPathCollection"/> suitable for filling or stroking as one unit.
    /// </summary>
    /// <param name="glyphIds">The glyph identifiers.</param>
    /// <param name="points">The absolute glyph origins in pixel units.</param>
    /// <param name="glyphOptions">The glyph rendering options.</param>
    /// <returns>
    /// The combined <see cref="IPathCollection"/> for the rendered glyphs.
    /// </returns>
    public static IPathCollection GeneratePaths(ReadOnlySpan<ushort> glyphIds, ReadOnlySpan<Vector2> points, GlyphOptions glyphOptions)
    {
        using GlyphBuilder glyphBuilder = new();
        TextRenderer renderer = new(glyphBuilder);

        renderer.Render(glyphIds, points, glyphOptions);

        return glyphBuilder.Paths;
    }

    /// <summary>
    /// Generates per-glyph path data and metadata for positioned glyphs.
    /// Each entry contains the combined outline paths for a glyph and associated metadata that enables intelligent fill or stroke decisions at the glyph level.
    /// </summary>
    /// <param name="glyphIds">The glyph identifiers.</param>
    /// <param name="points">The absolute glyph origins in pixel units.</param>
    /// <param name="glyphOptions">The glyph rendering options.</param>
    /// <returns>
    /// A read-only list of <see cref="GlyphPathCollection"/> entries, one for each rendered glyph.
    /// </returns>
    public static IReadOnlyList<GlyphPathCollection> GenerateGlyphs(ReadOnlySpan<ushort> glyphIds, ReadOnlySpan<Vector2> points, GlyphOptions glyphOptions)
    {
        using GlyphBuilder glyphBuilder = new();
        TextRenderer renderer = new(glyphBuilder);

        renderer.Render(glyphIds, points, glyphOptions);

        return glyphBuilder.Glyphs;
    }

    /// <summary>
    /// Generates per-glyph path data and metadata for the rendered <paramref name="text"/>.
    /// Each entry contains the combined outline paths for a glyph and associated metadata that enables intelligent fill or stroke decisions at the glyph level.
    /// </summary>
    /// <param name="text">The text to shape and render.</param>
    /// <param name="textOptions">The text rendering and layout options.</param>
    /// <returns>
    /// A read-only list of <see cref="GlyphPathCollection"/> entries, one for each rendered glyph.
    /// </returns>
    public static IReadOnlyList<GlyphPathCollection> GenerateGlyphs(string text, TextOptions textOptions)
    {
        using GlyphBuilder glyphBuilder = new();
        TextRenderer renderer = new(glyphBuilder);

        renderer.Render(text, textOptions);

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
    /// <returns>
    /// The combined <see cref="IPathCollection"/> for the rendered glyphs.
    /// </returns>
    public static IPathCollection GeneratePaths(string text, IPath path, TextOptions textOptions)
    {
        (IPath Path, TextOptions TextOptions) transformed = ConfigureOptions(textOptions, path);
        using GlyphBuilder glyphBuilder = new(transformed.Path);
        TextRenderer renderer = new(glyphBuilder);

        renderer.Render(text, transformed.TextOptions);

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
    /// <returns>
    /// A read-only list of <see cref="GlyphPathCollection"/> entries, one for each rendered glyph.
    /// </returns>
    public static IReadOnlyList<GlyphPathCollection> GenerateGlyphs(string text, IPath path, TextOptions textOptions)
    {
        (IPath Path, TextOptions TextOptions) transformed = ConfigureOptions(textOptions, path);
        using GlyphBuilder glyphBuilder = new(transformed.Path);
        TextRenderer renderer = new(glyphBuilder);

        renderer.Render(text, transformed.TextOptions);

        return glyphBuilder.Glyphs;
    }

    /// <summary>
    /// Normalizes options for path-based layout by moving any origin offset from the
    /// text options onto the path itself.
    /// </summary>
    /// <param name="options">The source text options.</param>
    /// <param name="path">The layout path.</param>
    /// <returns>
    /// The (possibly translated) path and matching options with a zero origin. When the
    /// origin is already zero the original instances are returned unchanged.
    /// </returns>
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
