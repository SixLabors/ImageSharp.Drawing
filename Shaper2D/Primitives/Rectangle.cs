using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Shaper2D.Primitives
{
    
        public struct RectangleF : IEquatable<RectangleF>
        {
        /// <summary>
        /// Represents a <see cref="RectangleF"/> that has X, Y, Width, and Height values set to zero.
        /// </summary>
        public static readonly RectangleF Empty = default(RectangleF);

            /// <summary>
            /// The backing vector for SIMD support.
            /// </summary>
            private Vector4 backingVector;

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangleF"/> struct.
        /// </summary>
        /// <param name="x">The horizontal position of the rectangle.</param>
        /// <param name="y">The vertical position of the rectangle.</param>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        public RectangleF(float x, float y, float width, float height)
            {
                this.backingVector = new Vector4(x, y, width, height);
            }

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangleF"/> struct.
        /// </summary>
        /// <param name="vector">The vector.</param>
        public RectangleF(Vector4 vector)
            {
                this.backingVector = vector;
            }

        /// <summary>
        /// Gets or sets the x-coordinate of this <see cref="RectangleF"/>.
        /// </summary>
        public float X
            {
                get
                {
                    return this.backingVector.X;
                }

                set
                {
                    this.backingVector.X = value;
                }
            }

        /// <summary>
        /// Gets or sets the y-coordinate of this <see cref="RectangleF"/>.
        /// </summary>
        public float Y
            {
                get
                {
                    return this.backingVector.Y;
                }

                set
                {
                    this.backingVector.Y = value;
                }
            }

        /// <summary>
        /// Gets or sets the width of this <see cref="RectangleF"/>.
        /// </summary>
        public float Width
            {
                get
                {
                    return this.backingVector.Z;
                }

                set
                {
                    this.backingVector.Z = value;
                }
            }

        /// <summary>
        /// Gets or sets the height of this <see cref="RectangleF"/>.
        /// </summary>
        public float Height
            {
                get
                {
                    return this.backingVector.W;
                }

                set
                {
                    this.backingVector.W = value;
                }
            }

        /// <summary>
        /// Gets a value indicating whether this <see cref="RectangleF"/> is empty.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
            public bool IsEmpty => this.Equals(Empty);

            /// <summary>
            /// Gets the y-coordinate of the top edge of this <see cref="Rectangle"/>.
            /// </summary>
            public float Top => this.Y;

            /// <summary>
            /// Gets the x-coordinate of the right edge of this <see cref="Rectangle"/>.
            /// </summary>
            public float Right => this.X + this.Width;

            /// <summary>
            /// Gets the y-coordinate of the bottom edge of this <see cref="Rectangle"/>.
            /// </summary>
            public float Bottom => this.Y + this.Height;

            /// <summary>
            /// Gets the x-coordinate of the left edge of this <see cref="Rectangle"/>.
            /// </summary>
            public float Left => this.X;

            /// <summary>
            /// Computes the sum of adding two rectangles.
            /// </summary>
            /// <param name="left">The rectangle on the left hand of the operand.</param>
            /// <param name="right">The rectangle on the right hand of the operand.</param>
            /// <returns>
            /// The <see cref="Rectangle"/>
            /// </returns>
            public static RectangleF operator +(RectangleF left, RectangleF right)
            {
                return new RectangleF(left.backingVector + right.backingVector);
            }

            /// <summary>
            /// Computes the difference left by subtracting one rectangle from another.
            /// </summary>
            /// <param name="left">The rectangle on the left hand of the operand.</param>
            /// <param name="right">The rectangle on the right hand of the operand.</param>
            /// <returns>
            /// The <see cref="Rectangle"/>
            /// </returns>
            public static RectangleF operator -(RectangleF left, RectangleF right)
            {
                return new RectangleF(left.backingVector - right.backingVector);
            }

            /// <summary>
            /// Compares two <see cref="Rectangle"/> objects for equality.
            /// </summary>
            /// <param name="left">
            /// The <see cref="Rectangle"/> on the left side of the operand.
            /// </param>
            /// <param name="right">
            /// The <see cref="Rectangle"/> on the right side of the operand.
            /// </param>
            /// <returns>
            /// True if the current left is equal to the <paramref name="right"/> parameter; otherwise, false.
            /// </returns>
            public static bool operator ==(RectangleF left, RectangleF right)
            {
                return left.Equals(right);
            }

            /// <summary>
            /// Compares two <see cref="Rectangle"/> objects for inequality.
            /// </summary>
            /// <param name="left">
            /// The <see cref="Rectangle"/> on the left side of the operand.
            /// </param>
            /// <param name="right">
            /// The <see cref="Rectangle"/> on the right side of the operand.
            /// </param>
            /// <returns>
            /// True if the current left is unequal to the <paramref name="right"/> parameter; otherwise, false.
            /// </returns>
            public static bool operator !=(RectangleF left, RectangleF right)
            {
                return !left.Equals(right);
            }

            /// <summary>
            /// Returns the center point of the given <see cref="Rectangle"/>
            /// </summary>
            /// <param name="rectangle">The rectangle</param>
            /// <returns><see cref="Point"/></returns>
            public static Point Center(Rectangle rectangle)
            {
                return new Point(rectangle.Left + (rectangle.Width / 2), rectangle.Top + (rectangle.Height / 2));
            }

            /// <summary>
            /// Determines if the specfied point is contained within the rectangular region defined by
            /// this <see cref="Rectangle"/>.
            /// </summary>
            /// <param name="x">The x-coordinate of the given point.</param>
            /// <param name="y">The y-coordinate of the given point.</param>
            /// <returns>The <see cref="bool"/></returns>
            public bool Contains(int x, int y)
            {
                // TODO: SIMD?
                return this.X <= x
                       && x < this.Right
                       && this.Y <= y
                       && y < this.Bottom;
            }


            /// <summary>
            /// Determines if the specfied <see cref="Rectangle"/> intersects the rectangular region defined by
            /// this <see cref="Rectangle"/>.
            /// </summary>
            /// <param name="rect">The other Rectange </param>
            /// <returns>The <see cref="bool"/></returns>
            public bool Intersects(Rectangle rect)
            {
                return rect.Left <= this.Right && rect.Right >= this.Left
                    &&
                    rect.Top <= this.Bottom && rect.Bottom >= this.Top;
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
                    return "Rectangle [ Empty ]";
                }

                return
                    $"Rectangle [ X={this.X}, Y={this.Y}, Width={this.Width}, Height={this.Height} ]";
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (obj is Rectangle)
                {
                    return this.Equals((Rectangle)obj);
                }

                return false;
            }

            /// <inheritdoc/>
            public bool Equals(Rectangle other)
            {
                return this.backingVector.Equals(other.backingVector);
            }

            /// <summary>
            /// Returns the hash code for this instance.
            /// </summary>
            /// <param name="rectangle">
            /// The instance of <see cref="Rectangle"/> to return the hash code for.
            /// </param>
            /// <returns>
            /// A 32-bit signed integer that is the hash code for this instance.
            /// </returns>
            private int GetHashCode(Rectangle rectangle)
            {
                return rectangle.backingVector.GetHashCode();
            }
        }
}
