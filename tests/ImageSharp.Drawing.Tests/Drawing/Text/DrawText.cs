// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Numerics;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;
using SixLabors.ImageSharp.Drawing.Tests.Processing;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Text
{
    public class DrawText : BaseImageOperationsExtensionTest
    {
        private readonly FontCollection fontCollection;
        private readonly DrawingOptions otherTextOptions = new DrawingOptions()
        {
            TextOptions = new TextOptions(),
            GraphicsOptions = new GraphicsOptions()
        };

        private readonly Font font;

        public DrawText()
        {
            this.fontCollection = new FontCollection();
            this.font = this.fontCollection.Install(TestFontUtilities.GetPath("SixLaborsSampleAB.woff")).CreateFont(12);
        }

        [Fact]
        public void FillsForEachACharacterWhenBrushSetAndNotPen()
        {
            this.operations.DrawText(
                this.otherTextOptions,
                "123",
                this.font,
                Brushes.Solid(Color.Red),
                null,
                Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
            Assert.NotEqual(this.textOptions, processor.Options.TextOptions);
            Assert.NotEqual(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void FillsForEachACharacterWhenBrushSetAndNotPenDefaultOptions()
        {
            this.operations.DrawText("123", this.font, Brushes.Solid(Color.Red), null, Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
            Assert.Equal(this.textOptions, processor.Options.TextOptions);
            Assert.Equal(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void FillsForEachACharacterWhenBrushSet()
        {
            this.operations.DrawText(this.otherTextOptions, "123", this.font, Brushes.Solid(Color.Red), Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
            Assert.NotEqual(this.textOptions, processor.Options.TextOptions);
            Assert.NotEqual(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void FillsForEachACharacterWhenBrushSetDefaultOptions()
        {
            this.operations.DrawText("123", this.font, Brushes.Solid(Color.Red), Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
            Assert.Equal(this.textOptions, processor.Options.TextOptions);
            Assert.Equal(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void FillsForEachACharacterWhenColorSet()
        {
            this.operations.DrawText(this.otherTextOptions, "123", this.font, Color.Red, Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);

            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
            Assert.NotEqual(this.textOptions, processor.Options.TextOptions);
            Assert.NotEqual(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void FillsForEachACharacterWhenColorSetDefaultOptions()
        {
            this.operations.DrawText("123", this.font, Color.Red, Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);

            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
            Assert.Equal(this.textOptions, processor.Options.TextOptions);
            Assert.Equal(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void DrawForEachACharacterWhenPenSetAndNotBrush()
        {
            this.operations.DrawText(
                this.otherTextOptions,
                "123",
                this.font,
                null,
                Pens.Dash(Color.Red, 1),
                Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
            Assert.NotEqual(this.textOptions, processor.Options.TextOptions);
            Assert.NotEqual(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void DrawForEachACharacterWhenPenSetAndNotBrushDefaultOptions()
        {
            this.operations.DrawText("123", this.font, null, Pens.Dash(Color.Red, 1), Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
            Assert.Equal(this.textOptions, processor.Options.TextOptions);
            Assert.Equal(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void DrawForEachACharacterWhenPenSet()
        {
            this.operations.DrawText(this.otherTextOptions, "123", this.font, Pens.Dash(Color.Red, 1), Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);
            Assert.NotEqual(this.textOptions, processor.Options.TextOptions);
            Assert.NotEqual(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void DrawForEachACharacterWhenPenSetDefaultOptions()
        {
            this.operations.DrawText("123", this.font, Pens.Dash(Color.Red, 1), Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);

            Assert.Equal("123", processor.Text);
            Assert.Equal(this.font, processor.Font);
            SolidBrush penBrush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
            Assert.Equal(Color.Red, penBrush.Color);
            Assert.Equal(1, processor.Pen.StrokeWidth);
            Assert.Equal(PointF.Empty, processor.Location);
            Assert.Equal(this.textOptions, processor.Options.TextOptions);
            Assert.Equal(this.options, processor.Options.GraphicsOptions);
        }

        [Fact]
        public void DrawForEachACharacterWhenPenSetAndFillFroEachWhenBrushSet()
        {
            this.operations.DrawText(
                this.otherTextOptions,
                "123",
                this.font,
                Brushes.Solid(Color.Red),
                Pens.Dash(Color.Red, 1),
                Vector2.Zero);

            DrawTextProcessor processor = this.Verify<DrawTextProcessor>(0);

            Assert.Equal("123", processor.Text);
            Assert.Equal(this.font, processor.Font);
            SolidBrush brush = Assert.IsType<SolidBrush>(processor.Brush);
            Assert.Equal(Color.Red, brush.Color);
            Assert.Equal(PointF.Empty, processor.Location);
            SolidBrush penBrush = Assert.IsType<SolidBrush>(processor.Pen.StrokeFill);
            Assert.Equal(Color.Red, penBrush.Color);
            Assert.Equal(1, processor.Pen.StrokeWidth);
            Assert.NotEqual(this.textOptions, processor.Options.TextOptions);
            Assert.NotEqual(this.options, processor.Options.GraphicsOptions);
        }
    }
}
