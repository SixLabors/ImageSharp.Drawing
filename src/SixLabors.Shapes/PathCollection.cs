// <copyright file="Path.cs" company="Scott Williams">
// Copyright (c) Scott Williams and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace SixLabors.Shapes
{
    using SixLabors.Primitives;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;

    /// <summary>
    /// A aggregate of <see cref="IPath"/>s to apply common operations to them.
    /// </summary>
    /// <seealso cref="IPath" />
    public class PathCollection : IPathCollection
    {
        public IPath[] paths;

        public PathCollection(IEnumerable<IPath> paths)
        {
            Guard.NotNull(paths, nameof(paths));

            this.paths = paths.ToArray();
            if (this.paths.Length == 0)
            {
                this.Bounds = new RectangleF(0, 0, 0, 0);
            }
            else
            {

                float minX = paths.Min(x => x.Bounds.Left);
                float maxX = paths.Max(x => x.Bounds.Right);

                float minY = paths.Min(x => x.Bounds.Top);
                float maxY = paths.Max(x => x.Bounds.Bottom);

                this.Bounds = new RectangleF(minX, minY, maxX - minX, maxY - minY);
            }
        }
        
        public PathCollection(params IPath[] paths)
            : this((IEnumerable<IPath>)paths)
        {
        }

        /// <inheritdoc />
        public RectangleF Bounds { get; }

        /// <inheritdoc />
        public IEnumerator<IPath> GetEnumerator() => ((IEnumerable<IPath>)this.paths).GetEnumerator();

        /// <inheritdoc />
        public IPathCollection Transform(Matrix3x2 matrix)
        {
            var result = new IPath[this.paths.Length];
            for(var i = 0; i < this.paths.Length && i < result.Length; i++)
            {
                result[i] = this.paths[i].Transform(matrix);
            }

            return new PathCollection(result);
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<IPath>)this.paths).GetEnumerator();
    }
}