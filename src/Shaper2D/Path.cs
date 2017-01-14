// <copyright file="Path.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace Shaper2D
{
    using System.Collections.Immutable;
    using System.Numerics;

    /// <summary>
    /// A aggregate of <see cref="ILineSegment"/>s making a single logical path
    /// </summary>
    /// <seealso cref="IPath" />
    public class Path : IPath
    {
        /// <summary>
        /// The inner path.
        /// </summary>
        private readonly InternalPath innerPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="Path"/> class.
        /// </summary>
        /// <param name="segment">The segment.</param>
        public Path(params ILineSegment[] segment)
        {
            this.innerPath = new InternalPath(segment, false);
        }

        /// <inheritdoc />
        public Rectangle Bounds => this.innerPath.Bounds;

        /// <inheritdoc />
        public bool IsClosed => false;

        /// <inheritdoc />
        public float Length => this.innerPath.Length;

        /// <inheritdoc />
        public ImmutableArray<Point> AsSimpleLinearPath()
        {
            return this.innerPath.Points;
        }

        /// <inheritdoc />
        public PointInfo Distance(Point point)
        {
            return this.innerPath.DistanceFromPath(point);
        }
    }
}