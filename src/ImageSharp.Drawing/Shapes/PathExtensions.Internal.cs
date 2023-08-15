// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;

namespace SixLabors.ImageSharp.Drawing;

/// <content>
/// Convenience methods that can be applied to shapes and paths.
/// </content>
public static partial class PathExtensions
{
    /// <summary>
    /// Create a path with the segment order reversed.
    /// </summary>
    /// <param name="path">The path to reverse.</param>
    /// <returns>The reversed <see cref="IPath"/>.</returns>
    internal static IPath Reverse(this IPath path)
    {
        IEnumerable<LinearLineSegment> segments = path.Flatten().Select(p => new LinearLineSegment(p.Points.ToArray().Reverse().ToArray()));
        bool closed = false;
        if (path is ISimplePath sp)
        {
            closed = sp.IsClosed;
        }

        return closed ? new Polygon(segments) : new Path(segments);
    }
}
