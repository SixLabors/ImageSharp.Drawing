// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.PolygonGeometry;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Extensions to <see cref="IPath"/> that allow the generation of outlines.
/// </summary>
public static class OutlinePathExtensions
{
    private static readonly StrokeOptions DefaultOptions = new();

    /// <summary>
    /// Generates an outline of the path.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width)
        => GenerateOutline(path, width, DefaultOptions);

    /// <summary>
    /// Generates an outline of the path.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="strokeOptions">The stroke geometry options.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, StrokeOptions strokeOptions)
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        return StrokedShapeGenerator.GenerateStrokedShapes(path, width, strokeOptions);
    }

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern)
        => path.GenerateOutline(width, pattern, false);

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <param name="strokeOptions">The stroke geometry options.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, StrokeOptions strokeOptions)
        => GenerateOutline(path, width, pattern, false, strokeOptions);

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <param name="startOff">Whether the first item in the pattern is on or off.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool startOff)
        => GenerateOutline(path, width, pattern, startOff, DefaultOptions);

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <param name="startOff">Whether the first item in the pattern is on or off.</param>
    /// <param name="strokeOptions">The stroke geometry options.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(
        this IPath path,
        float width,
        ReadOnlySpan<float> pattern,
        bool startOff,
        StrokeOptions strokeOptions)
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        if (pattern.Length < 2)
        {
            return path.GenerateOutline(width, strokeOptions);
        }

        const float eps = 1e-6f;
        const int maxPatternSegments = 10000;

        // Compute the absolute pattern length in path units to detect degenerate patterns.
        float patternLength = 0f;
        for (int i = 0; i < pattern.Length; i++)
        {
            patternLength += MathF.Abs(pattern[i]) * width;
        }

        // Fallback to a solid outline when the dash pattern is too small to be meaningful.
        if (patternLength <= eps)
        {
            return path.GenerateOutline(width, strokeOptions);
        }

        IEnumerable<ISimplePath> paths = path.Flatten();

        List<PointF[]> outlines = [];
        List<PointF> buffer = new(64); // arbitrary initial capacity hint.

        foreach (ISimplePath p in paths)
        {
            bool online = !startOff;
            int patternPos = 0;
            float targetLength = pattern[patternPos] * width;

            ReadOnlySpan<PointF> pts = p.Points.Span;
            if (pts.Length < 2)
            {
                continue;
            }

            // number of edges to traverse (no wrap for open paths)
            int edgeCount = p.IsClosed ? pts.Length : pts.Length - 1;
            float totalLength = 0f;

            // Compute total path length to estimate the number of dash segments to produce.
            for (int j = 0; j < edgeCount; j++)
            {
                int nextIndex = p.IsClosed ? (j + 1) % pts.Length : j + 1;
                totalLength += Vector2.Distance(pts[j], pts[nextIndex]);
            }

            if (totalLength > eps)
            {
                // Avoid runaway segmentation by falling back when the dash count explodes.
                float estimatedSegments = (totalLength / patternLength) * pattern.Length;
                if (estimatedSegments > maxPatternSegments)
                {
                    return path.GenerateOutline(width, strokeOptions);
                }
            }

            int i = 0;
            Vector2 current = pts[0];

            while (i < edgeCount)
            {
                int nextIndex = p.IsClosed ? (i + 1) % pts.Length : i + 1;
                Vector2 next = pts[nextIndex];
                float segLen = Vector2.Distance(current, next);

                // Skip degenerate segments.
                if (segLen <= eps)
                {
                    current = next;
                    i++;
                    continue;
                }

                // Accumulate into the current dash span when the segment is shorter than the target.
                if (segLen + eps < targetLength)
                {
                    buffer.Add(current);
                    current = next;
                    i++;
                    targetLength -= segLen;
                    continue;
                }

                // Close out a dash span when the segment length matches the target length.
                if (MathF.Abs(segLen - targetLength) <= eps)
                {
                    buffer.Add(current);
                    buffer.Add(next);

                    if (online && buffer.Count >= 2 && buffer[0] != buffer[^1])
                    {
                        outlines.Add([.. buffer]);
                    }

                    buffer.Clear();
                    online = !online;

                    current = next;
                    i++;
                    patternPos = (patternPos + 1) % pattern.Length;
                    targetLength = pattern[patternPos] * width;
                    continue;
                }

                // Split inside this segment to end the current dash span.
                float t = targetLength / segLen; // 0 < t < 1 here
                Vector2 split = current + (t * (next - current));

                buffer.Add(current);
                buffer.Add(split);

                if (online && buffer.Count >= 2 && buffer[0] != buffer[^1])
                {
                    outlines.Add([.. buffer]);
                }

                buffer.Clear();
                online = !online;

                current = split; // continue along the same geometric segment

                patternPos = (patternPos + 1) % pattern.Length;
                targetLength = pattern[patternPos] * width;
            }

            // flush tail of the last dash span, if any
            if (buffer.Count > 0)
            {
                buffer.Add(current); // terminate at the true end position

                if (online && buffer.Count >= 2 && buffer[0] != buffer[^1])
                {
                    outlines.Add([.. buffer]);
                }

                buffer.Clear();
            }
        }

        // Each outline span is stroked as an open polyline; the union cleans overlaps.
        return StrokedShapeGenerator.GenerateStrokedShapes(outlines, width, strokeOptions);
    }
}
