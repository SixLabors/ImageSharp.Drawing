// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

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
    /// <exception cref="ClipperException">Thrown when an offset cannot be calculated.</exception>
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
    /// <exception cref="ClipperException">Thrown when an offset cannot be calculated.</exception>
#pragma warning disable RCS1163 // Unused parameter
#pragma warning disable IDE0060 // Remove unused parameter
    public static IPath GenerateOutline(this IPath path, float width, JointStyle jointStyle, EndCapStyle endCapStyle)
#pragma warning restore IDE0060 // Remove unused parameter
#pragma warning restore RCS1163 // Unused parameter
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        List<Polygon> polygons = [];
        foreach (ISimplePath simplePath in path.Flatten())
        {
            PolygonStroker stroker = new() { Width = width };
            Polygon polygon = stroker.ProcessPath(simplePath.Points.Span);
            polygons.Add(polygon);
        }

        return new ComplexPolygon(polygons);

        // ClipperOffset offset = new(MiterOffsetDelta);
        // offset.AddPath(path, jointStyle, endCapStyle);

        // return offset.Execute(width);
    }

    /// <summary>
    /// Generates an outline of the path with alternating on and off segments based on the pattern.
    /// </summary>
    /// <param name="path">The path to outline</param>
    /// <param name="width">The outline width.</param>
    /// <param name="pattern">The pattern made of multiples of the width.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    /// <exception cref="ClipperException">Thrown when an offset cannot be calculated.</exception>
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
    /// <exception cref="ClipperException">Thrown when an offset cannot be calculated.</exception>
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
    /// <exception cref="ClipperException">Thrown when an offset cannot be calculated.</exception>
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
    /// <exception cref="ClipperException">Thrown when an offset cannot be calculated.</exception>
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

        IEnumerable<ISimplePath> paths = path.Flatten();

        ClipperOffset offset = new(MiterOffsetDelta);
        List<PointF> buffer = new();
        foreach (ISimplePath p in paths)
        {
            bool online = !startOff;
            float targetLength = pattern[0] * width;
            int patternPos = 0;
            ReadOnlySpan<PointF> points = p.Points.Span;

            // Create a new list of points representing the new outline
            int pCount = points.Length;
            if (!p.IsClosed)
            {
                pCount--;
            }

            int i = 0;
            Vector2 currentPoint = points[0];

            while (i < pCount)
            {
                int next = (i + 1) % points.Length;
                Vector2 targetPoint = points[next];
                float distToNext = Vector2.Distance(currentPoint, targetPoint);
                if (distToNext > targetLength)
                {
                    // find a point between the 2
                    float t = targetLength / distToNext;

                    Vector2 point = (currentPoint * (1 - t)) + (targetPoint * t);
                    buffer.Add(currentPoint);
                    buffer.Add(point);

                    // we now inset a line joining
                    if (online)
                    {
                        offset.AddPath(new ReadOnlySpan<PointF>(buffer.ToArray()), jointStyle, endCapStyle);
                    }

                    online = !online;

                    buffer.Clear();

                    currentPoint = point;

                    // next length
                    patternPos = (patternPos + 1) % pattern.Length;
                    targetLength = pattern[patternPos] * width;
                }
                else if (distToNext <= targetLength)
                {
                    buffer.Add(currentPoint);
                    currentPoint = targetPoint;
                    i++;
                    targetLength -= distToNext;
                }
            }

            if (buffer.Count > 0)
            {
                if (p.IsClosed)
                {
                    buffer.Add(points[0]);
                }
                else
                {
                    buffer.Add(points[points.Length - 1]);
                }

                if (online)
                {
                    offset.AddPath(new ReadOnlySpan<PointF>(buffer.ToArray()), jointStyle, endCapStyle);
                }

                buffer.Clear();
            }
        }

        return offset.Execute(width);
    }
}
