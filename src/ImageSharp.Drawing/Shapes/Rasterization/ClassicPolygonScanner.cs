using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization
{
    internal ref struct ClassicPolygonScanner
    {
        public float SubpixelFraction;

        private readonly Region region;
        private IMemoryOwner<float> bBuffer;
        private Span<float> buffer;
        private int minY;
        private int maxY;

        public int PixelLineY;
        public float SubPixelY;
        private IntersectionRule intersectionRule;
        private Configuration configuration;
        private float yPlusOne;

        public ClassicPolygonScanner(
            Region region,
            IMemoryOwner<float> bBuffer,
            int minY,
            int maxY,
            int subpixelCount,
            IntersectionRule intersectionRule,
            Configuration configuration)
        {
            this.region = region;
            this.bBuffer = bBuffer;
            this.minY = minY;
            this.maxY = maxY;
            this.intersectionRule = intersectionRule;
            this.configuration = configuration;
            this.buffer = bBuffer.Memory.Span;

            this.SubpixelFraction = 1f / subpixelCount;
            this.PixelLineY = minY - 1;
            this.SubPixelY = float.NaN;
            this.yPlusOne = float.NaN;
        }

        public static ClassicPolygonScanner Create(
            Region region,
            int minY,
            int maxY,
            int subsampling,
            IntersectionRule intersectionRule,
            Configuration configuration)
        {
            int maxIntersections = region.MaxIntersections;

            return new ClassicPolygonScanner(
                region,
                configuration.MemoryAllocator.Allocate<float>(maxIntersections),
                minY,
                maxY,
                subsampling,
                intersectionRule,
                configuration);
        }

        public void Dispose()
        {
            this.bBuffer.Dispose();
        }

        public bool MoveToNextPixelLine()
        {
            this.PixelLineY++;
            this.yPlusOne = this.PixelLineY + 1;
            this.SubPixelY = this.PixelLineY - this.SubpixelFraction;
            return this.PixelLineY < this.maxY;
        }

        public bool MoveToNextSubpixelScanLine()
        {
            this.SubPixelY += this.SubpixelFraction;
            return this.SubPixelY < this.yPlusOne;
        }

        public ReadOnlySpan<float> ScanCurrentLine()
        {
            int pointsFound = this.region.Scan(this.SubPixelY, this.buffer, this.configuration, this.intersectionRule);
            return this.buffer.Slice(0, pointsFound);
        }
    }
}
