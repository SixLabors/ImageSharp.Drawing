// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using SixLabors.PolygonClipper;
using ClipperPolygon = SixLabors.PolygonClipper.Polygon;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

/// <summary>
/// Builders for <see cref="ClipperPolygon"/> from ImageSharp paths.
/// PolygonClipper requires explicit orientation and nesting of contours ImageSharp polygons do not contain that information
/// so we must derive that from the input.
/// </summary>
internal static class PolygonClipperFactory
{
    /// <summary>
    /// Builds a <see cref="Polygon"/> from closed <see cref="ISimplePath"/> rings.
    /// </summary>
    /// <remarks>
    /// Pipeline:
    /// 1) Filter to closed paths with ≥3 unique points, copy to <see cref="Vertex"/> rings.
    /// 2) Compute signed area via the shoelace formula to get orientation and magnitude.
    /// 3) For each ring, pick its lexicographic bottom-left vertex.
    /// 4) Parent assignment: for ring i, shoot a conceptual vertical ray downward from its bottom-left point
    ///    and test containment against all other rings using the selected <paramref name="rule"/>.
    ///    The parent is the smallest-area ring that contains the point.
    /// 5) Depth is the number of ancestors by repeated parent lookup.
    /// 6) Materialize <see cref="Contour"/>s, enforce even depth CCW and odd depth CW,
    ///    set <see cref="Contour.ParentIndex"/> and <see cref="Contour.Depth"/>, add to <see cref="Polygon"/> and wire holes.
    /// Notes:
    /// - Step 4 mirrors the parent-detection approach formalized in Martínez–Rueda 2013.
    /// - Containment uses Even-Odd or Non-Zero consistently, so glyph-like inputs can use Non-Zero.
    /// - Boundary handling: points exactly on edges are not special-cased here, which is typical for nesting.
    /// </remarks>
    /// <param name="paths">Closed simple paths.</param>
    /// <param name="rule">Containment rule for nesting, <see cref="IntersectionRule.EvenOdd"/> or <see cref="IntersectionRule.NonZero"/>.</param>
    /// <param name="polygon">Optional existing polygon to populate.</param>
    /// <returns>The constructed <see cref="ClipperPolygon"/>.</returns>
    public static ClipperPolygon FromSimplePaths(IEnumerable<ISimplePath> paths, IntersectionRule rule, ClipperPolygon? polygon = null)
    {
        // Gather rings as Vertex lists (explicitly closed), plus per-ring metadata.
        List<List<Vertex>> rings = [];
        List<double> areas = [];
        List<int> bottomLeft = [];

        foreach (ISimplePath p in paths)
        {
            if (!p.IsClosed)
            {
                // TODO: could append first point to close, but that fabricates geometry.
                continue;
            }

            ReadOnlySpan<PointF> s = p.Points.Span;
            int n = s.Length;

            // Need at least 3 points to form area.
            if (n < 3)
            {
                continue;
            }

            // Copy all points as-is.
            List<Vertex> ring = new(n);
            for (int i = 0; i < n; i++)
            {
                ring.Add(new Vertex(s[i].X, s[i].Y));
            }

            // Ensure explicit closure: start == end.
            if (ring.Count > 0)
            {
                Vertex first = ring[0];
                Vertex last = ring[^1];
                if (first.X != last.X || first.Y != last.Y)
                {
                    ring.Add(first);
                }
            }

            // After closure, still require at least 3 unique vertices.
            if (ring.Count < 4) // 3 unique + repeated first == last
            {
                continue;
            }

            rings.Add(ring);

            // SignedArea must handle a closed ring (last == first).
            areas.Add(SignedArea(ring));

            // Choose lexicographic bottom-left vertex index for nesting test.
            bottomLeft.Add(IndexOfBottomLeft(ring));
        }

        int m = rings.Count;
        if (m == 0)
        {
            return [];
        }

        // Parent assignment: pick the smallest-area ring that contains the bottom-left vertex.
        // TODO: We can use pooling here if we care about large numbers of rings.
        int[] parent = new int[m];
        Array.Fill(parent, -1);

        for (int i = 0; i < m; i++)
        {
            Vertex q = rings[i][bottomLeft[i]];
            int best = -1;
            double bestArea = double.MaxValue;

            for (int j = 0; j < m; j++)
            {
                if (i == j)
                {
                    continue;
                }

                if (IsPointInPolygon(q, rings[j], rule))
                {
                    double a = Math.Abs(areas[j]);
                    if (a < bestArea)
                    {
                        bestArea = a;
                        best = j;
                    }
                }
            }

            parent[i] = best;
        }

        // Depth = number of ancestors by following Parent links.
        // TODO: We can pool this if we care about large numbers of rings.
        int[] depth = new int[m];
        for (int i = 0; i < m; i++)
        {
            int d = 0;
            for (int pIdx = parent[i]; pIdx >= 0; pIdx = parent[pIdx])
            {
                d++;
            }

            depth[i] = d;
        }

        // Emit contours, enforce orientation by depth, and wire into polygon.
        polygon ??= [];
        for (int i = 0; i < m; i++)
        {
            Contour c = new();

            // Stream vertices into the contour. Ring is already explicitly closed.
            foreach (Vertex v in rings[i])
            {
                c.AddVertex(v);
            }

            // Orientation convention: even depth = outer => CCW, odd depth = hole => CW.
            if ((depth[i] & 1) == 0)
            {
                c.SetCounterClockwise();
            }
            else
            {
                c.SetClockwise();
            }

            // Topology annotations.
            c.ParentIndex = parent[i] >= 0 ? parent[i] : null;
            c.Depth = depth[i];

            polygon.Add(c);
        }

        // Record hole indices for parents now that indices are stable.
        for (int i = 0; i < m; i++)
        {
            int pIdx = parent[i];
            if (pIdx >= 0)
            {
                polygon[pIdx].AddHoleIndex(i);
            }
        }

        return polygon;
    }

