using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    internal ref struct ClassicPolygonScanner
    {
        public bool ScanlineDirty;

        private readonly Region region;
        private IMemoryOwner<float> bBuffer;
        private Span<float> buffer;
        private int minY;
        private int maxY;
        private float subpixelFraction;
        private float subpixelFractionPoint;
        private int y;
        private float subPixel;
        private IntersectionRule intersectionRule;
        private Configuration configuration;

        public ClassicPolygonScanner(Region region, IMemoryOwner<float> bBuffer, int minY, int maxY, int subpixelCount, IntersectionRule intersectionRule, Configuration configuration)
        {
            this.region = region;
            this.bBuffer = bBuffer;
            this.minY = minY;
            this.maxY = maxY;
            this.intersectionRule = intersectionRule;
            this.configuration = configuration;
            this.buffer = bBuffer.Memory.Span;

            this.subpixelFraction = 1f / subpixelCount;
            this.subpixelFractionPoint = this.subpixelFraction / subpixelCount;
            this.ScanlineDirty = true;
            this.y = minY - 1;
            this.subPixel = float.MaxValue;
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

            return new ClassicPolygonScanner(region, configuration.MemoryAllocator.Allocate<float>(maxIntersections), minY, maxY,
                subsampling, intersectionRule, configuration);
        }

        public void Dispose()
        {
            this.bBuffer.Dispose();
        }

        public bool MoveToNextScanline()
        {
            float yPlusOne = this.y + 1;
            if (this.subPixel < yPlusOne)
            {
                this.subPixel += this.subpixelFraction;
            }
            else
            {
                this.y++;
                if (this.ScanlineDirty)
                {
                    this.buffer.Clear();
                    this.ScanlineDirty = false;
                }

                this.subPixel = this.y;
            }

            return this.y < this.maxY;
        }

        public ReadOnlySpan<float> ScanCurrentLine()
        {
            int pointsFound = this.region.Scan(this.subPixel, this.buffer, this.configuration, this.intersectionRule);
            return this.buffer.Slice(0, pointsFound);
        }
    }
}