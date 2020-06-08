// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    /// <summary>
    /// Provides <see cref="Image{TPixel}" /> instances for parametric unit tests.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format of the image</typeparam>
    public abstract partial class TestImageProvider<TPixel>
        where TPixel : unmanaged, IPixel<TPixel>
    {
        private class LambdaProvider : TestImageProvider<TPixel>
        {
            private readonly Func<Image<TPixel>> factoryFunc;

            public LambdaProvider(Func<Image<TPixel>> factoryFunc)
            {
                this.factoryFunc = factoryFunc;
            }

            public override Image<TPixel> GetImage() => this.factoryFunc();
        }
    }
}