    /// <summary>
    /// Computes the signed area of a closed ring using the shoelace formula.
    /// </summary>
    /// <param name="r">Ring of vertices.</param>
    /// <remarks>
    /// Formula:
    /// <code>
    /// A = 0.5 * Σ cross(v[j], v[i])  with j = (i - 1) mod n
    /// </code>
    /// where <c>cross(a,b) = a.X * b.Y - a.Y * b.X</c>.
    /// Interpretation:
    /// - <c>A &gt; 0</c> means counter-clockwise orientation.
    /// - <c>A &lt; 0</c> means clockwise orientation.
    /// </remarks>
    private static double SignedArea(List<Vertex> r)
    {
        double area = 0d;

        for (int i = 0, j = r.Count - 1; i < r.Count; j = i, i++)
        {
            area += Vertex.Cross(r[j], r[i]);
        }

        return 0.5d * area;
    }

    /// <summary>
    /// Returns the index of the lexicographically bottom-left vertex.
    /// </summary>
    /// <param name="r">Ring of vertices.</param>
    /// <remarks>
    /// Lexicographic order (X then Y) yields a unique seed for nesting tests and matches
    /// common parent-detection proofs that cast a ray from the lowest-leftmost point.
    /// </remarks>
    private static int IndexOfBottomLeft(List<Vertex> r)
    {
        int k = 0;

        for (int i = 1; i < r.Count; i++)
        {
            Vertex a = r[i];
            Vertex b = r[k];

            if (a.X < b.X || (a.X == b.X && a.Y < b.Y))
            {
                k = i;
            }
        }

        return k;
    }

    /// <summary>
    /// Dispatches to the selected point-in-polygon implementation.
    /// </summary>
    /// <param name="p">Query point.</param>
    /// <param name="ring">Closed ring.</param>
    /// <param name="rule">Fill rule.</param>
    private static bool IsPointInPolygon(in Vertex p, List<Vertex> ring, IntersectionRule rule)
    {
        if (rule == IntersectionRule.EvenOdd)
        {
            return PointInPolygonEvenOdd(p, ring);
        }

        return PointInPolygonNonZero(p, ring);
    }

