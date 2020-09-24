// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing
{
    internal readonly struct TolerantComparer
    {
        private readonly float negEps;
        private readonly float negEps2;

        public TolerantComparer(float eps)
        {
            this.Eps = eps;
            this.Eps2 = eps * eps;
            this.negEps = -eps;
            this.negEps2 = -this.Eps2;
        }

        /// <summary>
        /// Gets the epsilon value.
        /// </summary>
        public float Eps { get; }

        /// <summary>
        /// Gets the SQUARED Epsilon value.
        /// </summary>
        public float Eps2 { get; }

        public bool IsZero(float x) => x <= this.Eps && x >= this.negEps;

        public bool IsGreater(float a, float b) => a > b + this.Eps;

        public bool IsLess(float a, float b) => a < b - this.Eps;

        public bool AreEqual(float a, float b)
        {
            float d = a - b;
            return d < this.Eps && d > this.negEps;
        }

        public bool AreEqual(PointF a, PointF b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return this.IsZero(dx) && this.IsZero(dy);
        }

        public bool IsPositive(float x) => x > this.Eps;

        public bool IsNegative(float x) => x < this.negEps;

        public bool IsGreaterOrEqual(float a, float b) => a >= b - this.Eps;

        public bool IsLessOrEqual(float a, float b) => b >= a - this.Eps;

        public int Sign(float a)
        {
            if (a >= this.Eps)
            {
                return 1;
            }

            if (a <= this.negEps)
            {
                return -1;
            }

            return 0;
        }

        public bool IsZero2(float x)
        {
            return x > this.negEps2 && x < this.Eps2;
        }

        public bool IsGreater2(float a, float b)
        {
            return a > b + this.Eps2;
        }

        public bool IsLess2(float a, float b)
        {
            return a < b - this.Eps2;
        }

        public bool AreEqual2(float a, float b)
        {
            var d = a - b;
            return d < this.Eps2 && d > this.negEps2;
        }

        public bool IsPositive2(float x)
        {
            return x > this.Eps2;
        }

        public bool IsNegative2(float x)
        {
            return x < this.negEps2;
        }

        public int Sign2(float a)
        {
            if (a > this.Eps2)
            {
                return 1;
            }

            if (a < this.negEps2)
            {
                return -1;
            }

            return 0;
        }
    }
}