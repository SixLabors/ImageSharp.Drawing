// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Adds extensions that control whether glyph-outline caching is shared across all
/// <c>DrawText</c> calls on a canvas (per-canvas) rather than rebuilt per call.
/// </summary>
public static class GlyphCacheDefaultsExtensions
{
    /// <summary>
    /// Sets whether canvases created from this configuration share a single glyph-outline cache
    /// across every <c>DrawText</c> call (per-canvas), instead of using a private cache per call.
    /// </summary>
    /// <remarks>
    /// Sharing lets a glyph outline built once be reused by later text runs on the same canvas,
    /// which can help text-heavy output, at the cost of sub-pixel-quantized reuse across calls.
    /// Disabled by default.
    /// </remarks>
    /// <param name="configuration">The configuration to store the setting against.</param>
    /// <param name="enabled"><see langword="true"/> to share the glyph cache per canvas.</param>
    public static void SetSharedGlyphCache(this Configuration configuration, bool enabled)
    {
        Guard.NotNull(configuration, nameof(configuration));
        configuration.Properties[typeof(SharedGlyphCacheKey)] = enabled;
    }

    /// <summary>
    /// Gets whether canvases created from this configuration share a glyph-outline cache per canvas.
    /// </summary>
    /// <param name="configuration">The configuration to read the setting from.</param>
    /// <returns><see langword="true"/> if sharing is enabled; otherwise <see langword="false"/> (the default).</returns>
    internal static bool GetSharedGlyphCache(this Configuration configuration)
        => configuration.Properties.TryGetValue(typeof(SharedGlyphCacheKey), out object? value) && value is true;

    // Dedicated marker type used as the Configuration.Properties key, mirroring how the
    // pluggable drawing backend keys its entry on typeof(IDrawingBackend).
    private sealed class SharedGlyphCacheKey
    {
    }
}
