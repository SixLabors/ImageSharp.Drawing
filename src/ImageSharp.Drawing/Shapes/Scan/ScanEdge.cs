// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Shapes.Scan
{
    /// <summary>
    /// Holds coordinates, and coefficients for a polygon edge to be horizontally scanned.
    /// The edge's segment is defined with the reciprocal slope form:
    /// x = p * y + q
    /// </summary>
    internal readonly struct ScanEdge
    {
        public readonly float Y0;
        public readonly float Y1;
        private readonly double p;
        private readonly double q;

        // Store 3 small values in a single Int32, to make EdgeData more compact:
        // EdgeUp, Emit0, Emit1
        private readonly int flags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ScanEdge(PointF p0, PointF p1, int flags)
        {
            this.Y0 = p0.Y;
            this.Y1 = p1.Y;
            this.flags = flags;
            double dy = (double)p1.Y - p0.Y;

            // To improve accuracy, center the edge around zero before calculating the coefficients:
            double cx = ((double)p0.X + p1.X) * 0.5;
            double cy = ((double)p0.Y + p1.Y) * 0.5;

            // p0.X -= cx;
            // p0.Y -= cy;
            // p1.X -= cx;
            // p1.Y -= cy;
            double p0x = p0.X - cx;
            double p0y = p0.Y - cy;
            double p1x = p1.X - cx;
            double p1y = p1.Y - cy;

            this.p = (p1x - p0x) / dy;
            this.q = ((p0x * p1y) - (p1x * p0y)) / dy;

            // After centering, the equation would be:
            // x = p * (y-cy) + q + cx
            // Adjust  the coefficients, so we no longer need (cx,cy):
            this.q += cx - (this.p * cy);
        }

        // True when non-horizontal edge is oriented upwards in screen coords
        public bool EdgeUp => (this.flags & 1) == 1;

        // How many times to include the intersection result
        // When the scanline intersects the endpoint at Y0.
        public int EmitV0 => (this.flags & 0b00110) >> 1;

        // How many times to include the intersection result
        // When the scanline intersects the endpoint at Y1.
        public int EmitV1 => (this.flags & 0b11000) >> 3;

        public float GetX(float y) => (float)((this.p * y) + this.q);

        public override string ToString()
            => $"(Y0={this.Y0} Y1={this.Y1} E0={this.EmitV0} E1={this.EmitV1} {(this.EdgeUp ? "Up" : "Down")} p={this.p} q={this.q})";
    }
}