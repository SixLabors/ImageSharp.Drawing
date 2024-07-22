// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_332
{
    [Fact]
    public void CanAccessEmptyRichTextRuns()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 70);
        RichTextOptions options = new(font);
        Assert.Empty(options.TextRuns);
    }
}
