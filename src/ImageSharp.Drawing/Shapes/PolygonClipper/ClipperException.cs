// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

/// <summary>
/// The exception that is thrown when an error occurs clipping a polygon.
/// </summary>
public class ClipperException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ClipperException"/> class.
    /// </summary>
    public ClipperException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClipperException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ClipperException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClipperException" /> class with a specified error message and a
    /// reference to the inner exception that is the cause of this exception.</summary>
    /// <param name="message">The error message that explains the reason for the exception. </param>
    /// <param name="innerException">The exception that is the cause of the current exception, or a <see langword="null"/>
    /// reference if no inner exception is specified. </param>
    public ClipperException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
