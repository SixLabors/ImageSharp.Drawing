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
    /// Stores an ordered pair of integers, which specify a height and width.
    /// </summary>
    /// <remarks>
    /// This struct is fully mutable. This is done (against the guidelines) for the sake of performance,
    /// as it avoids the need to create new values for modification operations.
    /// </remarks>
    public struct SizeF : IEquatable<SizeF>
    {
        /// <summary>
        /// Represents a <see cref="SizeF"/> that has Width and Height values set to zero.
        /// </summary>
        public static readonly SizeF Empty = default(SizeF);

        /// <summary>
        /// The backing vector for SIMD support.
        /// </summary>
        private Vector2 backingVector;

        /// <summary>
        /// Initializes a new instance of the <see cref="Size"/> struct.
        /// </summary>
        /// <param name="width">The width of the size.</param>
        /// <param name="height">The height of the size.</param>
        public SizeF(float width, float height)
        {
            backingVector = new Vector2(width, height);
        }

        public SizeF(Vector2 vector)
        {
            backingVector = vector;
        }

        /// <summary>
        /// Gets or sets the width of this <see cref="SizeF"/>.
        /// </summary>
        public float Width => backingVector.X;

        /// <summary>
        /// Gets or sets the height of this <see cref="SizeF"/>.
        /// </summary>
        public float Height => backingVector.Y;

        /// <summary>
        /// Gets a value indicating whether this <see cref="SizeF"/> is empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsEmpty => this.Equals(Empty);

        /// <summary>
        /// Computes the sum of adding two sizes.
        /// </summary>
        /// <param name="left">The size on the left hand of the operand.</param>
        /// <param name="right">The size on the right hand of the operand.</param>
        /// <returns>
        /// The <see cref="Size"/>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SizeF operator +(SizeF left, SizeF right)
        {
            return new SizeF(left.backingVector + right.backingVector);
        }

        /// <summary>
        /// Computes the difference left by subtracting one size from another.
        /// </summary>
        /// <param name="left">The size on the left hand of the operand.</param>
        /// <param name="right">The size on the right hand of the operand.</param>
        /// <returns>
        /// The <see cref="SizeF"/>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SizeF operator -(SizeF left, SizeF right)
        {
            return new SizeF(left.backingVector - right.backingVector);
        }

        /// <summary>
        /// Compares two <see cref="Size"/> objects for equality.
        /// </summary>
        /// <param name="left">
        /// The <see cref="Size"/> on the left side of the operand.
        /// </param>
        /// <param name="right">
        /// The <see cref="Size"/> on the right side of the operand.
        /// </param>
        /// <returns>
        /// True if the current left is equal to the <paramref name="right"/> parameter; otherwise, false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(SizeF left, SizeF right)
        {
            return left.backingVector == right.backingVector; 
        }

        /// <summary>
        /// Compares two <see cref="Size"/> objects for inequality.
        /// </summary>
        /// <param name="left">
        /// The <see cref="Size"/> on the left side of the operand.
        /// </param>
        /// <param name="right">
        /// The <see cref="Size"/> on the right side of the operand.
        /// </param>
        /// <returns>
        /// True if the current left is unequal to the <paramref name="right"/> parameter; otherwise, false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(SizeF left, SizeF right)
        {
            return left.backingVector != right.backingVector;
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
                return "Size [ Empty ]";
            }

            return $"Size [ Width={this.Width}, Height={this.Height} ]";
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj)
        {
            if (obj is SizeF)
            {
                return this.Equals((SizeF)obj);
            }

            return false;
        }

        /// <inheritdoc/>
        public bool Equals(SizeF other)
        {
            return this.backingVector == other.backingVector;
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <param name="size">
        /// The instance of <see cref="Size"/> to return the hash code for.
        /// </param>
        /// <returns>
        /// A 32-bit signed integer that is the hash code for this instance.
        /// </returns>
        private int GetHashCode(SizeF size)
        {
            return size.backingVector.GetHashCode();
        }
    }
}
