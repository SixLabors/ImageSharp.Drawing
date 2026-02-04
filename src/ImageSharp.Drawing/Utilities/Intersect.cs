// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Utilities;

/// <summary>
/// Lightweight 2D segment intersection helpers for polygon and path processing.
/// </summary>
/// <remarks>
/// This is intentionally small and allocation-free. It favors speed and numerical tolerance
/// over exhaustive classification (e.g., collinear overlap detection), which keeps it fast
/// enough for per-segment scanning in stroking or clipping preparation passes.
/// </remarks>
internal static class Intersect
{
    // Epsilon used for floating-point tolerance. We treat values within ±Eps as zero.
    // This helps avoid instability when segments are nearly parallel or endpoints are
    // very close to the intersection boundary.
    private const float Eps = 1e-3f;
    private const float MinusEps = -Eps;
    private const float OnePlusEps = 1 + Eps;

    /// <summary>
    /// Tests two line segments for intersection, ignoring collinear overlap.
    /// </summary>
    /// <param name="a0">Start of segment A.</param>
    /// <param name="a1">End of segment A.</param>
    /// <param name="b0">Start of segment B.</param>
    /// <param name="b1">End of segment B.</param>
    /// <param name="intersectionPoint">
    /// Receives the intersection point when the segments intersect within tolerance.
    /// When no intersection is detected, the value is left unchanged.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the segments intersect within their extents (including endpoints),
    /// <see langword="false"/> if they are disjoint or collinear.
    /// </returns>
    /// <remarks>
    /// The method is based on solving two parametric line equations and uses a small epsilon
    /// window around [0, 1] to account for floating-point error. Collinear cases are rejected
    /// early (crossD ≈ 0) to keep the method fast; callers that need collinear overlap detection
    /// must implement that separately.
    /// </remarks>
    public static bool LineSegmentToLineSegmentIgnoreCollinear(Vector2 a0, Vector2 a1, Vector2 b0, Vector2 b1, ref Vector2 intersectionPoint)
    {
        // Direction vectors of the segments.
        float dax = a1.X - a0.X;
        float day = a1.Y - a0.Y;
        float dbx = b1.X - b0.X;
        float dby = b1.Y - b0.Y;

        // Cross product of directions. When near zero, the lines are parallel or collinear.
        float crossD = (-dbx * day) + (dax * dby);

        // Reject parallel/collinear lines. Collinear overlap is intentionally ignored.
        if (crossD is > MinusEps and < Eps)
        {
            return false;
        }

        // Solve for parameters s and t where:
        //   a0 + t*(a1-a0) = b0 + s*(b1-b0)
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