    /// <summary>
    /// Even-odd point-in-polygon via ray casting.
    /// </summary>
    /// <param name="p">Query point.</param>
    /// <param name="ring">Closed ring.</param>
    /// <remarks>
    /// Let a horizontal ray start at <paramref name="p"/> and extend to +∞ in X.
    /// For each edge (a→b), count an intersection if the edge straddles the ray’s Y
    /// and the ray’s X is strictly less than the edge’s X at that Y:
    /// <code>
    /// intersects = ((b.Y &gt; p.Y) != (a.Y &gt; p.Y)) amp;&amp; p.X &lt; x_at_pY(a,b)
    /// </code>
    /// Parity of the count determines interior.
    /// Horizontal edges contribute zero because the straddle test excludes equal Y.
    /// Using a half-open interval on Y prevents double-counting shared vertices.
    /// </remarks>
    private static bool PointInPolygonEvenOdd(in Vertex p, List<Vertex> ring)
    {
        bool inside = false;
        int n = ring.Count;
        int j = n - 1;

        for (int i = 0; i < n; j = i, i++)
        {
            Vertex a = ring[j];
            Vertex b = ring[i];

            bool straddles = (b.Y > p.Y) != (a.Y > p.Y);

            if (straddles)
            {
                double ySpan = a.Y - b.Y;
                double xAtPY = (((a.X - b.X) * (p.Y - b.Y)) / (ySpan == 0d ? double.Epsilon : ySpan)) + b.X;

                if (p.X < xAtPY)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    /// <summary>
    /// Non-zero winding point-in-polygon.
    /// </summary>
    /// <param name="p">Query point.</param>
    /// <param name="ring">Closed ring.</param>
    /// <remarks>
    /// Scan all edges (a→b).
    /// - If the edge crosses the scanline upward (<c>a.Y ≤ p.Y &amp;&amp; b.Y &gt; p.Y</c>) and
    ///   <paramref name="p"/> lies strictly to the left of the edge, increment the winding.
    /// - If it crosses downward (<c>a.Y &gt; p.Y &amp;&amp; b.Y ≤ p.Y</c>) and <paramref name="p"/>
    ///   lies strictly to the right, decrement the winding.
    /// The point is inside iff the winding number is non-zero.
    /// Left/right is decided by the sign of the cross product of vectors a→b and a→p.
    /// </remarks>
    private static bool PointInPolygonNonZero(in Vertex p, List<Vertex> ring)
    {
        int winding = 0;
        int n = ring.Count;

        for (int i = 0, j = n - 1; i < n; j = i, i++)
        {
            Vertex a = ring[j];
            Vertex b = ring[i];

            if (a.Y <= p.Y)
            {
                if (b.Y > p.Y && IsLeft(a, b, p))
                {
                    winding++;
                }
            }
            else if (b.Y <= p.Y && !IsLeft(a, b, p))
            {
                winding--;
            }
        }

        return winding != 0;
    }

    /// <summary>
    /// Returns true if <paramref name="p"/> is strictly left of the directed edge a→b.
    /// </summary>
    /// <param name="a">Edge start.</param>
    /// <param name="b">Edge end.</param>
    /// <param name="p">Query point.</param>
    /// <remarks>
    /// Tests the sign of the 2D cross product:
    /// <code>
    /// cross = (b - a) × (p - a) = (b.X - a.X)*(p.Y - a.Y) - (b.Y - a.Y)*(p.X - a.X)
    /// </code>
    /// Left if cross &gt; 0, right if cross &lt; 0, collinear if cross == 0.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsLeft(Vertex a, Vertex b, Vertex p) => Vertex.Cross(b - a, p - a) > 0d;
}
