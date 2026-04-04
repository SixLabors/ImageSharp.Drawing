// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing;

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Provides low-level geometry helpers for polygon winding and segment intersection.
/// </summary>
/// <remarks>
/// Polygon methods expect a closed ring where the first point is repeated as the last point.
/// Orientation signs are defined using world-space math conventions (Y points up):
/// positive signed area is counter-clockwise and negative signed area is clockwise.
/// In screen space (Y points down), the visual winding appears inverted.
/// </remarks>
internal static class PolygonUtilities
{
    // Epsilon used for floating-point tolerance. Values within +-Eps are treated as zero.
    // This reduces instability when segments are nearly parallel or endpoints are close.
    private const float Eps = 1e-3f;
    private const float MinusEps = -Eps;
    private const float OnePlusEps = 1 + Eps;

    /// <summary>
    /// Ensures that a closed polygon ring matches the expected orientation.
    /// </summary>
    /// <param name="polygon">Polygon ring to normalize in place.</param>
    /// <param name="expectedOrientation">
    /// Expected orientation sign:
    /// positive for counter-clockwise in world space, negative for clockwise in world space.
    /// </param>
    /// <remarks>
    /// The ring is reversed only when its orientation sign disagrees with
    /// <paramref name="expectedOrientation"/>. Degenerate rings (zero area) are not changed.
    /// </remarks>
    public static void EnsureOrientation(Span<PointF> polygon, int expectedOrientation)
    {
        if (GetPolygonOrientation(polygon) * expectedOrientation < 0)
        {
            polygon.Reverse();
        }
    }

    /// <summary>
    /// Returns the orientation sign of a closed polygon ring using the shoelace sum.
    /// </summary>
    /// <param name="polygon">Closed polygon ring.</param>
    /// <returns>
    /// -1 for clockwise, 1 for counter-clockwise, or 0 for degenerate (zero-area) input.
    /// </returns>
    private static int GetPolygonOrientation(ReadOnlySpan<PointF> polygon)
    {
        float sum = 0f;
        for (int i = 0; i < polygon.Length - 1; ++i)
        {
            PointF current = polygon[i];
            PointF next = polygon[i + 1];
            sum += (current.X * next.Y) - (next.X * current.Y);
        }

        // A tolerant compare could be used here, but edge scanning does not special-case
        // zero-area or near-zero-area input, so we keep this strict sign check.
        return Math.Sign(sum);
    }

    /// <summary>
    /// Tests whether two line segments intersect, excluding collinear overlap cases.
    /// </summary>
    /// <param name="a0">Start point of segment A.</param>
    /// <param name="a1">End point of segment A.</param>
    /// <param name="b0">Start point of segment B.</param>
    /// <param name="b1">End point of segment B.</param>
    /// <param name="intersectionPoint">
    /// Receives the intersection point when an intersection is found.
    /// If no intersection is detected, the value is not modified.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the segments intersect within their extents
    /// (including endpoints); otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This solves the two segment equations in parametric form and accepts values in [0, 1]
    /// with an epsilon margin for floating-point tolerance.
    /// Parallel and collinear pairs are rejected early (cross product ~= 0).
    /// </remarks>
    public static bool LineSegmentToLineSegmentIgnoreCollinear(
        Vector2 a0,
        Vector2 a1,
        Vector2 b0,
        Vector2 b1,
        ref Vector2 intersectionPoint)
    {
        // Direction vectors of the segments.
        float dax = a1.X - a0.X;
        float day = a1.Y - a0.Y;
        float dbx = b1.X - b0.X;
        float dby = b1.Y - b0.Y;

        // Cross product of the direction vectors. Near zero means parallel/collinear.
        float crossD = (-dbx * day) + (dax * dby);

        // Reject parallel and collinear lines. Collinear overlap is intentionally not handled.
        if (crossD is > MinusEps and < Eps)
        {
            return false;
        }

        // Solve for parameters s and t where:
        // a0 + t * (a1 - a0) = b0 + s * (b1 - b0)
        float s = ((-day * (a0.X - b0.X)) + (dax * (a0.Y - b0.Y))) / crossD;
        float t = ((dbx * (a0.Y - b0.Y)) - (dby * (a0.X - b0.X))) / crossD;

        // If both parameters are within [0,1] (with tolerance), the segments intersect.
        if (s > MinusEps && s < OnePlusEps && t > MinusEps && t < OnePlusEps)
        {
            intersectionPoint.X = a0.X + (t * dax);
            intersectionPoint.Y = a0.Y + (t * day);
            return true;
        }

        return false;
    }
}
