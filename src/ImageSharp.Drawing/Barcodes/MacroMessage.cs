// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The message formats 05 and 06, which Data Matrix and PDF417 compress into one Macro character. A message in either format starts with the header
/// [)&gt;RS05GS or [)&gt;RS06GS and ends with the trailer RS EOT.
/// </summary>
internal static class MacroMessage
{
    /// <summary>
    /// The length of the header, [)&gt;RS05GS or [)&gt;RS06GS.
    /// </summary>
    public const int HeaderLength = 7;

    /// <summary>
    /// The length of the trailer, RS EOT.
    /// </summary>
    public const int TrailerLength = 2;

    /// <summary>
    /// Returns the format of a message.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <returns>5 or 6 when the text has the header and the trailer of that format, or 0.</returns>
    public static int Format(ReadOnlySpan<char> text)
        => text.Length >= HeaderLength + TrailerLength
            && text.StartsWith("[)>\u001E0")
            && text[5] is '5' or '6'
            && text[6] == '\u001D'
            && text.EndsWith("\u001E\u0004")
            ? text[5] - '0'
            : 0;
}
