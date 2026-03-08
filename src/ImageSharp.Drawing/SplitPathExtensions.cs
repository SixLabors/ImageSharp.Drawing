// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Extensions to <see cref="IPath"/> for splitting paths into dash segments
/// without performing stroke expansion.
/// </summary>
public static class SplitPathExtensions
{
    // Safety limit: if the estimated number of dash segments exceeds this threshold,
    // return the original path unsplit to avoid runaway segmentation from very short
    // patterns applied to very long paths.
    private const int MaxPatternSegments = 10000;

    /// <summary>
    /// Splits the given path into dash segments based on the provided pattern.
    /// Returns a composite path containing only the "on" segments as open sub-paths.
    /// </summary>
    /// <param name="path">The centerline path to split.</param>
    /// <param name="strokeWidth">The stroke width (pattern elements are multiples of this).</param>
    /// <param name="pattern">The dash pattern. Each element is a multiple of <paramref name="strokeWidth"/>.</param>
    /// <returns>A path containing the "on" dash segments.</returns>
    public static IPath GenerateDashes(this IPath path, float strokeWidth, ReadOnlySpan<float> pattern)
        => path.GenerateDashes(strokeWidth, pattern, startOff: false);

    /// <summary>
    /// Splits the given path into dash segments based on the provided pattern.
    /// Returns a composite path containing only the "on" segments as open sub-paths.
    /// </summary>
    /// <param name="path">The centerline path to split.</param>
    /// <param name="strokeWidth">The stroke width (pattern elements are multiples of this).</param>
    /// <param name="pattern">The dash pattern. Each element is a multiple of <paramref name="strokeWidth"/>.</param>
    /// <param name="startOff">Whether the first item in the pattern is off rather than on.</param>
    /// <returns>A path containing the "on" dash segments.</returns>
    public static IPath GenerateDashes(this IPath path, float strokeWidth, ReadOnlySpan<float> pattern, bool startOff)
    {
        if (pattern.Length < 2)
        {
            return path;
        }

        const float eps = 1e-6f;

        // Compute the absolute pattern length in path units to detect degenerate patterns.
        float patternLength = 0f;
        for (int i = 0; i < pattern.Length; i++)
        {
            patternLength += MathF.Abs(pattern[i]) * strokeWidth;
        }

        // Fallback to the original path when the dash pattern is too small to be meaningful.
        if (patternLength <= eps)
        {
            return path;
        }

        IEnumerable<ISimplePath> simplePaths = path.Flatten();
        List<IPath> segments = [];
        List<PointF> buffer = new(64);

        foreach (ISimplePath p in simplePaths)
        {
            bool online = !startOff;
            int patternPos = 0;
            float targetLength = pattern[patternPos] * strokeWidth;

            ReadOnlySpan<PointF> pts = p.Points.Span;
            if (pts.Length < 2)
            {
                continue;
            }

            // Number of edges to traverse (closed paths wrap; open paths stop one short).
            int edgeCount = p.IsClosed ? pts.Length : pts.Length - 1;

            // Compute total path length to estimate the number of dash segments.
            // This avoids runaway segmentation when a very short pattern is applied
            // to a very long path.
            float totalLength = 0f;
            for (int j = 0; j < edgeCount; j++)
            {
                int nextIndex = p.IsClosed ? (j + 1) % pts.Length : j + 1;
                totalLength += Vector2.Distance(pts[j], pts[nextIndex]);
            }

            if (totalLength > eps)
            {
                float estimatedSegments = (totalLength / patternLength) * pattern.Length;
                if (estimatedSegments > MaxPatternSegments)
                {
                    return path;
                }
            }

            int ei = 0;
            Vector2 current = pts[0];

            while (ei < edgeCount)
            {
                int nextIndex = p.IsClosed ? (ei + 1) % pts.Length : ei + 1;
                Vector2 next = pts[nextIndex];
                float segLen = Vector2.Distance(current, next);

                // Skip degenerate zero-length segments.
                if (segLen <= eps)
                {
                    current = next;
                    ei++;
                    continue;
                }

                // Accumulate into the current dash span when the segment is shorter
                // than the remaining target length.
                if (segLen + eps < targetLength)
                {
                    if (online)
                    {
                        buffer.Add(current);
                    }

                    current = next;
                    ei++;
                    targetLength -= segLen;
                    continue;
                }

                // Close out a dash span when the segment length matches the target.
                if (MathF.Abs(segLen - targetLength) <= eps)
                {
                    if (online)
                    {
                        buffer.Add(current);
                        buffer.Add(next);
                        FlushBuffer(buffer, segments);
                    }

                    buffer.Clear();
                    online = !online;
                    current = next;
                    ei++;
                    patternPos = (patternPos + 1) % pattern.Length;
                    targetLength = pattern[patternPos] * strokeWidth;
                    continue;
                }

                // Split inside this segment to end the current dash span.
                float t = targetLength / segLen;
                Vector2 split = current + (t * (next - current));

                if (online)
                {
                    buffer.Add(current);
                    buffer.Add(split);
                    FlushBuffer(buffer, segments);
                }

                buffer.Clear();
                online = !online;
                current = split; // continue along the same geometric segment
                patternPos = (patternPos + 1) % pattern.Length;
                targetLength = pattern[patternPos] * strokeWidth;
            }

            // Flush the tail of the last dash span, if any.
            if (buffer.Count > 0)
            {
                if (online)
                {
                    buffer.Add(current);
                    FlushBuffer(buffer, segments);
                }

                buffer.Clear();
            }
        }

        if (segments.Count == 0)
        {
            return path;
        }

        if (segments.Count == 1)
        {
            return segments[0];
        }

        return new ComplexPolygon(segments);
    }

    private static void FlushBuffer(List<PointF> buffer, List<IPath> segments)
    {
        if (buffer.Count >= 2 && buffer[0] != buffer[^1])
        {
            segments.Add(new Path(new LinearLineSegment([.. buffer])));
        }
    }
}
