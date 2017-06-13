// <copyright file="Polygon.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// A shape made up of a single path made up of one of more <see cref="ILineSegment"/>s
    /// </summary>
    public class Polygon : Path
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Polygon"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Polygon(params ILineSegment[] segments)
            : base((IEnumerable<ILineSegment>)segments)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Polygon"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Polygon(IEnumerable<ILineSegment> segments)
            : base(segments)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Polygon" /> class.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public Polygon(ILineSegment segment)
            : base(segment)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Polygon"/> class.
        /// </summary>
        /// <param name="path">The path.</param>
        internal Polygon(Path path)
            : base(path)
        {
        }

        /// <summary>
        /// Gets a value indicating whether this instance is a closed path.
        /// </summary>
        protected override bool IsClosed => true;

        /// <summary>
        /// Transforms the rectangle using specified matrix.
        /// </summary>
        /// <param name="matrix">The matrix.</param>
        /// <returns>
        /// A new shape with the matrix applied to it.
        /// </returns>
        public override IPath Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            ILineSegment[] segments = new ILineSegment[this.LineSegments.Count];
            int i = 0;
            foreach (ILineSegment s in this.LineSegments)
            {
                segments[i++] = s.Transform(matrix);
            }

            return new Polygon(segments);
        }
    }
}
