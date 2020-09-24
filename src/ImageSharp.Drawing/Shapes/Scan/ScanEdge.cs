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
    internal readonly struct ScanEdge
    {
        public readonly float Y0;
        public readonly float Y1;
        public readonly float P;
        public readonly float Q;

        // Store 3 small values in a single Int32, to make EdgeData more compact:
        // EdgeUp, Emit0, Emit1
        private readonly int flags;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ScanEdge(ref PointF p0, ref PointF p1, int flags)
        {
            this.Y0 = p0.Y;
            this.Y1 = p1.Y;
            this.flags = flags;
            float dy = p1.Y - p0.Y;
            this.P = (p1.X - p0.X) / dy;
            this.Q = ((p0.X * p1.Y) - (p1.X * p0.Y)) / dy;
        }

        // Edge is up in screen coords
        public bool EdgeUp => (this.flags & 1) == 1;

        // How many times to include the intersection result
        // When the scanline intersects the endpoint at Y0.
        public int EmitV0 => (this.flags & 0b00110) >> 1;

        // How many times to include the intersection result
        // When the scanline intersects the endpoint at Y1.
        public int EmitV1 => (this.flags & 0b11000) >> 3;

        private string UpDownString => this.EdgeUp ? "Up" : "Down";

        public override string ToString()
            => $"(Y0={this.Y0} Y1={this.Y1} E0={this.EmitV0} E1={this.EmitV1} {this.UpDownString} P={this.P} Q={this.Q})";
    }
}