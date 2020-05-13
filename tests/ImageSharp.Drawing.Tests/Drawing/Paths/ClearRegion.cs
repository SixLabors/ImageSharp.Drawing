// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class ClearRegion : BaseImageOperationsExtensionTest
    {
        ShapeGraphicsOptions nonDefaultOptions = new ShapeGraphicsOptions()
        {
            GraphicsOptions = {
                AlphaCompositionMode = PixelFormats.PixelAlphaCompositionMode.Clear,
                BlendPercentage = 0.5f,
                ColorBlendingMode = PixelFormats.PixelColorBlendingMode.Darken
            }
        };
        IBrush brush = Brushes.Solid(Color.HotPink);
        Region path = new ShapeRegion(new EllipsePolygon(10, 10, 100));

        [Fact]
        public void Brush()
        {
            this.operations.Clear(this.nonDefaultOptions, this.brush, this.path);

            FillRegionProcessor processor = this.Verify<FillRegionProcessor>();

            var expectedOptions = this.nonDefaultOptions;
            Assert.Equal(expectedOptions.ShapeOptions, processor.Options.ShapeOptions);
            Assert.Equal(1, processor.Options.GraphicsOptions.BlendPercentage);
            Assert.Equal(PixelFormats.PixelAlphaCompositionMode.Src, processor.Options.GraphicsOptions.AlphaCompositionMode);
            Assert.Equal(PixelFormats.PixelColorBlendingMode.Normal, processor.Options.GraphicsOptions.ColorBlendingMode);


            Assert.Equal(this.path, processor.Region);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void BrushDefaultOptions()
        {
            this.operations.Clear(this.brush, this.path);

            FillRegionProcessor processor = this.Verify<FillRegionProcessor>();

            var expectedOptions = this.shapeOptions;
            Assert.Equal(expectedOptions, processor.Options.ShapeOptions);
            Assert.Equal(1, processor.Options.GraphicsOptions.BlendPercentage);
            Assert.Equal(PixelFormats.PixelAlphaCompositionMode.Src, processor.Options.GraphicsOptions.AlphaCompositionMode);
            Assert.Equal(PixelFormats.PixelColorBlendingMode.Normal, processor.Options.GraphicsOptions.ColorBlendingMode);

            Assert.Equal(this.path, processor.Region);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void ColorSet()
        {
            this.operations.Clear(this.nonDefaultOptions, Color.Red, this.path);

            FillRegionProcessor processor = this.Verify<FillRegionProcessor>();

            var expectedOptions = this.nonDefaultOptions;
            Assert.Equal(expectedOptions.ShapeOptions, processor.Options.ShapeOptions);

            Assert.Equal(1, processor.Options.GraphicsOptions.BlendPercentage);
            Assert.Equal(PixelFormats.PixelAlphaCompositionMode.Src, processor.Options.GraphicsOptions.AlphaCompositionMode);
            Assert.Equal(PixelFormats.PixelColorBlendingMode.Normal, processor.Options.GraphicsOptions.ColorBlendingMode);
            Assert.Equal(this.path, processor.Region);
            Assert.NotEqual(this.brush, processor.Brush);
            var brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
        }

        [Fact]
        public void ColorAndThicknessDefaultOptions()
        {
            this.operations.Clear(Color.Red, this.path);

            FillRegionProcessor processor = this.Verify<FillRegionProcessor>();

            var expectedOptions = this.shapeOptions;
            Assert.Equal(expectedOptions, processor.Options.ShapeOptions);
            Assert.Equal(1, processor.Options.GraphicsOptions.BlendPercentage);
            Assert.Equal(PixelFormats.PixelAlphaCompositionMode.Src, processor.Options.GraphicsOptions.AlphaCompositionMode);
            Assert.Equal(PixelFormats.PixelColorBlendingMode.Normal, processor.Options.GraphicsOptions.ColorBlendingMode);


            Assert.Equal(this.path, processor.Region);
            Assert.NotEqual(this.brush, processor.Brush);
            var brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
        }
    }
}
