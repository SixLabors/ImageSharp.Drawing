// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// A path that is always empty.
    /// </summary>
    public sealed class EmptyPath : IPath
    {
        private EmptyPath(PathTypes pathType) => this.PathType = pathType;

        /// <summary>
        /// Gets the closed path instance of the empty path
        /// </summary>
        public static EmptyPath ClosedPath { get; } = new EmptyPath(PathTypes.Closed);

        /// <summary>
        /// Gets the open path instance of the empty path
        /// </summary>
        public static EmptyPath OpenPath { get; } = new EmptyPath(PathTypes.Open);

        /// <inheritdoc />
        public PathTypes PathType { get; }

        /// <inheritdoc />
        public RectangleF Bounds => RectangleF.Empty;

        /// <inheritdoc />
        public IPath AsClosedPath() => ClosedPath;

        /// <inheritdoc />
        public IEnumerable<ISimplePath> Flatten() => Array.Empty<ISimplePath>();

        /// <inheritdoc />
        public IPath Transform(Matrix3x2 matrix) => this;
    }
}
