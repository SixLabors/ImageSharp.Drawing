// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ImageComparison;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing
{
    [GroupOutput("Drawing")]
    public class FillSolidBrushTests
    {
        [Theory]
        [WithBlankImage(1, 1, PixelTypes.Rgba32)]
        [WithBlankImage(7, 4, PixelTypes.Rgba32)]
        [WithBlankImage(16, 7, PixelTypes.Rgba32)]
        [WithBlankImage(33, 32, PixelTypes.Rgba32)]
        [WithBlankImage(400, 500, PixelTypes.Rgba32)]
        public void DoesNotDependOnSize<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using (Image<TPixel> image = provider.GetImage())
            {
                Color color = Color.HotPink;
                image.Mutate(c => c.Fill(color));

                image.DebugSave(provider, appendPixelTypeToFileName: false);
                image.ComparePixelBufferTo(color);
            }
        }

        [Theory]
        [WithBlankImage(16, 16, PixelTypes.Rgba32 | PixelTypes.Argb32 | PixelTypes.RgbaVector)]
        public void DoesNotDependOnSinglePixelType<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using (Image<TPixel> image = provider.GetImage())
            {
                Color color = Color.HotPink;
                image.Mutate(c => c.Fill(color));

                image.DebugSave(provider, appendSourceFileOrDescription: false);
                image.ComparePixelBufferTo(color);
            }
        }

        [Theory]
        [WithBlankImage(16, 16, PixelTypes.RgbaVector)]
        public void WorksAtHighPrecision<TPixel>(TestImageProvider<TPixel> provider)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using (var image = provider.GetImage() as Image<RgbaVector>)
            {
                var pixel = new RgbaVector(1e-38f, 2e-38f, 3e-38f);
                var brush = new SolidBrush<RgbaVector>(pixel);
                image.Mutate(c => c.Fill(brush));

                image.DebugSave(provider, appendSourceFileOrDescription: false);
                image.ComparePixelBufferTo(pixel);
            }
        }

        [Theory]
        [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, "Blue")]
        [WithSolidFilledImages(16, 16, "Yellow", PixelTypes.Rgba32, "Khaki")]
        public void WhenColorIsOpaque_OverridePreviousColor<TPixel>(
            TestImageProvider<TPixel> provider,
            string newColorName)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using (Image<TPixel> image = provider.GetImage())
            {
                Color color = TestUtils.GetColorByName(newColorName);
                image.Mutate(c => c.Fill(color));

                image.DebugSave(
                    provider,
                    newColorName,
                    appendPixelTypeToFileName: false,
                    appendSourceFileOrDescription: false);
                image.ComparePixelBufferTo(color);
            }
        }

        [Theory]
        [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, 5, 7, 3, 8)]
        [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, 8, 5, 6, 4)]
        public void FillRegion<TPixel>(TestImageProvider<TPixel> provider, int x0, int y0, int w, int h)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            FormattableString testDetails = $"(x{x0},y{y0},w{w},h{h})";
            var region = new RectangleF(x0, y0, w, h);
            Color color = TestUtils.GetColorByName("Blue");

            provider.RunValidatingProcessorTest(c => c.Fill(color, region), testDetails, ImageComparer.Exact);
        }

        [Theory]
        [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, 5, 7, 3, 8)]
        [WithSolidFilledImages(16, 16, "Red", PixelTypes.Rgba32, 8, 5, 6, 4)]
        public void FillRegion_WorksOnWrappedMemoryImage<TPixel>(
            TestImageProvider<TPixel> provider,
            int x0,
            int y0,
            int w,
            int h)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            FormattableString testDetails = $"(x{x0},y{y0},w{w},h{h})";
            var region = new RectangleF(x0, y0, w, h);
            Color color = TestUtils.GetColorByName("Blue");

            provider.RunValidatingProcessorTestOnWrappedMemoryImage(
                c => c.Fill(color, region),
                testDetails,
                ImageComparer.Exact,
                useReferenceOutputFrom: nameof(this.FillRegion));
        }

        public static readonly TheoryData<bool, string, float, PixelColorBlendingMode, float> BlendData =
            new TheoryData<bool, string, float, PixelColorBlendingMode, float>
                {
                    { false, "Blue", 0.5f, PixelColorBlendingMode.Normal, 1.0f },
                    { false, "Blue", 1.0f, PixelColorBlendingMode.Normal, 0.5f },
                    { false, "Green", 0.5f, PixelColorBlendingMode.Normal, 0.3f },
                    { false, "HotPink", 0.8f, PixelColorBlendingMode.Normal, 0.8f },
                    { false, "Blue", 0.5f, PixelColorBlendingMode.Multiply, 1.0f },
                    { false, "Blue", 1.0f, PixelColorBlendingMode.Multiply, 0.5f },
                    { false, "Green", 0.5f, PixelColorBlendingMode.Multiply, 0.3f },
                    { false, "HotPink", 0.8f, PixelColorBlendingMode.Multiply, 0.8f },
                    { false, "Blue", 0.5f, PixelColorBlendingMode.Add, 1.0f },
                    { false, "Blue", 1.0f, PixelColorBlendingMode.Add, 0.5f },
                    { false, "Green", 0.5f, PixelColorBlendingMode.Add, 0.3f },
                    { false, "HotPink", 0.8f, PixelColorBlendingMode.Add, 0.8f },
                    { true, "Blue", 0.5f, PixelColorBlendingMode.Normal, 1.0f },
                    { true, "Blue", 1.0f, PixelColorBlendingMode.Normal, 0.5f },
                    { true, "Green", 0.5f, PixelColorBlendingMode.Normal, 0.3f },
                    { true, "HotPink", 0.8f, PixelColorBlendingMode.Normal, 0.8f },
                    { true, "Blue", 0.5f, PixelColorBlendingMode.Multiply, 1.0f },
                    { true, "Blue", 1.0f, PixelColorBlendingMode.Multiply, 0.5f },
                    { true, "Green", 0.5f, PixelColorBlendingMode.Multiply, 0.3f },
                    { true, "HotPink", 0.8f, PixelColorBlendingMode.Multiply, 0.8f },
                    { true, "Blue", 0.5f, PixelColorBlendingMode.Add, 1.0f },
                    { true, "Blue", 1.0f, PixelColorBlendingMode.Add, 0.5f },
                    { true, "Green", 0.5f, PixelColorBlendingMode.Add, 0.3f },
                    { true, "HotPink", 0.8f, PixelColorBlendingMode.Add, 0.8f },
                };

        [Theory]
        [WithSolidFilledImages(nameof(BlendData), 16, 16, "Red", PixelTypes.Rgba32)]
        public void BlendFillColorOverBackground<TPixel>(
            TestImageProvider<TPixel> provider,
            bool triggerFillRegion,
            string newColorName,
            float alpha,
            PixelColorBlendingMode blenderMode,
            float blendPercentage)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            Color fillColor = TestUtils.GetColorByName(newColorName).WithAlpha(alpha);

            using (Image<TPixel> image = provider.GetImage())
            {
                TPixel bgColor = image[0, 0];

                var options = new DrawingOptions
                {
                    GraphicsOptions = new GraphicsOptions
                    {
                        Antialias = false,
                        ColorBlendingMode = blenderMode,
                        BlendPercentage = blendPercentage
                    }
                };

                if (triggerFillRegion)
                {
                    var path = new RectangularPolygon(0, 0, 16, 16);
                    image.Mutate(c => c.SetGraphicsOptions(options.GraphicsOptions).Fill(new SolidBrush(fillColor), path));
                }
                else
                {
                    image.Mutate(c => c.Fill(options, new SolidBrush(fillColor)));
                }

                var testOutputDetails = new
                {
                    triggerFillRegion,
                    newColorName,
                    alpha,
                    blenderMode,
                    blendPercentage
                };

                image.DebugSave(
                    provider,
                    testOutputDetails,
                    appendPixelTypeToFileName: false,
                    appendSourceFileOrDescription: false);

                PixelBlender<TPixel> blender = PixelOperations<TPixel>.Instance.GetPixelBlender(
                    blenderMode,
                    PixelAlphaCompositionMode.SrcOver);
                TPixel expectedPixel = blender.Blend(bgColor, fillColor.ToPixel<TPixel>(), blendPercentage);

                image.ComparePixelBufferTo(expectedPixel);
            }
        }
    }
}
