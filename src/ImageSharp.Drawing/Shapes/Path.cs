// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// A aggregate of <see cref="ILineSegment"/>s making a single logical path.
    /// </summary>
    /// <seealso cref="IPath" />
    public class Path : IPath, ISimplePath, IPathInternals, IInternalPathOwner
    {
        private readonly ILineSegment[] lineSegments;
        private InternalPath innerPath;

        /// <summary>
        /// Initializes a new instance of the <see cref="Path"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Path(IEnumerable<ILineSegment> segments)
            : this(segments?.ToArray())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Path" /> class.
        /// </summary>
        /// <param name="path">The path.</param>
        public Path(Path path)
            : this(path.LineSegments)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Path"/> class.
        /// </summary>
        /// <param name="segments">The segments.</param>
        public Path(params ILineSegment[] segments)
            => this.lineSegments = segments ?? throw new ArgumentNullException(nameof(segments));

        /// <inheritdoc/>
        bool ISimplePath.IsClosed => this.IsClosed;

        /// <inheritdoc cref="ISimplePath.IsClosed"/>
        public virtual bool IsClosed => false;

        /// <inheritdoc/>
        public ReadOnlyMemory<PointF> Points => this.InnerPath.Points();

        /// <inheritdoc />
        public RectangleF Bounds => this.InnerPath.Bounds;

        /// <inheritdoc />
        public PathTypes PathType => this.IsClosed ? PathTypes.Open : PathTypes.Closed;

        /// <summary>
        /// Gets the maximum number intersections that a shape can have when testing a line.
        /// </summary>
        internal int MaxIntersections => this.InnerPath.PointCount;

        /// <summary>
        /// Gets readonly collection of line segments.
        /// </summary>
        public IReadOnlyList<ILineSegment> LineSegments => this.lineSegments;

        /// <summary>
        /// Gets or sets a value indicating whether close or collinear vertices should be removed. TEST ONLY!
        /// </summary>
        internal bool RemoveCloseAndCollinearPoints { get; set; } = true;

        private InternalPath InnerPath =>
            this.innerPath ??= new InternalPath(this.lineSegments, this.IsClosed, this.RemoveCloseAndCollinearPoints);

        /// <inheritdoc />
        public virtual IPath Transform(Matrix3x2 matrix)
        {
            if (matrix.IsIdentity)
            {
                return this;
            }

            var segments = new ILineSegment[this.lineSegments.Length];

            for (int i = 0; i < this.LineSegments.Count; i++)
            {
                segments[i] = this.lineSegments[i].Transform(matrix);
            }

            return new Path(segments);
        }

        /// <inheritdoc />
        public IPath AsClosedPath()
        {
            if (this.IsClosed)
            {
                return this;
            }

            return new Polygon(this.LineSegments);
        }

        /// <inheritdoc />
        public IEnumerable<ISimplePath> Flatten()
        {
            yield return this;
        }

        /// <inheritdoc/>
        SegmentInfo IPathInternals.PointAlongPath(float distance)
           => this.InnerPath.PointAlongPath(distance);

        /// <inheritdoc/>
        IReadOnlyList<InternalPath> IInternalPathOwner.GetRingsAsInternalPath() => new[] { this.InnerPath };

        /// <summary>
        /// Converts an svg path into a Path
        /// </summary>
        /// <param name="data">data</param>
        /// <param name="value">path</param>
        /// <returns>true if successful</returns>
        public static bool TryParseSvgPath(string data, out IPath value)
            => TryParseSvgPath(data.AsSpan(), out value);

        /// <summary>
        /// Converts an svg path into a Path
        /// </summary>
        /// <param name="data">data</param>
        /// <param name="value">path</param>
        /// <returns>true if successful</returns>
        public static bool TryParseSvgPath(ReadOnlySpan<char> data, out IPath value)
        {
            value = null;

            var builder = new PathBuilder();

            PointF first = PointF.Empty;
            PointF c = PointF.Empty;
            PointF lastc = PointF.Empty;
            PointF point1;
            PointF point2;
            PointF point3;

            char op = '\0';
            char previousOp = '\0';
            bool relative = false;
            while (true)
            {
                data = data.TrimStart();
                if (data.Length == 0)
                {
                    break;
                }

                char ch = data[0];
                if (char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.')
                {
                    // Are we are the end of the string or we are at the end of the path?
                    if (data.Length == 0 || op == 'Z')
                    {
                        return false;
                    }
                }
                else if (IsSeparator(ch))
                {
                    data = TrimSeparator(data);
                }
                else
                {
                    op = ch;
                    relative = false;
                    if (char.IsLower(op))
                    {
                        op = char.ToUpper(op);
                        relative = true;
                    }

                    data = TrimSeparator(data.Slice(1));
                }

                switch (op)
                {
                    case 'M':
                        data = FindPoint(data, out point1, relative, c);
                        builder.MoveTo(point1);
                        previousOp = '\0';
                        op = 'L';
                        c = point1;
                        break;
                    case 'L':
                        data = FindPoint(data, out point1, relative, c);
                        builder.LineTo(point1);
                        c = point1;
                        break;
                    case 'H':
                        data = FindScaler(data, out float x);
                        if (relative)
                        {
                            x += c.X;
                        }

                        builder.LineTo(x, c.Y);
                        c.X = x;
                        break;
                    case 'V':
                        data = FindScaler(data, out float y);
                        if (relative)
                        {
                            y += c.Y;
                        }

                        builder.LineTo(c.X, y);
                        c.Y = y;
                        break;
                    case 'C':
                        data = FindPoint(data, out point1, relative, c);
                        data = FindPoint(data, out point2, relative, c);
                        data = FindPoint(data, out point3, relative, c);
                        builder.CubicBezierTo(point1, point2, point3);
                        lastc = point2;
                        c = point3;
                        break;
                    case 'S':
                        data = FindPoint(data, out point2, relative, c);
                        data = FindPoint(data, out point3, relative, c);
                        point1 = c;
                        if (previousOp is 'C' or 'S')
                        {
                            point1.X -= lastc.X - c.X;
                            point1.Y -= lastc.Y - c.Y;
                        }

                        builder.CubicBezierTo(point1, point2, point3);
                        lastc = point2;
                        c = point3;
                        break;
                    case 'Q': // Quadratic Bezier Curve
                        data = FindPoint(data, out point1, relative, c);
                        data = FindPoint(data, out point2, relative, c);
                        builder.QuadraticBezierTo(point1, point2);
                        lastc = point1;
                        c = point2;
                        break;
                    case 'T':
                        data = FindPoint(data, out point2, relative, c);
                        point1 = c;
                        if (previousOp is 'Q' or 'T')
                        {
                            point1.X -= lastc.X - c.X;
                            point1.Y -= lastc.Y - c.Y;
                        }

                        builder.QuadraticBezierTo(point1, point2);
                        lastc = point1;
                        c = point2;
                        break;
                    case 'A':
                        data = FindScaler(data, out float radiiX);
                        data = TrimSeparator(data);
                        data = FindScaler(data, out float radiiY);
                        data = TrimSeparator(data);
                        data = FindScaler(data, out float angle);
                        data = TrimSeparator(data);
                        data = FindScaler(data, out float largeArc);
                        data = TrimSeparator(data);
                        data = FindScaler(data, out float sweep);

                        data = FindPoint(data, out PointF point, relative, c);
                        if (data.Length > 0)
                        {
                            builder.ArcTo(radiiX, radiiY, angle, largeArc == 1, sweep == 1, point);
                            c = point;
                        }

                        break;
                    case 'Z':
                        builder.CloseFigure();
                        c = first;
                        break;
                    case '~':
                        data = FindPoint(data, out point1, relative, c);
                        data = FindPoint(data, out point2, relative, c);
                        builder.MoveTo(point1);
                        builder.LineTo(point2);
                        break;
                    default:
                        return false;
                }

                if (previousOp == 0)
                {
                    first = c;
                }

                previousOp = op;
            }

            value = builder.Build();
            return true;

            static bool IsSeparator(char ch)
                => char.IsWhiteSpace(ch) || ch == ',';

            static ReadOnlySpan<char> TrimSeparator(ReadOnlySpan<char> data)
            {
                if (data.Length == 0)
                {
                    return data;
                }

                int idx = 0;
                for (; idx < data.Length; idx++)
                {
                    if (!IsSeparator(data[idx]))
                    {
                        break;
                    }
                }

                return data.Slice(idx);
            }

            static ReadOnlySpan<char> FindPoint(ReadOnlySpan<char> str, out PointF value, bool isRelative, PointF relative)
            {
                str = FindScaler(str, out float x);
                str = FindScaler(str, out float y);
                if (isRelative)
                {
                    x += relative.X;
                    y += relative.Y;
                }

                value = new PointF(x, y);
                return str;
            }

            static ReadOnlySpan<char> FindScaler(ReadOnlySpan<char> str, out float scaler)
            {
                str = TrimSeparator(str);
                scaler = 0;

                for (int i = 0; i < str.Length; i++)
                {
                    if (IsSeparator(str[i]) || i == str.Length)
                    {
                        scaler = ParseFloat(str.Slice(0, i));
                        return str.Slice(i);
                    }
                }

                if (str.Length > 0)
                {
                    scaler = ParseFloat(str);
                }

                return ReadOnlySpan<char>.Empty;
            }

#if !NETCOREAPP2_1_OR_GREATER
            static unsafe float ParseFloat(ReadOnlySpan<char> str)
            {
                fixed (char* p = str)
                {
                    return float.Parse(new string(p, 0, str.Length));
                }
            }
#else
            static float ParseFloat(ReadOnlySpan<char> str)
                => float.Parse(str);
#endif
        }
    }
}
