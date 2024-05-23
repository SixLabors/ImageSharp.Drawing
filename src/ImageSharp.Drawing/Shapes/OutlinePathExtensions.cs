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
    private const float MiterOffsetDelta = 20;
    private const JointStyle DefaultJointStyle = JointStyle.Square;
    private const EndCapStyle DefaultEndCapStyle = EndCapStyle.Butt;

    /// <summary>
    /// Calculates the scaling matrixes tha tmust be applied to the inout and output paths of for successful clipping.
    /// </summary>
    /// <param name="width">the requested width</param>
    /// <param name="scaleUpMartrix">The matrix to apply to the input path</param>
    /// <param name="scaleDownMartrix">The matrix to apply to the output path</param>
    /// <returns>The final width to use internally to outlining</returns>
    private static float CalculateScalingMatrix(float width, out Matrix3x2 scaleUpMartrix, out Matrix3x2 scaleDownMartrix)
    {
        // when the thickness is below a 0.5 threshold we need to scale
        // the source path (up) and result path (down) by a factor to ensure
        // the offest is greater than 0.5 to ensure offsetting isn't skipped.
        scaleUpMartrix = Matrix3x2.Identity;
        scaleDownMartrix = Matrix3x2.Identity;
        if (width < 0.5)
        {
            float scale = 1 / width;
            scaleUpMartrix = Matrix3x2.CreateScale(scale);
            scaleDownMartrix = Matrix3x2.CreateScale(width);
            width = 1;
        }

        return width;
    }

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

        width = CalculateScalingMatrix(width, out Matrix3x2 scaleUpMartrix, out Matrix3x2 scaleDownMartrix);

        ClipperOffset offset = new(MiterOffsetDelta);

        // transform is noop for Matrix3x2.Identity
        offset.AddPath(path.Transform(scaleUpMartrix), jointStyle, endCapStyle);

        return offset.Execute(width).Transform(scaleDownMartrix);
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

        width = CalculateScalingMatrix(width, out Matrix3x2 scaleUpMartrix, out Matrix3x2 scaleDownMartrix);

        IEnumerable<ISimplePath> paths = path.Transform(scaleUpMartrix).Flatten();

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
                        offset.AddPath(CollectionsMarshal.AsSpan(buffer), jointStyle, endCapStyle);
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
                    buffer.Add(points[^1]);
                }

                if (online)
                {
                    offset.AddPath(CollectionsMarshal.AsSpan(buffer), jointStyle, endCapStyle);
                }

                buffer.Clear();
            }
        }

        return offset.Execute(width).Transform(scaleDownMartrix);
    }
}
