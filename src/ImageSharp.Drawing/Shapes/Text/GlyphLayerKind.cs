// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.Text;

/// <summary>
/// Optional semantic classification for layers to aid monochrome projection or decoration handling.
/// </summary>
public enum GlyphLayerKind
{
    /// <summary>
    /// Regular glyph geometry layer.
    /// </summary>
    Glyph = 0,

    /// <summary>
    /// Text decoration geometry (underline/overline/strikethrough).
    /// </summary>
    Decoration = 1,

    /// <summary>
    /// Painted layer (e.g. color emoji glyph).
    /// </summary>
    Painted = 2
}
