using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Shaper2D.Primitives
{
    /// <summary>
    /// Represents an ordered pair of floating point x- and y-coordinates that defines a point in
    /// a two-dimensional plane.
    /// </summary>
    /// <remarks>
    /// This struct is fully mutable. This is done (against the guidelines) for the sake of performance,
    /// as it avoids the need to create new values for modification operations.
    /// </remarks>
    public struct PointF : IEquatable<PointF>
    {
        /// <summary>
        /// Represents a <see cref="PointF"/> that has X and Y values set to zero.
        /// </summary>
        public static readonly PointF Empty = default(PointF);
        /// <summary>
        /// The backing vector for SIMD support.
        /// </summary>
        private Vector2 backingVector;

        /// <summary>
        /// Initializes a new instance of the <see cref="Point"/> struct.
        /// </summary>
        /// <param name="x">The horizontal position of the point.</param>
        /// <param name="y">The vertical position of the point.</param>
        public PointF(float x, float y)
            : this()
        {
            this.backingVector = new Vector2(x, y);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Point"/> struct.
        /// </summary>
        /// <param name="vector">
        /// The vector representing the width and height.
        /// </param>
        public PointF(Vector2 vector)
        {
            this.backingVector = vector;
        }

        /// <summary>
        /// Gets or sets the x-coordinate of this <see cref="Point"/>.
        /// </summary>
        public float X => backingVector.X;

        /// <summary>
        /// Gets or sets the y-coordinate of this <see cref="Point"/>.
        /// </summary>
        public float Y => backingVector.Y;

        /// <summary>
        /// Gets a value indicating whether this <see cref="Point"/> is empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsEmpty => this.Equals(Empty);

        /// <summary>
        /// Computes the sum of adding two points.
        /// </summary>
        /// <param name="left">The point on the left hand of the operand.</param>
        /// <param name="right">The point on the right hand of the operand.</param>
        /// <returns>
        /// The <see cref="Point"/>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PointF operator +(PointF left, PointF right)
        {
            return new PointF(left.backingVector + right.backingVector);
        }

        /// <summary>
        /// Computes the difference left by subtracting one point from another.
        /// </summary>
        /// <param name="left">The point on the left hand of the operand.</param>
        /// <param name="right">The point on the right hand of the operand.</param>
        /// <returns>
        /// The <see cref="Point"/>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PointF operator -(PointF left, PointF right)
        {
            return new PointF(left.backingVector - right.backingVector);
        }

        /// <summary>
        /// Compares two <see cref="Point"/> objects for equality.
        /// </summary>
        /// <param name="left">
        /// The <see cref="Point"/> on the left side of the operand.
        /// </param>
        /// <param name="right">
        /// The <see cref="Point"/> on the right side of the operand.
        /// </param>
        /// <returns>
        /// True if the current left is equal to the <paramref name="right"/> parameter; otherwise, false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(PointF left, PointF right)
        {
            return left.backingVector == right.backingVector;
        }

        /// <summary>
        /// Compares two <see cref="Point"/> objects for inequality.
        /// </summary>
        /// <param name="left">
        /// The <see cref="Point"/> on the left side of the operand.
        /// </param>
        /// <param name="right">
        /// The <see cref="Point"/> on the right side of the operand.
        /// </param>
        /// <returns>
        /// True if the current left is unequal to the <paramref name="right"/> parameter; otherwise, false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(PointF left, PointF right)
        {
            return left.backingVector != right.backingVector;
        }
        
        /// <summary>
        /// Gets a <see cref="Vector2"/> representation for this <see cref="Point"/>.
        /// </summary>
        /// <returns>A <see cref="Vector2"/> representation for this object.</returns>
        public Vector2 ToVector2()
        {
            return new Vector2(this.backingVector.X, this.backingVector.Y);
        }

        /// <summary>
        /// Translates this <see cref="Point"/> by the specified amount.
        /// </summary>
        /// <param name="dx">The amount to offset the x-coordinate.</param>
        /// <param name="dy">The amount to offset the y-coordinate.</param>
        public void Offset(int dx, int dy)
        {
            this.backingVector.X += dx;
            this.backingVector.Y += dy;
        }

        /// <summary>
        /// Translates this <see cref="Point"/> by the specified amount.
        /// </summary>
        /// <param name="p">The <see cref="Point"/> used offset this <see cref="Point"/>.</param>
        public void Offset(PointF p)
        {
            this.backingVector += p.backingVector;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return this.GetHashCode(this);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (this.IsEmpty)
            {
                return "Point [ Empty ]";
            }

            return $"Point [ X={this.X}, Y={this.Y} ]";
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is PointF)
            {
                return this.Equals((PointF)obj);
            }

            return false;
        }

        /// <inheritdoc/>
        public bool Equals(PointF other)
        {
            return this.backingVector == other.backingVector;
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <param name="point">
        /// The instance of <see cref="Point"/> to return the hash code for.
        /// </param>
        /// <returns>
        /// A 32-bit signed integer that is the hash code for this instance.
        /// </returns>
        private int GetHashCode(PointF point)
        {
            return point.backingVector.GetHashCode();
        }
    }
}
