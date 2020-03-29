// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// A aggregate of <see cref="IPath"/>s to apply common operations to them.
    /// </summary>
    /// <seealso cref="IPath" />
    public class PathCollection : IPathCollection
    {
        private readonly IPath[] paths;

        /// <summary>
        /// Initializes a new instance of the <see cref="PathCollection"/> class.
        /// </summary>
        /// <param name="paths">The collection of paths</param>
        public PathCollection(IEnumerable<IPath> paths)
            : this(paths?.ToArray())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PathCollection"/> class.
        /// </summary>
        /// <param name="paths">The collection of paths</param>
        public PathCollection(params IPath[] paths)
        {
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));

            if (this.paths.Length == 0)
            {
                this.Bounds = new RectangleF(0, 0, 0, 0);
            }
            else
            {
                float minX = this.paths.Min(x => x.Bounds.Left);
                float maxX = this.paths.Max(x => x.Bounds.Right);

                float minY = this.paths.Min(x => x.Bounds.Top);
                float maxY = this.paths.Max(x => x.Bounds.Bottom);

                this.Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }
        }

        /// <inheritdoc />
        public RectangleF Bounds { get; }

        /// <inheritdoc />
        public IEnumerator<IPath> GetEnumerator() => ((IEnumerable<IPath>)this.paths).GetEnumerator();

        /// <inheritdoc />
        public IPathCollection Transform(Matrix3x2 matrix)
        {
            var result = new IPath[this.paths.Length];

            for (int i = 0; i < this.paths.Length && i < result.Length; i++)
            {
                result[i] = this.paths[i].Transform(matrix);
            }

            return new PathCollection(result);
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<IPath>)this.paths).GetEnumerator();
    }
}