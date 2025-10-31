// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonGeometry;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Extensions to <see cref="IPath"/> that allow the generation of outlines.
/// </summary>
public static class OutlinePathExtensions
{
    private const float MiterOffsetDelta = 20;
    private const JointStyle DefaultJointStyle = JointStyle.Square;
    private const EndCapStyle DefaultEndCapStyle = EndCapStyle.Butt;

    /// <summary>
    /// Generates an outline of the path.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width)
        => GenerateOutline(path, width, DefaultJointStyle, DefaultEndCapStyle);

    /// <summary>
    /// Generates an outline of the path.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="jointStyle">The style to apply to the joints.</param>
    /// <param name="endCapStyle">The style to apply to the end caps.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, JointStyle jointStyle, EndCapStyle endCapStyle)
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        StrokedShapeGenerator generator = new(MiterOffsetDelta);
        return new ComplexPolygon(generator.GenerateStrokedShapes(path, width));
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
    /// <param name="startOff">Whether the first item in the pattern is on or off.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool startOff)
        => GenerateOutline(path, width, pattern, startOff, DefaultJointStyle, DefaultEndCapStyle);

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <param name="jointStyle">The style to apply to the joints.</param>
    /// <param name="endCapStyle">The style to apply to the end caps.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, JointStyle jointStyle, EndCapStyle endCapStyle)
        => GenerateOutline(path, width, pattern, false, jointStyle, endCapStyle);

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <param name="startOff">Whether the first item in the pattern is on or off.</param>
    /// <param name="jointStyle">The style to apply to the joints.</param>
    /// <param name="endCapStyle">The style to apply to the end caps.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool startOff, JointStyle jointStyle, EndCapStyle endCapStyle)
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        if (pattern.Length < 2)
        {
            return path.GenerateOutline(width, jointStyle, endCapStyle);
        }

        const float eps = 1e-6f;

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

            int i = 0;
            Vector2 current = pts[0];

            while (i < edgeCount)
            {
                int nextIndex = p.IsClosed ? (i + 1) % pts.Length : i + 1;
                Vector2 next = pts[nextIndex];
                float segLen = Vector2.Distance(current, next);

                if (segLen <= eps)
                {
                    current = next;
                    i++;
                    continue;
                }

                if (segLen + eps < targetLength)
                {
                    buffer.Add(current);
                    current = next;
                    i++;
                    targetLength -= segLen;
                    continue;
                }

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

                // split inside this segment
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
        StrokedShapeGenerator generator = new(MiterOffsetDelta);
        return new ComplexPolygon(generator.GenerateStrokedShapes(outlines, width));
    }
}
