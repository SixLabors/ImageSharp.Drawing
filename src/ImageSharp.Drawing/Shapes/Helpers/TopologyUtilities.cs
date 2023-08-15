// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.Helpers;

/// <summary>
/// Implements some basic algorithms on raw data structures.
/// Polygons are represented with a span of points,
/// where first point should be repeated at the end.
/// </summary>
/// <remarks>
/// Positive orientation means Clockwise in world coordinates (positive direction goes UP on paper).
/// Since the Drawing library deals mostly with Screen coordinates where this is opposite,
/// we use different terminology here to avoid confusion.
/// </remarks>
internal static class TopologyUtilities
{
    /// <summary>
    /// Positive: CCW in world coords (CW on screen)
    /// Negative: CW in world coords (CCW on screen)
    /// </summary>
    public static void EnsureOrientation(Span<PointF> polygon, int expectedOrientation)
    {
        if (GetPolygonOrientation(polygon) * expectedOrientation < 0)
        {
            polygon.Reverse();
        }
    }

    /// <summary>
    /// Zero: area is 0
    /// Positive: CCW in world coords (CW on screen)
    /// Negative: CW in world coords (CCW on screen)
    /// </summary>
    private static int GetPolygonOrientation(ReadOnlySpan<PointF> polygon)
    {
        float sum = 0f;
        for (int i = 0; i < polygon.Length - 1; ++i)
        {
            PointF curr = polygon[i];
            PointF next = polygon[i + 1];
            sum += (curr.X * next.Y) - (next.X * curr.Y);
        }

        // Normally, this should be a tolerant comparison, we don't have a special path for zero-area
        // (or for self-intersecting, semi-zero-area) polygons in edge scanning.
        return Math.Sign(sum);
    }
}
