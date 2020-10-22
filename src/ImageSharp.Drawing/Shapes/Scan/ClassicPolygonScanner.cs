using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    internal ref struct ClassicPolygonScanner
    {
        public float SubpixelFraction;

        private readonly Region region;
        private IMemoryOwner<float> bBuffer;
        private Span<float> buffer;
        private int minY;
        private int maxY;

        public int y;
        public float subPixel;
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
            this.y = minY - 1;
            this.subPixel = float.NaN;
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
            this.y++;
            this.yPlusOne = this.y + 1;
            this.subPixel = this.y - this.SubpixelFraction;
            return this.y < this.maxY;
        }

        public bool MoveToNextSubpixelScanLine()
        {
            this.subPixel += this.SubpixelFraction;
            return this.subPixel < this.yPlusOne;
        }

        public ReadOnlySpan<float> ScanCurrentLine()
        {
            int pointsFound = this.region.Scan(this.subPixel, this.buffer, this.configuration, this.intersectionRule);
            return this.buffer.Slice(0, pointsFound);
        }
    }
}
