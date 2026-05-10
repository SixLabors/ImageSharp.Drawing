// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
        // TODO. Make this a void. We can reverse the segments in place and then reverse the points in place as well.
        IEnumerable<LinearLineSegment> segments = path.Flatten().Select(static p => new LinearLineSegment(ReversePoints(p.Points.Span)));
        bool closed = false;
        if (path is ISimplePath sp)
        {
            closed = sp.IsClosed;
        }

        return closed ? new Polygon(segments) : new Path(segments);
    }

    private static PointF[] ReversePoints(ReadOnlySpan<PointF> points)
    {
        PointF[] reversed = new PointF[points.Length];
        for (int i = 0; i < reversed.Length; i++)
        {
            reversed[i] = points[points.Length - 1 - i];
        }

        return reversed;
    }
}
