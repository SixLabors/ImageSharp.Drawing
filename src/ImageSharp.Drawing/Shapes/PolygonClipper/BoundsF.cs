// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper
{
    internal struct BoundsF
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;

        public BoundsF(float l, float t, float r, float b)
        {
            Guard.MustBeGreaterThanOrEqualTo(r, l, nameof(r));
            Guard.MustBeGreaterThanOrEqualTo(b, t, nameof(r));

            this.Left = l;
            this.Top = t;
            this.Right = r;
            this.Bottom = b;
        }

        public BoundsF(BoundsF bounds)
        {
            this.Left = bounds.Left;
            this.Top = bounds.Top;
            this.Right = bounds.Right;
            this.Bottom = bounds.Bottom;
        }

        public BoundsF(bool isValid)
        {
            if (isValid)
            {
                this.Left = 0;
                this.Top = 0;
                this.Right = 0;
                this.Bottom = 0;
            }
            else
            {
                this.Left = float.MaxValue;
                this.Top = float.MaxValue;
                this.Right = -float.MaxValue;
                this.Bottom = -float.MaxValue;
            }
        }

        public float Width
        {
            get => this.Right - this.Left;
            set => this.Right = this.Left + value;
        }

        public float Height
        {
            get => this.Bottom - this.Top;
            set => this.Bottom = this.Top + value;
        }

        public bool IsEmpty()
            => this.Bottom <= this.Top || this.Right <= this.Left;

        public Vector2 MidPoint()
            => new Vector2(this.Left + this.Right, this.Top + this.Bottom) * .5F;

        public bool Contains(Vector2 pt)
            => pt.X > this.Left
            && pt.X < this.Right
            && pt.Y > this.Top && pt.Y < this.Bottom;

        public bool Contains(BoundsF bounds)
            => bounds.Left >= this.Left
            && bounds.Right <= this.Right
            && bounds.Top >= this.Top
            && bounds.Bottom <= this.Bottom;

        public bool Intersects(BoundsF bounds)
            => (Math.Max(this.Left, bounds.Left) < Math.Min(this.Right, bounds.Right))
            && (Math.Max(this.Top, bounds.Top) < Math.Min(this.Bottom, bounds.Bottom));

        public PathF AsPath()
            => new(4)
            {
                new PointF(this.Left, this.Top),
                new PointF(this.Right, this.Top),
                new PointF(this.Right, this.Bottom),
                new PointF(this.Left, this.Bottom)
            };
    }
}
