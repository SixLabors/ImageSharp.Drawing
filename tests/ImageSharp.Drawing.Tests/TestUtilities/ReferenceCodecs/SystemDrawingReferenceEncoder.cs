// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.ReferenceCodecs
{
    public class SystemDrawingReferenceEncoder : IImageEncoder
    {
        private readonly ImageFormat imageFormat;

        public SystemDrawingReferenceEncoder(ImageFormat imageFormat)
            => this.imageFormat = imageFormat;

        public static SystemDrawingReferenceEncoder Png { get; } = new SystemDrawingReferenceEncoder(ImageFormat.Png);

        public static SystemDrawingReferenceEncoder Bmp { get; } = new SystemDrawingReferenceEncoder(ImageFormat.Bmp);

        public void Encode<TPixel>(Image<TPixel> image, Stream stream)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            using Bitmap sdBitmap = SystemDrawingBridge.To32bppArgbSystemDrawingBitmap(image);
            sdBitmap.Save(stream, this.imageFormat);
        }

        public Task EncodeAsync<TPixel>(Image<TPixel> image, Stream stream, CancellationToken cancellationToken)
            where TPixel : unmanaged, IPixel<TPixel>
            => throw new NotImplementedException();
    }
}
