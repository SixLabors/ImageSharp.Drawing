// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    // [Flags]
    // internal enum EdgeFlags
    // {
    //     None = 0,
    //
    //     // Edge is directed "Up" in SCREEN coordinates,
    //     // which means end.Y < start.Y
    //     EdgeUp = 1,
    //
    //     IncludeStartOnce = 1 << 8,  // 0x00100
    //     IncludeStartTwice = 1 << 9, // 0x00200
    //     IncludeEndOnce = 1 << 16,   // 0x10000
    //     IncludeEndTwice = 1 << 17,  // 0x20000
    // }

    /// <summary>
    /// Holds coordinates, and coefficients for a polygon edge to be vertically scanned.
    /// The edge's segment is defined with the reciprocal slope form:
    /// x = P * y + Q
    /// </summary>
    internal readonly struct EdgeData
    {
        public readonly float Y0; // Start
        public readonly float Y1; // End
        public readonly float P;
        public readonly float Q;

        private readonly int flags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private EdgeData(ref PointF p0, ref PointF p1, int flags)
        {
            this.Y0 = p0.Y;
            this.Y1 = p1.Y;
            this.flags = flags;
            float dy = p1.Y - p0.Y;
            this.P = (p1.X - p0.X) / dy;
            this.Q = ((p0.X * p1.Y) - (p1.X * p0.Y)) / dy;
        }

        public bool EdgeUp => (this.flags & 1) == 1;

        public int IncludeIsec0Times => (this.flags & 0b00110) >> 1;

        public int IncludeIsec1Times => (this.flags & 0b11000) >> 3;

        private static EdgeData CreateSorted(PointF start, PointF end, bool edgeUp, int includeStartTimes, int includeEndTimes)
        {
            if (edgeUp)
            {
                Swap(ref start, ref end);
                Swap(ref includeStartTimes, ref includeEndTimes);
            }

            int up = edgeUp ? 1 : 0;
            int flags = up | (includeStartTimes << 1) | (includeEndTimes << 3);
            return new EdgeData(ref start, ref end, flags);
        }

        public IMemoryOwner<EdgeData> CreateEdgesForMultipolygon(TessellatedMultipolygon multipolygon,
            MemoryAllocator allocator)
        {
            throw new NotImplementedException();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap<T>(ref T left, ref T right)
        {
            T tmp = left;
            left = right;
            right = tmp;
        }
    }
}