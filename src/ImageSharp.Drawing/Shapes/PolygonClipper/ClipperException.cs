// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.Drawing.PolygonClipper
{
    /// <summary>
    /// The exception that is thrown when an error occurs clipping a polygon.
    /// </summary>
    public class ClipperException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipperException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public ClipperException(string message)
            : base(message)
        {
        }
    }
}
