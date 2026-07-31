// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public partial class DrawingCanvasTests
{
    [Theory]
    [WithBasicTestPatternImages(384, 256, PixelTypes.Rgba32)]
    public void DrawImage_WithRotationTransform_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> foreground = provider.GetImage();
        using Image<TPixel> target = new(384, 256);

        DrawingOptions options = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation(MathF.PI / 4F, new Vector2(192F, 128F)))
        };

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, options))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawImage(
                foreground,
                foreground.Bounds,
                new RectangleF(72, 48, 240, 160),
                KnownResamplers.NearestNeighbor);
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);

        // Ubunut with .NET10 has some minor difference due to nearest neightbor resampling, so we need to use a tolerant comparer here.
        target.CompareToReferenceOutput(ImageComparer.TolerantPercentage(0.0080F), provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(320, 220, PixelTypes.Rgba32)]
    public void DrawImage_WithSourceClippingAndScaling_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> foreground = provider.GetImage();
        using Image<TPixel> target = new(320, 220);
        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawImage(
                foreground,
                new Rectangle(-48, 18, 196, 148),
                new RectangleF(18, 20, 170, 120),
                KnownResamplers.Bicubic);
            canvas.DrawImage(
                foreground,
                new Rectangle(220, 100, 160, 140),
                new RectangleF(170, 72, 130, 110),
                KnownResamplers.NearestNeighbor);
            canvas.Draw(Pens.Solid(Color.Black, 3), new Rectangle(8, 8, 304, 204));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);
        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(360, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithClipPathAndTransform_MatchesReference<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> foreground = provider.GetImage();
        using Image<TPixel> target = new(360, 240);

        DrawingOptions transformedOptions = new()
        {
            Transform = new Matrix4x4(Matrix3x2.CreateRotation(0.32F, new Vector2(180, 120)))
        };

        IPath clipPath = new EllipsePolygon(new PointF(180, 120), new SizeF(208, 126));

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, target, new DrawingOptions()))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.Fill(Brushes.Solid(Color.LightGray.WithAlpha(0.45F)), new Rectangle(18, 16, 324, 208));

            _ = canvas.Save(transformedOptions);
            canvas.Clip(ClipOperation.Difference, clipPath);

            canvas.DrawImage(
                foreground,
                new Rectangle(10, 8, 234, 180),
                new RectangleF(64, 36, 232, 164),
                KnownResamplers.Bicubic);
            canvas.DrawImage(
                foreground,
                new Rectangle(102, 32, 196, 166),
                new RectangleF(92, 58, 210, 148),
                KnownResamplers.NearestNeighbor);
            canvas.Restore();

            canvas.Draw(Pens.DashDot(Color.DarkSlateGray, 3F), clipPath);
            canvas.Draw(Pens.Solid(Color.Black, 2F), new Rectangle(8, 8, 344, 224));
        }

        target.DebugSave(provider, appendSourceFileOrDescription: false);

        target.CompareToReferenceOutput(provider, appendSourceFileOrDescription: false);
    }

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithForeignPixelFormat_MatchesFullConversion<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertForeignPixelFormatMatchesFullConversion(
            provider,
            new Rectangle(64, 48, 180, 150),
            new RectangleF(40, 30, 200, 170),
            new Matrix4x4(Matrix3x2.CreateRotation(0.28F, new Vector2(160, 120))));

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithForeignPixelFormat_PartialRegionNoTransform_MatchesFullConversion<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertForeignPixelFormatMatchesFullConversion(
            provider,
            new Rectangle(64, 48, 180, 150),
            new RectangleF(40, 30, 200, 170),
            Matrix4x4.Identity);

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithForeignPixelFormat_SourceOutsideTopLeft_MatchesFullConversion<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertForeignPixelFormatMatchesFullConversion(
            provider,
            new Rectangle(-48, -32, 220, 190),
            new RectangleF(30, 24, 210, 180),
            new Matrix4x4(Matrix3x2.CreateRotation(0.21F, new Vector2(160, 120))));

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithForeignPixelFormat_SourceOutsideBottomRight_MatchesFullConversion<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertForeignPixelFormatMatchesFullConversion(
            provider,
            new Rectangle(200, 150, 260, 220),
            new RectangleF(48, 40, 200, 168),
            Matrix4x4.Identity);

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithForeignPixelFormat_ProjectiveTransform_MatchesFullConversion<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // A quad/projective transform (non-affine Matrix4x4 with perspective terms) combined
        // with a rotation, exercising the transform path over the clipped region.
        Matrix4x4 projective = new Matrix4x4(Matrix3x2.CreateRotation(0.18F, new Vector2(160, 120)))
        {
            M14 = 0.0006F,
            M24 = 0.0004F
        };

        AssertForeignPixelFormatMatchesFullConversion(
            provider,
            new Rectangle(56, 40, 190, 160),
            new RectangleF(44, 34, 200, 168),
            projective);
    }

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithForeignPixelFormat_WholeImage_MatchesFullConversion<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertForeignPixelFormatMatchesFullConversion(
            provider,
            new Rectangle(0, 0, 320, 240),
            new RectangleF(24, 20, 260, 200),
            new Matrix4x4(Matrix3x2.CreateRotation(0.15F, new Vector2(160, 120))));

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithEmptySourceRect_IsNoOp<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertDrawImageIsNoOp(
            provider,
            new Rectangle(40, 30, 0, 120),
            new RectangleF(20, 20, 200, 160));

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithEmptyDestinationRect_IsNoOp<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertDrawImageIsNoOp(
            provider,
            new Rectangle(40, 30, 180, 150),
            new RectangleF(20, 20, 200, 0));

    [Theory]
    [WithBasicTestPatternImages(320, 240, PixelTypes.Rgba32)]
    public void DrawImage_WithSourceRectFullyOutsideImage_IsNoOp<TPixel>(TestImageProvider<TPixel> provider)
        where TPixel : unmanaged, IPixel<TPixel>
        => AssertDrawImageIsNoOp(
            provider,
            new Rectangle(400, 300, 120, 100),
            new RectangleF(20, 20, 200, 160));

    /// <summary>
    /// A draw whose clipped source/destination region is empty must be a no-op for both the
    /// typed <see cref="Image{TPixel}"/> overload and the foreign-pixel-format <see cref="Image"/>
    /// overload, leaving the cleared background untouched.
    /// </summary>
    private static void AssertDrawImageIsNoOp<TPixel>(
        TestImageProvider<TPixel> provider,
        Rectangle sourceRect,
        RectangleF destinationRect)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> source = provider.GetImage();

        // A source image whose pixel format differs from the canvas, forcing the foreign-format path.
        using Image<Rgb24> foreignSource = source.CloneAs<Rgb24>();

        // The reference is the cleared background: a degenerate draw must not change any pixel.
        using Image<TPixel> expected = new(source.Width, source.Height);
        using (DrawingCanvas<TPixel> reference = CreateCanvas(provider, expected, new DrawingOptions()))
        {
            reference.Clear(Brushes.Solid(Color.White));
        }

        void AssertNoOp(Action<DrawingCanvas<TPixel>> draw)
        {
            using Image<TPixel> actual = new(source.Width, source.Height);
            using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, new DrawingOptions()))
            {
                canvas.Clear(Brushes.Solid(Color.White));
                draw(canvas);
            }

            ImageComparer.Exact.VerifySimilarity(expected, actual);
        }

        // Typed overload -> DrawImageCore empty-region early-return.
        AssertNoOp(canvas => canvas.DrawImage(source, sourceRect, destinationRect, KnownResamplers.Bicubic));

        // Foreign-format overload -> DrawImage empty-region early-return before any conversion.
        AssertNoOp(canvas => canvas.DrawImage((Image)foreignSource, sourceRect, destinationRect, KnownResamplers.Bicubic));
    }

    /// <summary>
    /// Drawing a foreign-pixel-format image (which converts only the clipped source region) must
    /// produce pixels identical to first converting the whole image to the canvas format and drawing that.
    /// </summary>
    private static void AssertForeignPixelFormatMatchesFullConversion<TPixel>(
        TestImageProvider<TPixel> provider,
        Rectangle sourceRect,
        RectangleF destinationRect,
        Matrix4x4 transform)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using Image<TPixel> source = provider.GetImage();

        // A source image whose pixel format differs from the canvas, forcing a per-pixel conversion.
        using Image<Rgb24> foreignSource = source.CloneAs<Rgb24>();

        // Reference source: the whole foreign image converted up-front to the canvas format.
        using Image<TPixel> convertedSource = foreignSource.CloneAs<TPixel>();

        DrawingOptions options = new()
        {
            Transform = transform
        };

        using Image<TPixel> actual = new(source.Width, source.Height);
        using Image<TPixel> expected = new(source.Width, source.Height);

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, actual, options))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawImage((Image)foreignSource, sourceRect, destinationRect, KnownResamplers.Bicubic);
        }

        using (DrawingCanvas<TPixel> canvas = CreateCanvas(provider, expected, options))
        {
            canvas.Clear(Brushes.Solid(Color.White));
            canvas.DrawImage(convertedSource, sourceRect, destinationRect, KnownResamplers.Bicubic);
        }

        // Converting only the clipped region must produce pixels identical to converting the whole image.
        ImageComparer.Exact.VerifySimilarity(expected, actual);
    }
}
