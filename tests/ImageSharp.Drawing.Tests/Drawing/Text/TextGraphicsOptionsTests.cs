// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;

using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Text
{
    public class TextOptionsTests
    {
        private readonly TextOptions newTextOptions = new TextOptions();
        private readonly TextOptions cloneTextOptions = new TextOptions().DeepClone();

        [Fact]
        public void CloneTextOptionsIsNotNull() => Assert.True(this.cloneTextOptions != null);

        [Fact]
        public void DefaultTextOptionsApplyKerning()
        {
            const bool Expected = true;
            Assert.Equal(Expected, this.newTextOptions.ApplyKerning);
            Assert.Equal(Expected, this.cloneTextOptions.ApplyKerning);
        }

        [Fact]
        public void DefaultTextOptionsHorizontalAlignment()
        {
            const HorizontalAlignment Expected = HorizontalAlignment.Left;
            Assert.Equal(Expected, this.newTextOptions.HorizontalAlignment);
            Assert.Equal(Expected, this.cloneTextOptions.HorizontalAlignment);
        }

        [Fact]
        public void DefaultTextOptionsVerticalAlignment()
        {
            const VerticalAlignment Expected = VerticalAlignment.Top;
            Assert.Equal(Expected, this.newTextOptions.VerticalAlignment);
            Assert.Equal(Expected, this.cloneTextOptions.VerticalAlignment);
        }

        [Fact]
        public void DefaultTextOptionsDpiX()
        {
            const float Expected = 72F;
            Assert.Equal(Expected, this.newTextOptions.DpiX);
            Assert.Equal(Expected, this.cloneTextOptions.DpiX);
        }

        [Fact]
        public void DefaultTextOptionsDpiY()
        {
            const float Expected = 72F;
            Assert.Equal(Expected, this.newTextOptions.DpiY);
            Assert.Equal(Expected, this.cloneTextOptions.DpiY);
        }

        [Fact]
        public void DefaultTextOptionsTabWidth()
        {
            const float Expected = 4F;
            Assert.Equal(Expected, this.newTextOptions.TabWidth);
            Assert.Equal(Expected, this.cloneTextOptions.TabWidth);
        }

        [Fact]
        public void DefaultTextOptionsWrapTextWidth()
        {
            const float Expected = 0F;
            Assert.Equal(Expected, this.newTextOptions.WrapTextWidth);
            Assert.Equal(Expected, this.cloneTextOptions.WrapTextWidth);
        }

        [Fact]
        public void NonDefaultClone()
        {
            var expected = new TextOptions
            {
                ApplyKerning = false,
                DpiX = 46F,
                DpiY = 52F,
                HorizontalAlignment = HorizontalAlignment.Center,
                TabWidth = 3F,
                VerticalAlignment = VerticalAlignment.Bottom,
                WrapTextWidth = 42F
            };

            TextOptions actual = expected.DeepClone();

            Assert.Equal(expected.ApplyKerning, actual.ApplyKerning);
            Assert.Equal(expected.DpiX, actual.DpiX);
            Assert.Equal(expected.DpiY, actual.DpiY);
            Assert.Equal(expected.HorizontalAlignment, actual.HorizontalAlignment);
            Assert.Equal(expected.TabWidth, actual.TabWidth);
            Assert.Equal(expected.VerticalAlignment, actual.VerticalAlignment);
            Assert.Equal(expected.WrapTextWidth, actual.WrapTextWidth);
        }

        [Fact]
        public void CloneIsDeep()
        {
            var expected = new TextOptions();
            TextOptions actual = expected.DeepClone();

            actual.ApplyKerning = false;
            actual.DpiX = 46F;
            actual.DpiY = 52F;
            actual.HorizontalAlignment = HorizontalAlignment.Center;
            actual.TabWidth = 3F;
            actual.VerticalAlignment = VerticalAlignment.Bottom;
            actual.WrapTextWidth = 42F;

            Assert.NotEqual(expected.ApplyKerning, actual.ApplyKerning);
            Assert.NotEqual(expected.DpiX, actual.DpiX);
            Assert.NotEqual(expected.DpiY, actual.DpiY);
            Assert.NotEqual(expected.HorizontalAlignment, actual.HorizontalAlignment);
            Assert.NotEqual(expected.TabWidth, actual.TabWidth);
            Assert.NotEqual(expected.VerticalAlignment, actual.VerticalAlignment);
            Assert.NotEqual(expected.WrapTextWidth, actual.WrapTextWidth);
        }
    }
}
