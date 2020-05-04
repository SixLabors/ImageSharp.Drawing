// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using SixLabors.ImageSharp.Drawing.Tests.TestUtilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class Fill : BaseImageOperationsExtensionTest
    {
        IBrush brush = new SolidBrush(Color.HotPink);

        [Fact]
        public void Brush()
        {
            this.operations.Fill(new GraphicsOptions(), this.brush);

            FillProcessor processor = this.Verify<FillProcessor>();

            Assert.NotEqual(this.options, processor.Options);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void BrushDefaultOptions()
        {
            this.operations.Fill(this.brush);

            FillProcessor processor = this.Verify<FillProcessor>();

            Assert.Equal(this.options, processor.Options);
            Assert.Equal(this.brush, processor.Brush);
        }

        [Fact]
        public void ColorSet()
        {
            this.operations.Fill(new GraphicsOptions(), Color.Red);

            FillProcessor processor = this.Verify<FillProcessor>();

            Assert.NotEqual(this.options, processor.Options);
            var solidBrush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, solidBrush.Color);
        }

        [Fact]
        public void ColorSetDefaultOptions()
        {
            this.operations.Fill(Color.Red);

            FillProcessor processor = this.Verify<FillProcessor>();

            Assert.Equal(this.options, processor.Options);
            var solidBrush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, solidBrush.Color);
        }
    }
}
