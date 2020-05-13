// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System.Numerics;
using System.Runtime.InteropServices.ComTypes;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class Clear : BaseImageOperationsExtensionTest
    {
        GraphicsOptionsComparer clearComparer = new GraphicsOptionsComparer() { SkipClearOptions = true };
        GraphicsOptions nonDefaultOptions = new GraphicsOptions()
        {
            AlphaCompositionMode = PixelFormats.PixelAlphaCompositionMode.Clear,
            BlendPercentage = 0.5f,
            ColorBlendingMode = PixelFormats.PixelColorBlendingMode.Darken,
            AntialiasSubpixelDepth = 99
        };
        IBrush brush = new SolidBrush(Color.HotPink);

        [Fact]
        public void Brush()
        {
            this.operations.Clear(this.nonDefaultOptions, this.brush);

            FillProcessor processor = this.Verify<FillProcessor>();

            Assert.Equal(this.nonDefaultOptions, processor.Options, this.clearComparer);
            Assert.NotEqual(this.options, processor.Options, this.clearComparer);

            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void BrushDefaultOptions()
        {
            this.operations.Clear(this.brush);

            FillProcessor processor = this.Verify<FillProcessor>();

            Assert.Equal(this.options, processor.Options, this.clearComparer);

            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void ColorSet()
        {
            this.operations.Clear(this.nonDefaultOptions, Color.Red);

            FillProcessor processor = this.Verify<FillProcessor>();

            Assert.Equal(this.nonDefaultOptions, processor.Options, this.clearComparer);

            var solidBrush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, solidBrush.Color);
        }

        [Fact]
        public void ColorSetDefaultOptions()
        {
            this.operations.Clear(Color.Red);

            FillProcessor processor = this.Verify<FillProcessor>();

            Assert.Equal(this.options, processor.Options, this.clearComparer);

            var solidBrush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, solidBrush.Color);
        }
    }
}
