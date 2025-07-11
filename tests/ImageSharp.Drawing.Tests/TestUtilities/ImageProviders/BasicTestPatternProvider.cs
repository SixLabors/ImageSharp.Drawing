// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using Xunit.Abstractions;

namespace SixLabors.ImageSharp.Drawing.Tests;

public abstract partial class TestImageProvider<TPixel> : IXunitSerializable
{
    public virtual TPixel GetExpectedBasicTestPatternPixelAt(int x, int y)
    {
        throw new NotSupportedException("GetExpectedBasicTestPatternPixelAt(x,y) only works with BasicTestPattern");
    }

    private class BasicTestPatternProvider : BlankProvider
    {
        private static readonly TPixel TopLeftColor = Color.Red.ToPixel<TPixel>();
        private static readonly TPixel TopRightColor = Color.Green.ToPixel<TPixel>();
        private static readonly TPixel BottomLeftColor = Color.Blue.ToPixel<TPixel>();

        // Transparent purple:
        private static readonly TPixel BottomRightColor = GetBottomRightColor();

        public BasicTestPatternProvider(int width, int height)
            : base(width, height)
        {
        }

        // This parameterless constructor is needed for xUnit deserialization
        public BasicTestPatternProvider()
        {
        }

        public override string SourceFileOrDescription => TestUtils.AsInvariantString($"BasicTestPattern{this.Width}x{this.Height}");

        public override Image<TPixel> GetImage()
        {
            Image<TPixel> result = new(this.Configuration, this.Width, this.Height);
            result.ProcessPixelRows(accessor =>
            {
                int midY = this.Height / 2;
                int midX = this.Width / 2;

                for (int y = 0; y < midY; y++)
                {
                    Span<TPixel> row = accessor.GetRowSpan(y);

                    row.Slice(0, midX).Fill(TopLeftColor);
                    row.Slice(midX, this.Width - midX).Fill(TopRightColor);
                }

                for (int y = midY; y < this.Height; y++)
                {
                    Span<TPixel> row = accessor.GetRowSpan(y);

                    row.Slice(0, midX).Fill(BottomLeftColor);
                    row.Slice(midX, this.Width - midX).Fill(BottomRightColor);
                }
            });

            return result;
        }

        public override TPixel GetExpectedBasicTestPatternPixelAt(int x, int y)
        {
            int midY = this.Height / 2;
            int midX = this.Width / 2;

            if (y < midY)
            {
                return x < midX ? TopLeftColor : TopRightColor;
            }
            else
            {
                return x < midX ? BottomLeftColor : BottomRightColor;
            }
        }

        private static TPixel GetBottomRightColor()
            => TPixel.FromVector4(new Vector4(1f, 0f, 1f, 0.5f));
    }
}
