// <copyright file="Size.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.ComponentModel;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Stores an ordered pair of integers, which specify a height and width.
    /// </summary>
    /// <remarks>
    /// This struct is fully mutable. This is done (against the guidelines) for the sake of performance,
    /// as it avoids the need to create new values for modification operations.
    /// </remarks>
    public struct Size : IEquatable<Size>
    {
        /// <summary>
        /// Represents a <see cref="Size"/> that has Width and Height values set to zero.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static readonly Size Empty = default(Size);

        private readonly Vector2 backingVector;

        private readonly bool isSet;

        /// <summary>
        /// Initializes a new instance of the <see cref="Size"/> struct.
        /// </summary>
        /// <param name="width">The width of the size.</param>
        /// <param name="height">The height of the size.</param>
        public Size(float width, float height)
            : this(new Vector2(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Size"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public Size(Vector2 vector)
        {
            this.backingVector = vector;
            this.isSet = true;
        }

        /// <summary>
        /// Gets the width of this <see cref="Size"/>.
        /// </summary>
        public float Width => this.backingVector.X;

        /// <summary>
        /// Gets the height of this <see cref="Size"/>.
        /// </summary>
        public float Height => this.backingVector.Y;

        /// <summary>
        /// Gets a value indicating whether this <see cref="Size"/> is empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsEmpty => this.Equals(Empty);

        /// <summary>
        /// Computes the sum of adding two Sizes.
        /// </summary>
        /// <param name="left">The Size on the left hand of the operand.</param>
        /// <param name="right">The Size on the right hand of the operand.</param>
        /// <returns>
        /// The <see cref="Size"/>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Size operator +(Size left, Size right)
        {
            return new Size(left.backingVector + right.backingVector);
        }

        /// <summary>
        /// Computes the difference left by subtracting one Size from another.
        /// </summary>
        /// <param name="left">The Size on the left hand of the operand.</param>
        /// <param name="right">The Size on the right hand of the operand.</param>
        /// <returns>
        /// The <see cref="Size"/>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Size operator -(Size left, Size right)
        {
            return new Size(left.backingVector - right.backingVector);
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
        public static bool operator ==(Size left, Size right)
        {
            if (left.isSet && right.isSet)
            {
                return left.backingVector == right.backingVector;
            }

            return left.isSet == right.isSet;
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
        public static bool operator !=(Size left, Size right)
        {
            if (left.isSet && right.isSet)
            {
                return left.backingVector != right.backingVector;
            }

            return left.isSet != right.isSet;
        }

        /// <summary>
        /// returns the size as a <see cref="Vector2"/>
        /// </summary>
        /// <returns>The size as a vector2.</returns>
        public Vector2 ToVector2()
        {
            return this.backingVector;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return this.backingVector.GetHashCode();
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
            if (obj is Size)
            {
                return this.Equals((Size)obj);
            }

            return false;
        }

        /// <inheritdoc/>
        public bool Equals(Size other)
        {
            return this == other;
        }
    }
}
