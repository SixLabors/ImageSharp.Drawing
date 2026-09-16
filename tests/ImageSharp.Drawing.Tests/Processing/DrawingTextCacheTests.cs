// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using Moq;
using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class DrawingTextCacheTests
{
    /// <summary>
    /// Verifies exclusive buffer ownership across overlapping draws, clearing, and disposal.
    /// </summary>
    [Fact]
    public void OverlappingRenderers_ClearAndDisposePreserveOtherDraw()
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        DrawingTextCache cache = new();
        using RichTextGlyphRenderer first = new(new DrawingOptions(), null, null, Brushes.Solid(Color.Red), cache);
        TextRenderer.RenderTo(first, "Hello", new RichTextOptions(font));
        DrawingOperation[] expected = first.DrawingOperations.ToArray();

        using (RichTextGlyphRenderer second = new(new DrawingOptions(), null, null, Brushes.Solid(Color.Blue), cache))
        {
            // Hold both renderers live to test exclusive leases deterministically, without
            // depending on the scheduler to overlap the two operation lists.
            TextRenderer.RenderTo(second, "World", new RichTextOptions(font));
            cache.Clear();
            Assert.Equal(0, cache.Count);
            Assert.NotEmpty(second.DrawingOperations);
            Assert.NotSame(first.Scratch, second.Scratch);
        }

        Assert.NotEmpty(expected);
        Assert.Equal(expected, first.DrawingOperations);

        DrawingTextCache.DrawingScratch returned;
        using (RichTextGlyphRenderer next = new(new DrawingOptions(), null, null, Brushes.Solid(Color.Black), cache))
        {
            returned = next.Scratch;
            Assert.Empty(next.DrawingOperations);
            Assert.NotSame(first.Scratch, returned);
            TextRenderer.RenderTo(next, "H", new RichTextOptions(font));
        }

        using RichTextGlyphRenderer reused = new(new DrawingOptions(), null, null, Brushes.Solid(Color.Black), cache);
        Assert.Same(returned, reused.Scratch);
        Assert.Empty(reused.DrawingOperations);
        Assert.Equal(expected, first.DrawingOperations);
    }

    /// <summary>
    /// Verifies complete publication and abandonment of layered glyphs.
    /// </summary>
    /// <param name="completeGlyph">Whether to finish the glyph before disposing the renderer.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LayeredGlyph_PublishesOnlyAfterCompletion(bool completeGlyph)
    {
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        RichTextOptions options = new(font);
        FontRectangle bounds = default;
        GlyphRendererParameters parameters = default;
        Mock<IGlyphRenderer> capture = new();
        capture.Setup(x => x.BeginGlyph(in It.Ref<FontRectangle>.IsAny, in It.Ref<GlyphRendererParameters>.IsAny))
            .Callback(new InvocationAction(invocation =>
            {
                bounds = (FontRectangle)invocation.Arguments[0];
                parameters = (GlyphRendererParameters)invocation.Arguments[1];
            }))
            .Returns(false);

        // Obtain valid callback parameters through the font renderer, then stop between
        // layers deterministically rather than relying on a scheduler to expose the race.
        TextRenderer.RenderTo(capture.Object, "H", options);
        capture.Verify(x => x.BeginGlyph(in It.Ref<FontRectangle>.IsAny, in It.Ref<GlyphRendererParameters>.IsAny), Times.Once);
        DrawingTextCache cache = new();
        using (RichTextGlyphRenderer builder = new(new DrawingOptions(), null, null, Brushes.Solid(Color.Red), cache))
        {
            IGlyphRenderer callbacks = builder;
            callbacks.BeginText(bounds);
            Assert.True(callbacks.BeginGlyph(bounds, parameters));
            for (int layer = 0; layer < 2; layer++)
            {
                callbacks.BeginLayer(null, FillRule.NonZero);
                callbacks.BeginFigure();
                callbacks.MoveTo(new Vector2(layer * 20, 0));
                callbacks.LineTo(new Vector2((layer * 20) + 10, 0));
                callbacks.LineTo(new Vector2((layer * 20) + 10, 10));
                callbacks.LineTo(new Vector2(layer * 20, 10));
                callbacks.EndFigure();
                callbacks.EndLayer();
                Assert.Equal(0, cache.Count);
            }

            Assert.Equal(2, builder.DrawingOperations.Count);
            if (completeGlyph)
            {
                callbacks.EndGlyph();
                callbacks.EndText();
                Assert.Equal(1, cache.Count);
                using RichTextGlyphRenderer reader = new(new DrawingOptions(), null, null, Brushes.Solid(Color.Blue), cache);
                TextRenderer.RenderTo(reader, "H", options);
                Assert.Equal(2, reader.DrawingOperations.Count);
                for (int layer = 0; layer < 2; layer++)
                {
                    Assert.Same(builder.DrawingOperations[layer].Path, reader.DrawingOperations[layer].Path);
                }
            }
        }

        // Abandoning a draw must discard its incomplete glyph, not publish it from Dispose.
        Assert.Equal(completeGlyph ? 1 : 0, cache.Count);
    }

    /// <summary>
    /// Verifies duplicate publication preserves the first complete entry and eviction preserves acquired data.
    /// </summary>
    [Fact]
    public void Add_DuplicateCompletedGlyphPreservesFirstEntry()
    {
        DrawingTextCache cache = new(1);
        RichTextGlyphRenderer.CacheKey key = new() { Font = "test", GlyphId = 1 };
        List<RichTextGlyphRenderer.GlyphRenderData> first = [new() { FillPath = new RectanglePolygon(0, 0, 10, 10) }];
        List<RichTextGlyphRenderer.GlyphRenderData> second = [new() { FillPath = new RectanglePolygon(0, 0, 20, 20) }];

        // Two misses can finish the same glyph independently. Publishing the second must
        // neither append layers to the first nor replace the result another draw is reading.
        cache.Add(key, first);
        cache.Add(key, second);
        Assert.True(cache.TryGetValue(key, out List<RichTextGlyphRenderer.GlyphRenderData> actual));
        Assert.Same(first, actual);
        Assert.Single(actual);
        Assert.Equal(1, cache.Count);

        cache.Add(new RichTextGlyphRenderer.CacheKey { Font = "test", GlyphId = 2 }, second);
        Assert.False(cache.TryGetValue(key, out _));
        Assert.Equal(1, cache.Count);
        Assert.Single(actual);
    }

    /// <summary>
    /// Verifies concurrent drawing matches serial reuse of the same outlines at varied positions.
    /// </summary>
    [Fact]
    public async Task ConcurrentCanvases_MatchSerialOutputWithSameCachedOutlines()
    {
        const int workers = 4;
        Font font = TestFontUtilities.GetFont(TestFonts.OpenSans, 24);
        Font emojiFont = TestFontUtilities.GetFont(TestFonts.NotoColorEmojiRegular, 48);
        Assert.True(font.FontMetrics.TryGetGlyphMetrics(
            new CodePoint('H'),
            TextAttributes.None,
            TextDecorations.None,
            LayoutMode.HorizontalTopBottom,
            ColorFontSupport.None,
            null,
            out FontGlyphMetrics metrics));

        DrawingTextCache shared = new();
        DrawingTextCache serial = new();

        // Both caches must contain outlines built at the same positions. The assertion
        // compares concurrent and serial reuse, not reuse against newly built geometry.
        for (int index = 0; index < workers; index++)
        {
            using Image<Rgba32> warmShared = new(320, 160);
            using Image<Rgba32> warmSerial = new(320, 160);
            DrawSample(warmShared, shared, font, emojiFont, metrics.GlyphId, index);
            DrawSample(warmSerial, serial, font, emojiFont, metrics.GlyphId, index);
        }

        using Barrier start = new(workers);
        Task[] tasks = new Task[workers];
        for (int worker = 0; worker < workers; worker++)
        {
            int index = worker;
            // Barrier participants need dedicated threads so waiting for their peers does
            // not starve thread-pool work scheduled by other tests on small CI runners.
            tasks[worker] = Task.Factory.StartNew(() =>
            {
                using Image<Rgba32> expected = new(320, 160);
                lock (serial)
                {
                    DrawSample(expected, serial, font, emojiFont, metrics.GlyphId, index);
                }

                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(30)));

                for (int iteration = 0; iteration < 12; iteration++)
                {
                    using Image<Rgba32> actual = new(320, 160);
                    DrawSample(actual, shared, font, emojiFont, metrics.GlyphId, index);
                    ImageComparer.Exact.VerifySimilarity(expected, actual);
                    Assert.Equal(serial.Count, shared.Count);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Verifies competing publications and cache removal preserve acquired glyph and run data.
    /// </summary>
    /// <param name="capacity">The glyph cache capacity.</param>
    /// <param name="clearDuringDrawing">Whether competing writers also clear the cache.</param>
    [Theory]
    [InlineData(2, false)]
    [InlineData(DrawingTextCache.DefaultCapacity, false)]
    [InlineData(DrawingTextCache.DefaultCapacity, true)]
    public async Task ConcurrentPublication_EvictionAndClearPreserveAcquiredEntries(int capacity, bool clearDuringDrawing)
    {
        const int workers = 4;
        DrawingTextCache cache = new(capacity);
        using Barrier phase = new(workers);
        Task[] tasks = new Task[workers];
        for (int worker = 0; worker < workers; worker++)
        {
            int index = worker;
            // Barrier participants need dedicated threads so waiting for their peers does
            // not starve thread-pool work scheduled by other tests on small CI runners.
            tasks[worker] = Task.Factory.StartNew(() =>
            {
                IPath first = new RectanglePolygon(index, 0, 10, 10);
                IPath second = new RectanglePolygon(index, 10, 10, 10);
                List<RichTextGlyphRenderer.GlyphRenderData> entries =
                [
                    new() { FillPath = first },
                    new() { FillPath = second }
                ];

                for (int iteration = 0; iteration < 32; iteration++)
                {
                    RichTextGlyphRenderer.CacheKey key = new() { Font = "test", GlyphId = (ushort)iteration };
                    DrawingTextCache.RunPathCacheKey runKey = new(
                        [new DrawingTextCache.RunPathCacheEntry(first, Vector2.Zero, key, true)], 1);

                    // Race complete publications of the same key. No reader may see a
                    // mixture of entries from different producers or an incomplete list.
                    Assert.True(phase.SignalAndWait(TimeSpan.FromSeconds(30)));
                    cache.Add(key, entries);
                    cache.AddRunPath(runKey, first);
                    Assert.True(phase.SignalAndWait(TimeSpan.FromSeconds(30)));
                    Assert.True(cache.TryGetValue(key, out List<RichTextGlyphRenderer.GlyphRenderData> acquired));
                    Assert.True(cache.TryGetRunPath(runKey, out IPath acquiredRun));
                    Assert.Equal(2, acquired.Count);
                    IPath acquiredFirst = acquired[0].FillPath;
                    IPath acquiredSecond = acquired[1].FillPath;
                    Assert.Equal(new RectangleF(acquiredFirst.Bounds.X, 0, 10, 10), acquiredFirst.Bounds);
                    Assert.Equal(new RectangleF(acquiredFirst.Bounds.X, 10, 10, 10), acquiredSecond.Bounds);
                    RectangleF runBounds = acquiredRun.Bounds;
                    Assert.Equal(new SizeF(10, 10), runBounds.Size);
                    Assert.True(phase.SignalAndWait(TimeSpan.FromSeconds(30)));

                    // Every reader now owns a result. Competing insertions force eviction
                    // at small capacity; Clear additionally removes both indexes at once.
                    RichTextGlyphRenderer.CacheKey competingKey = new()
                    {
                        Font = "test",
                        GlyphId = (ushort)(100 + (iteration * workers) + index)
                    };

                    cache.Add(competingKey, entries);
                    cache.AddRunPath(new DrawingTextCache.RunPathCacheKey(
                        [new DrawingTextCache.RunPathCacheEntry(first, Vector2.Zero, competingKey, true)], 1), first);

                    if (clearDuringDrawing)
                    {
                        cache.Clear();
                    }

                    Assert.InRange(cache.Count, 0, capacity);
                    Assert.True(phase.SignalAndWait(TimeSpan.FromSeconds(30)));
                    Assert.Equal(2, acquired.Count);
                    Assert.Same(acquiredFirst, acquired[0].FillPath);
                    Assert.Same(acquiredSecond, acquired[1].FillPath);
                    Assert.Equal(runBounds, acquiredRun.Bounds);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Verifies a cached layered glyph replayed at a new position matches a fresh build there.
    /// Paint brushes are re-created from paints expressed before the drawing transform, so a
    /// replay that keeps the build position would sample a gradient outside the glyph.
    /// </summary>
    [Fact]
    public void LayeredGlyph_ReplayAtNewPositionMatchesFreshBuild()
    {
        Font emojiFont = TestFontUtilities.GetFont(TestFonts.NotoColorEmojiRegular, 48);
        TextBlock block = new("😀", new RichTextOptions(emojiFont) { ColorFontSupport = ColorFontSupport.ColrV1 });
        DrawingTextCache shared = new();
        DrawingOptions options = new();
        Brush brush = Brushes.Solid(Color.Red);

        using Image<Rgba32> warm = new(320, 160);
        warm.Mutate(x => x.Paint(options, shared, canvas => canvas.DrawText(block, new PointF(16, 100), 320F, brush, null)));

        using Image<Rgba32> expected = new(320, 160);
        expected.Mutate(x => x.Paint(options, new DrawingTextCache(), canvas => canvas.DrawText(block, new PointF(200, 20), 320F, brush, null)));

        using Image<Rgba32> actual = new(320, 160);
        actual.Mutate(x => x.Paint(options, shared, canvas => canvas.DrawText(block, new PointF(200, 20), 320F, brush, null)));

        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }

    /// <summary>
    /// Draws ordinary and decorated text, a positioned glyph run, and nested color layers.
    /// </summary>
    /// <param name="image">The independent target for this draw.</param>
    /// <param name="cache">The cache shared by concurrent canvases or used for the serial reference.</param>
    /// <param name="font">The ordinary text font.</param>
    /// <param name="emojiFont">The layered color font.</param>
    /// <param name="glyphId">The ordinary font's H glyph.</param>
    /// <param name="index">The worker index, varying placement and paint between canvases.</param>
    private static void DrawSample(Image<Rgba32> image, DrawingTextCache cache, Font font, Font emojiFont, ushort glyphId, int index)
    {
        using DrawingCanvas canvas = image.Frames.RootFrame.CreateCanvas(image.Configuration, new DrawingOptions(), cache);
        Brush brush = Brushes.Solid(index % 2 == 0 ? Color.Red : Color.Blue);

        RichTextOptions textOptions = new(font)
        {
            Origin = new Vector2(8 + index, 8),
            TextRuns = [new RichTextRun { Start = 0, End = 5, TextDecorations = TextDecorations.Underline }]
        };

        canvas.DrawText(textOptions, "Hello World", brush, null);
        RichGlyphOptions glyphOptions = new() { Font = font };
        canvas.DrawText(
            [glyphId, glyphId, glyphId],
            [new Vector2(8 + index, 72), new Vector2(32 + index, 72), new Vector2(56 + index, 72)],
            glyphOptions,
            brush,
            null);

        RichGlyphOptions emojiOptions = new()
        {
            Font = emojiFont,
            Origin = new Vector2(100 + index, 80),
            ColorFontSupport = ColorFontSupport.ColrV1
        };

        // This glyph contains nested SoftLight/SrcIn composite groups, exercising layer-stack
        // isolation and complete publication of the cached layer and group-marker sequence.
        canvas.DrawText(2629, emojiOptions, brush, null);
    }
}
