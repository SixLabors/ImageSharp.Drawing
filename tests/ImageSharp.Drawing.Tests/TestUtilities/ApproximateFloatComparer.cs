// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    /// <summary>
    /// Allows the approximate comparison of single precision floating point values.
    /// </summary>
    internal readonly struct ApproximateFloatComparer :
        IEqualityComparer<float>,
        IEqualityComparer<Vector2>,
        IEqualityComparer<PointF>,
        IEqualityComparer<Vector4>,
        IEqualityComparer<ColorMatrix>
    {
        private readonly float epsilon;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApproximateFloatComparer"/> class.
        /// </summary>
        /// <param name="epsilon">The comparison error difference epsilon to use.</param>
        public ApproximateFloatComparer(float epsilon = 1F) => this.epsilon = epsilon;

        /// <inheritdoc/>
        public bool Equals(float x, float y)
        {
            float d = x - y;

            return d >= -this.epsilon && d <= this.epsilon;
        }

        /// <inheritdoc/>
        public int GetHashCode(float obj) => obj.GetHashCode();

        /// <inheritdoc/>
        public bool Equals(Vector2 a, Vector2 b) => this.Equals(a.X, b.X) && this.Equals(a.Y, b.Y);

        /// <inheritdoc/>
        public bool Equals(PointF a, PointF b) => this.Equals(a.X, b.X) && this.Equals(a.Y, b.Y);

        /// <inheritdoc/>
        public int GetHashCode(Vector2 obj) => obj.GetHashCode();

        /// <inheritdoc/>
        public int GetHashCode(PointF obj) => obj.GetHashCode();

        /// <inheritdoc/>
        public bool Equals(Vector4 a, Vector4 b) => this.Equals(a.X, b.X) && this.Equals(a.Y, b.Y) && this.Equals(a.Z, b.Z) && this.Equals(a.W, b.W);

        /// <inheritdoc/>
        public int GetHashCode(Vector4 obj) => obj.GetHashCode();

        /// <inheritdoc/>
        public bool Equals(ColorMatrix x, ColorMatrix y)
            => this.Equals(x.M11, y.M11) && this.Equals(x.M12, y.M12) && this.Equals(x.M13, y.M13) && this.Equals(x.M14, y.M14)
            && this.Equals(x.M21, y.M21) && this.Equals(x.M22, y.M22) && this.Equals(x.M23, y.M23) && this.Equals(x.M24, y.M24)
            && this.Equals(x.M31, y.M31) && this.Equals(x.M32, y.M32) && this.Equals(x.M33, y.M33) && this.Equals(x.M34, y.M34)
            && this.Equals(x.M41, y.M41) && this.Equals(x.M42, y.M42) && this.Equals(x.M43, y.M43) && this.Equals(x.M44, y.M44)
            && this.Equals(x.M51, y.M51) && this.Equals(x.M52, y.M52) && this.Equals(x.M53, y.M53) && this.Equals(x.M54, y.M54);

        /// <inheritdoc/>
        public int GetHashCode(ColorMatrix obj) => obj.GetHashCode();
    }
}
