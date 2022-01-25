// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public abstract partial class TestImageProvider<TPixel>
    {
        private class BasicTestPatternProvider : BlankProvider
        {
            public BasicTestPatternProvider(int width, int height)
                : base(width, height)
            {
            }

            /// <summary>
            /// This parameterless constructor is needed for xUnit deserialization
            /// </summary>
            public BasicTestPatternProvider()
            {
            }

            public override string SourceFileOrDescription => TestUtils.AsInvariantString($"BasicTestPattern{this.Width}x{this.Height}");

            public override Image<TPixel> GetImage()
            {
                var result = new Image<TPixel>(this.Configuration, this.Width, this.Height);

                TPixel topLeftColor = Color.Red.ToPixel<TPixel>();
                TPixel topRightColor = Color.Green.ToPixel<TPixel>();
                TPixel bottomLeftColor = Color.Blue.ToPixel<TPixel>();

                // Transparent purple:
                TPixel bottomRightColor = default;
                bottomRightColor.FromVector4(new Vector4(1f, 0f, 1f, 0.5f));

                int midY = this.Height / 2;
                int midX = this.Width / 2;
                Buffer2D<TPixel> imageBuffer = result.GetRootFramePixelBuffer();
                for (int y = 0; y < midY; y++)
                {
                    Span<TPixel> row = imageBuffer.DangerousGetRowSpan(y);

                    row.Slice(0, midX).Fill(topLeftColor);
                    row.Slice(midX, this.Width - midX).Fill(topRightColor);
                }

                for (int y = midY; y < this.Height; y++)
                {
                    Span<TPixel> row = imageBuffer.DangerousGetRowSpan(y);

                    row.Slice(0, midX).Fill(bottomLeftColor);
                    row.Slice(midX, this.Width - midX).Fill(bottomRightColor);
                }

                return result;
            }
        }
    }
}
