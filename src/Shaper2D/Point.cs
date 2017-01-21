// <copyright file="Point.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System;
    using System.ComponentModel;
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Represents an ordered pair of integer x- and y-coordinates that defines a point in
    /// a two-dimensional plane.
    /// </summary>
    /// <remarks>
    /// This struct is fully mutable. This is done (against the guidelines) for the sake of performance,
    /// as it avoids the need to create new values for modification operations.
    /// </remarks>
    public struct Point : IEquatable<Point>
    {
        /// <summary>
        /// Represents an unset <see cref="Point"/>.
        /// </summary>
        public static readonly Point Empty = default(Point);

        /// <summary>
        /// Represents a <see cref="Point"/> that has X and Y values set to zero.
        /// </summary>
        public static readonly Point Zero = new Point(0, 0);

        private readonly Vector2 backingVector;

        private bool isSet;

        /// <summary>
        /// Initializes a new instance of the <see cref="Point"/> struct.
        /// </summary>
        /// <param name="x">The horizontal position of the point.</param>
        /// <param name="y">The vertical position of the point.</param>
        public Point(float x, float y)
            : this(new Vector2(x, y))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Point"/> struct.
        /// </summary>
        /// <param name="vector">
        /// The vector representing the width and height.
        /// </param>
        public Point(Vector2 vector)
        {
            this.backingVector = vector;
            this.isSet = true;
        }

        /// <summary>
        /// Gets the x-coordinate of this <see cref="Point"/>.
        /// </summary>
        public float X => this.backingVector.X;

        /// <summary>
        /// Gets the y-coordinate of this <see cref="Point"/>.
        /// </summary>
        public float Y => this.backingVector.Y;

        /// <summary>
        /// Gets a value indicating whether this <see cref="Point"/> is empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public bool IsEmpty => !this.isSet;

        /// <summary>
        /// Performs an implicit conversion from <see cref="Point"/> to <see cref="Vector2"/>.
        /// </summary>
        /// <param name="d">The d.</param>
        /// <returns>
        /// The result of the conversion.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Vector2(Point d)
        {
            return d.backingVector;
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="Vector2"/> to <see cref="Point"/>.
        /// </summary>
        /// <param name="d">The d.</param>
        /// <returns>
        /// The result of the conversion.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator Point(Vector2 d)
        {
            return new Point(d);
        }

        /// <summary>
        /// Computes the sum of adding two points.
        /// </summary>
        /// <param name="left">The point on the left hand of the operand.</param>
        /// <param name="right">The point on the right hand of the operand.</param>
        /// <returns>
        /// The <see cref="Point"/>
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Point operator +(Point left, Point right)
        {
            return new Point(left.backingVector + right.backingVector);
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
        public static Point operator -(Point left, Point right)
        {
            return new Point(left.backingVector - right.backingVector);
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
        public static bool operator ==(Point left, Point right)
        {
            if (left.isSet && right.isSet)
            {
                return left.backingVector == right.backingVector;
            }

            return left.isSet == right.isSet;
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
        public static bool operator !=(Point left, Point right)
        {
            if (left.isSet && right.isSet)
            {
                return left.backingVector != right.backingVector;
            }

            return left.isSet != right.isSet;
        }

        /// <summary>
        /// Gets a <see cref="Vector2"/> representation for this <see cref="Point"/>.
        /// </summary>
        /// <returns>A <see cref="Vector2"/> representation for this object.</returns>
        public Vector2 ToVector2()
        {
            return this.backingVector;
        }

        /// <summary>
        /// Translates this <see cref="Point" /> by the specified amount.
        /// </summary>
        /// <param name="p">The <see cref="Point" /> used offset this <see cref="Point" />.</param>
        /// <returns>A new point offset by the size</returns>
        public Point Offset(Size p)
        {
            return new Point(this.backingVector + p.ToVector2());
        }

        /// <summary>
        /// Applies the specified matrix to this point
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>A new point with the transofrm applied upon it.</returns>
        public Point Transform(Matrix3x2 matrix)
        {
            return new Point(Vector2.Transform(this.backingVector, matrix));
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
                return "Point [ Empty ]";
            }

            return $"Point [ X={this.X}, Y={this.Y} ]";
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj is Point)
            {
                return this == (Point)obj;
            }

            return false;
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        public bool Equals(Point other)
        {
            return this == other;
        }
    }
}