// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.FontGenerator;

/// <summary>
/// The class of character a glyph belongs to. The standards dimension the nominal printed image per
/// class, so a glyph is normalized onto the height its class stands at.
/// </summary>
internal enum GlyphClass
{
    /// <summary>
    /// A capital letter, and the default for a character the standards draw against the capitals, such
    /// as a bracket, a brace or an arithmetic sign.
    /// </summary>
    Capital,

    /// <summary>
    /// A digit.
    /// </summary>
    Digit,

    /// <summary>
    /// A small letter that rises to the ascender line.
    /// </summary>
    Ascender,

    /// <summary>
    /// A small letter that stands on the small letter line.
    /// </summary>
    SmallLetter,

    /// <summary>
    /// A small letter that hangs to the descender line.
    /// </summary>
    Descender,

    /// <summary>
    /// A character the standards dimension in millimetres rather than draw, such as the vertical line
    /// and the character erase. Those carry their own absolute size and are not normalized.
    /// </summary>
    Absolute,
}
