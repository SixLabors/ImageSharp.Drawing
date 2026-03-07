// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Splits a path into dash segments without performing stroke expansion.
/// Each "on" dash segment is returned as an open sub-path.
/// </summary>
internal static class DashPathSplitter
{
    /// <summary>
    /// Splits the given path into dash segments based on the provided pattern.
    /// Returns a composite path containing only the "on" segments as open sub-paths.
    /// </summary>
    /// <param name="path">The centerline path to split.</param>
    /// <param name="strokeWidth">The stroke width (pattern elements are multiples of this).</param>
    /// <param name="pattern">The dash pattern. Each element is a multiple of <paramref name="strokeWidth"/>.</param>
    /// <returns>A path containing the "on" dash segments.</returns>
    public static IPath SplitDashes(IPath path, float strokeWidth, ReadOnlySpan<float> pattern)
    {
        if (pattern.Length < 2)
        {
            return path;
        }

        const float eps = 1e-6f;

        float patternLength = 0f;
        for (int i = 0; i < pattern.Length; i++)
        {
            patternLength += MathF.Abs(pattern[i]) * strokeWidth;
        }

        if (patternLength <= eps)
        {
            return path;
        }

        IEnumerable<ISimplePath> simplePaths = path.Flatten();
        List<IPath> segments = [];
        List<PointF> buffer = new(64);

        foreach (ISimplePath p in simplePaths)
        {
            bool online = true;
            int patternPos = 0;
            float targetLength = pattern[patternPos] * strokeWidth;

            ReadOnlySpan<PointF> pts = p.Points.Span;
            if (pts.Length < 2)
            {
                continue;
            }

            int edgeCount = p.IsClosed ? pts.Length : pts.Length - 1;
            int ei = 0;
            Vector2 current = pts[0];

            while (ei < edgeCount)
            {
                int nextIndex = p.IsClosed ? (ei + 1) % pts.Length : ei + 1;
                Vector2 next = pts[nextIndex];
                float segLen = Vector2.Distance(current, next);

                if (segLen <= eps)
                {
                    current = next;
                    ei++;
                    continue;
                }

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
                current = split;
                patternPos = (patternPos + 1) % pattern.Length;
                targetLength = pattern[patternPos] * strokeWidth;
            }

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
            return Path.Empty;
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
