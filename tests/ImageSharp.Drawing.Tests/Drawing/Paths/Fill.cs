// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class Fill : BaseImageOperationsExtensionTest
    {
        GraphicsOptions nonDefaultOptions = new GraphicsOptions();
        IBrush brush = new SolidBrush(Color.HotPink);

        [Fact]
        public void Brush()
        {
            this.operations.Fill(this.nonDefaultOptions, this.brush);

            FillProcessor processor = this.Verify<FillProcessor>();

            var expectedOptions = this.nonDefaultOptions;
            Assert.Equal(expectedOptions, processor.Options);
            Assert.Equal(expectedOptions.BlendPercentage, processor.Options.BlendPercentage);
            Assert.Equal(expectedOptions.AlphaCompositionMode, processor.Options.AlphaCompositionMode);
            Assert.Equal(expectedOptions.ColorBlendingMode, processor.Options.ColorBlendingMode);

            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void BrushDefaultOptions()
        {
            this.operations.Fill(this.brush);

            FillProcessor processor = this.Verify<FillProcessor>();

            var expectedOptions = this.options;
            Assert.Equal(expectedOptions, processor.Options);
            Assert.Equal(expectedOptions.BlendPercentage, processor.Options.BlendPercentage);
            Assert.Equal(expectedOptions.AlphaCompositionMode, processor.Options.AlphaCompositionMode);
            Assert.Equal(expectedOptions.ColorBlendingMode, processor.Options.ColorBlendingMode);

            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void ColorSet()
        {
            this.operations.Fill(this.nonDefaultOptions, Color.Red);

            FillProcessor processor = this.Verify<FillProcessor>();


            var expectedOptions = this.nonDefaultOptions;
            Assert.Equal(expectedOptions, processor.Options);
            Assert.Equal(expectedOptions.BlendPercentage, processor.Options.BlendPercentage);
            Assert.Equal(expectedOptions.AlphaCompositionMode, processor.Options.AlphaCompositionMode);
            Assert.Equal(expectedOptions.ColorBlendingMode, processor.Options.ColorBlendingMode);

            var solidBrush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, solidBrush.Color);
        }

        [Fact]
        public void ColorSetDefaultOptions()
        {
            this.operations.Fill(Color.Red);

            FillProcessor processor = this.Verify<FillProcessor>();

            var expectedOptions = this.options;
            Assert.Equal(expectedOptions, processor.Options);
            Assert.Equal(expectedOptions.BlendPercentage, processor.Options.BlendPercentage);
            Assert.Equal(expectedOptions.AlphaCompositionMode, processor.Options.AlphaCompositionMode);
            Assert.Equal(expectedOptions.ColorBlendingMode, processor.Options.ColorBlendingMode);

            var solidBrush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, solidBrush.Color);
        }
    }
}
