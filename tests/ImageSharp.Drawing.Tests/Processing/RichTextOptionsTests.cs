// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class RichTextOptionsTests
{
    [Fact]
    public void CopyConstructor_CopiesTextRunProperties()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 12);
        Brush brush = Brushes.Solid(Color.Red);
        Pen pen = Pens.Solid(Color.Blue, 2F);
        Pen strikeout = Pens.Solid(Color.Green, 1F);
        Pen underline = Pens.Solid(Color.Yellow, 1F);
        Pen overline = Pens.Solid(Color.Purple, 1F);

        RichTextOptions original = new(font)
        {
            TextRuns =
            [
                new RichTextRun
                {
                    Start = 2,
                    End = 10,
                    Brush = brush,
                    Pen = pen,
                    StrikeoutPen = strikeout,
                    UnderlinePen = underline,
                    OverlinePen = overline,
                }
            ]
        };

        RichTextOptions copy = new(original);

        Assert.Single(copy.TextRuns);
        RichTextRun copiedRun = copy.TextRuns[0];

        Assert.Equal(2, copiedRun.Start);
        Assert.Equal(10, copiedRun.End);
        Assert.Same(brush, copiedRun.Brush);
        Assert.Same(pen, copiedRun.Pen);
        Assert.Same(strikeout, copiedRun.StrikeoutPen);
        Assert.Same(underline, copiedRun.UnderlinePen);
        Assert.Same(overline, copiedRun.OverlinePen);
    }

    [Fact]
    public void CopyConstructor_DeepCopiesRunList()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 12);

        RichTextOptions original = new(font)
        {
            TextRuns =
            [
                new RichTextRun { Start = 0, End = 5, Brush = Brushes.Solid(Color.Red) }
            ]
        };

        RichTextOptions copy = new(original);

        // The list is a separate instance.
        Assert.NotSame(original.TextRuns, copy.TextRuns);

        // The run object is a separate instance (deep copy).
        Assert.NotSame(original.TextRuns[0], copy.TextRuns[0]);
    }

    [Fact]
    public void CopyConstructor_CopiesMultipleTextRuns()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 12);

        RichTextOptions original = new(font)
        {
            TextRuns =
            [
                new RichTextRun { Start = 0, End = 3, Brush = Brushes.Solid(Color.Red) },
                new RichTextRun { Start = 3, End = 7, Pen = Pens.Solid(Color.Blue, 1F) },
                new RichTextRun { Start = 7, End = 12, UnderlinePen = Pens.Solid(Color.Green, 1F) }
            ]
        };

        RichTextOptions copy = new(original);

        Assert.Equal(3, copy.TextRuns.Count);

        for (int i = 0; i < original.TextRuns.Count; i++)
        {
            Assert.Equal(original.TextRuns[i].Start, copy.TextRuns[i].Start);
            Assert.Equal(original.TextRuns[i].End, copy.TextRuns[i].End);
            Assert.NotSame(original.TextRuns[i], copy.TextRuns[i]);
        }
    }

    [Fact]
    public void CopyConstructor_CopiesPath()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 12);
        IPath path = new Polygon(new PointF[] { new(0, 0), new(100, 0), new(100, 100), new(0, 100) });

        RichTextOptions original = new(font) { Path = path };
        RichTextOptions copy = new(original);

        Assert.Same(path, copy.Path);
    }

    [Fact]
    public void CopyConstructor_EmptyTextRuns_ProducesEmptyList()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 12);
        RichTextOptions original = new(font);

        RichTextOptions copy = new(original);

        Assert.Empty(copy.TextRuns);
    }
}
