// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Extensions to <see cref="IPath"/> that allow the generation of outlines.
/// </summary>
public static class OutlinePathExtensions
{
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
    public static IPath GenerateOutline(this IPath path, float width, JointStyle jointStyle, EndCapStyle endCapStyle)
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        List<Polygon> stroked = [];

        PolygonStroker stroker = new() { Width = width, LineJoin = GetLineJoin(jointStyle), LineCap = GetLineCap(endCapStyle) };
        foreach (ISimplePath simplePath in path.Flatten())
        {
            stroked.Add(new Polygon(stroker.ProcessPath(simplePath.Points.Span, simplePath.IsClosed || endCapStyle is EndCapStyle.Polygon or EndCapStyle.Joined).ToArray()));
        }

        return new ComplexPolygon(stroked);
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
    /// <param name="invert">Whether the first item in the pattern is off.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    /// <exception cref="ClipperException">Thrown when an offset cannot be calculated.</exception>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool invert)
        => GenerateOutline(path, width, pattern, invert, DefaultJointStyle, DefaultEndCapStyle);

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
    /// <param name="invert">Whether the first item in the pattern is off.</param>
    /// <param name="jointStyle">The style to apply to the joints.</param>
    /// <param name="endCapStyle">The style to apply to the end caps.</param>
    /// <returns>A new <see cref="IPath"/> representing the outline.</returns>
    /// <exception cref="ClipperException">Thrown when an offset cannot be calculated.</exception>
    public static IPath GenerateOutline(this IPath path, float width, ReadOnlySpan<float> pattern, bool invert, JointStyle jointStyle, EndCapStyle endCapStyle)
    {
        if (width <= 0)
        {
            return Path.Empty;
        }

        if (pattern.Length < 2)
        {
            return path.GenerateOutline(width, jointStyle, endCapStyle);
        }

        PolygonStroker stroker = new() { Width = width, LineJoin = GetLineJoin(jointStyle), LineCap = GetLineCap(endCapStyle) };
        PathsF stroked = [];
        List<PointF> buffer = [];

        foreach (ISimplePath simplePath in path.Flatten())
        {
            bool online = !invert;
            float targetLength = pattern[0] * width;
            int patternPos = 0;
            ReadOnlySpan<PointF> points = simplePath.Points.Span;

            // Create a new list of points representing the new outline
            int pCount = points.Length;
            if (!simplePath.IsClosed)
            {
                pCount--;
            }

            int i = 0;
            Vector2 currentPoint = points[0];

            while (i < pCount)
            {
                int next = (i + 1) % points.Length;
                Vector2 targetPoint = points[next];
                float distanceToNext = Vector2.Distance(currentPoint, targetPoint);
                if (distanceToNext > targetLength)
                {
                    // Find a point between the 2
                    float t = targetLength / distanceToNext;

                    Vector2 point = (currentPoint * (1 - t)) + (targetPoint * t);
                    buffer.Add(currentPoint);
                    buffer.Add(point);

                    // We now insert a line
                    if (online)
                    {
                        stroked.Add(stroker.ProcessPath(CollectionsMarshal.AsSpan(buffer), false));
                    }

                    online = !online;

                    buffer.Clear();

                    currentPoint = point;

                    // Next length
                    patternPos = (patternPos + 1) % pattern.Length;
                    targetLength = pattern[patternPos] * width;
                }
                else if (distanceToNext <= targetLength)
                {
                    buffer.Add(currentPoint);
                    currentPoint = targetPoint;
                    i++;
                    targetLength -= distanceToNext;
                }
            }

            if (buffer.Count > 0)
            {
                if (simplePath.IsClosed)
                {
                    buffer.Add(points[0]);
                }
                else
                {
                    buffer.Add(points[^1]);
                }

                if (online)
                {
                    stroked.Add(stroker.ProcessPath(CollectionsMarshal.AsSpan(buffer), false));
                }

                buffer.Clear();
            }
        }

        // Clean up self intersections.
        PolygonClipper clipper = new() { PreserveCollinear = true };
        clipper.AddSubject(stroked);
        PathsF clipped = [];
        clipper.Execute(ClippingOperation.Union, FillRule.Positive, clipped);

        if (clipped.Count == 0)
        {
            // Cannot clip. Return the stroked path.
            Polygon[] polygons = new Polygon[stroked.Count];
            for (int i = 0; i < stroked.Count; i++)
            {
                polygons[i] = new Polygon(stroked[i].ToArray());
            }

            return new ComplexPolygon(polygons);
        }

        // Convert the clipped paths back to polygons.
        Polygon[] result = new Polygon[clipped.Count];
        for (int i = 0; i < clipped.Count; i++)
        {
            result[i] = new Polygon(clipped[i].ToArray());
        }

        return new ComplexPolygon(result);
    }

    private static LineJoin GetLineJoin(JointStyle value)
        => value switch
        {
            JointStyle.Square => LineJoin.BevelJoin,
            JointStyle.Round => LineJoin.RoundJoin,
            _ => LineJoin.MiterJoin,
        };

    private static LineCap GetLineCap(EndCapStyle value)
        => value switch
        {
            EndCapStyle.Round => LineCap.Round,
            EndCapStyle.Square => LineCap.Square,
            _ => LineCap.Butt,
        };
}
