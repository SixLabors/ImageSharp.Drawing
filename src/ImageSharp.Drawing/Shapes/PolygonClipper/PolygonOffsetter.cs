// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Shapes.PolygonClipper
{
    /// <summary>
    /// Contains functions to offset paths (inflate/shrink).
    /// Ported from <see href="https://github.com/AngusJohnson/Clipper2"/> and originally licensed
    /// under <see href="http://www.boost.org/LICENSE_1_0.txt"/>
    /// </summary>
    internal sealed class PolygonOffsetter
    {
        private const float Tolerance = 1.0E-6F;
        private readonly List<Group> groupList = new();
        private readonly PathF normals = new();
        private readonly PathsF solution = new();
        private float groupDelta; // *0.5 for open paths; *-1.0 for negative areas
        private float delta;
        private float absGroupDelta;
        private float mitLimSqr;
        private float stepsPerRad;
        private float stepSin;
        private float stepCos;
        private JointStyle joinType;
        private EndCapStyle endType;

        public PolygonOffsetter(
            float miterLimit = 2F,
            float arcTolerance = 0F,
            bool preserveCollinear = false,
            bool reverseSolution = false)
        {
            this.MiterLimit = miterLimit;
            this.ArcTolerance = arcTolerance;
            this.MergeGroups = true;
            this.PreserveCollinear = preserveCollinear;
            this.ReverseSolution = reverseSolution;
        }

        public float ArcTolerance { get; }

        public bool MergeGroups { get; }

        public float MiterLimit { get; }

        public bool PreserveCollinear { get; }

        public bool ReverseSolution { get; }

        public void AddPath(PathF path, JointStyle joinType, EndCapStyle endType)
        {
            if (path.Count == 0)
            {
                return;
            }

            PathsF pp = new(1) { path };
            this.AddPaths(pp, joinType, endType);
        }

        public void AddPaths(PathsF paths, JointStyle joinType, EndCapStyle endType)
        {
            if (paths.Count == 0)
            {
                return;
            }

            this.groupList.Add(new Group(paths, joinType, endType));
        }

        public void Execute(float delta, PathsF solution)
        {
            solution.Clear();
            this.ExecuteInternal(delta);
            if (this.groupList.Count == 0)
            {
                goto Error;
            }

            // Clean up self-intersections.
            PolygonClipper clipper = new()
            {
                PreserveCollinear = this.PreserveCollinear,

                // The solution should retain the orientation of the input
                ReverseSolution = this.ReverseSolution != this.groupList[0].PathsReversed
            };

            clipper.AddSubject(this.solution);
            if (this.groupList[0].PathsReversed)
            {
                clipper.Execute(ClippingOperation.Union, FillRule.Negative, solution);
            }
            else
            {
                clipper.Execute(ClippingOperation.Union, FillRule.Positive, solution);
            }

            Error:

            // PolygonClipper will throw for unhandled exceptions but we need to explicitly capture an empty result.
            if (solution.Count == 0)
            {
                throw new ClipperException("An error occurred while attempting to clip the polygon. Check input for invalid entries.");
            }
        }

        private void ExecuteInternal(float delta)
        {
            this.solution.Clear();
            if (this.groupList.Count == 0)
            {
                return;
            }

            if (MathF.Abs(delta) < .5F)
            {
                foreach (Group group in this.groupList)
                {
                    foreach (PathF path in group.InPaths)
                    {
                        this.solution.Add(path);
                    }
                }
            }
            else
            {
                this.delta = delta;
                this.mitLimSqr = this.MiterLimit <= 1 ? 2F : 2F / ClipperUtils.Sqr(this.MiterLimit);
                foreach (Group group in this.groupList)
                {
                    this.DoGroupOffset(group);
                }
            }
        }

        private void DoGroupOffset(Group group)
        {
            if (group.EndType == EndCapStyle.Polygon)
            {
                // The lowermost polygon must be an outer polygon. So we can use that as the
                // designated orientation for outer polygons (needed for tidy-up clipping).
                GetBoundsAndLowestPolyIdx(group.InPaths, out int lowestIdx, out BoundsF grpBounds);
                if (lowestIdx < 0)
                {
                    return;
                }

                float area = ClipperUtils.Area(group.InPaths[lowestIdx]);
                group.PathsReversed = area < 0;
                if (group.PathsReversed)
                {
                    this.groupDelta = -this.delta;
                }
                else
                {
                    this.groupDelta = this.delta;
                }
            }
            else
            {
                group.PathsReversed = false;
                this.groupDelta = MathF.Abs(this.delta) * .5F;
            }

            this.absGroupDelta = MathF.Abs(this.groupDelta);
            this.joinType = group.JoinType;
            this.endType = group.EndType;

            // Calculate a sensible number of steps (for 360 deg for the given offset).
            if (group.JoinType == JointStyle.Round || group.EndType == EndCapStyle.Round)
            {
                // arcTol - when fArcTolerance is undefined (0), the amount of
                // curve imprecision that's allowed is based on the size of the
                // offset (delta). Obviously very large offsets will almost always
                // require much less precision. See also offset_triginometry2.svg
                float arcTol = this.ArcTolerance > 0.01F
                    ? this.ArcTolerance
                    : (float)Math.Log10(2 + this.absGroupDelta) * ClipperUtils.DefaultArcTolerance;
                float stepsPer360 = MathF.PI / (float)Math.Acos(1 - (arcTol / this.absGroupDelta));
                this.stepSin = MathF.Sin(2 * MathF.PI / stepsPer360);
                this.stepCos = MathF.Cos(2 * MathF.PI / stepsPer360);

                if (this.groupDelta < 0)
                {
                    this.stepSin = -this.stepSin;
                }

                this.stepsPerRad = stepsPer360 / (2 * MathF.PI);
            }

            bool isJoined = group.EndType is EndCapStyle.Joined or EndCapStyle.Polygon;

            foreach (PathF p in group.InPaths)
            {
                PathF path = ClipperUtils.StripDuplicates(p, isJoined);
                int cnt = path.Count;
                if ((cnt == 0) || ((cnt < 3) && (this.endType == EndCapStyle.Polygon)))
                {
                    continue;
                }

                if (cnt == 1)
                {
                    group.OutPath = new PathF();

                    // Single vertex so build a circle or square.
                    if (group.EndType == EndCapStyle.Round)
                    {
                        float r = this.absGroupDelta;
                        group.OutPath = ClipperUtils.Ellipse(path[0], r, r);
                    }
                    else
                    {
                        Vector2 d = new(MathF.Ceiling(this.groupDelta));
                        Vector2 xy = path[0] - d;
                        BoundsF r = new(xy.X, xy.Y, xy.X, xy.Y);
                        group.OutPath = r.AsPath();
                    }

                    group.OutPaths.Add(group.OutPath);
                }
                else
                {
                    if (cnt == 2 && group.EndType == EndCapStyle.Joined)
                    {
                        if (group.JoinType == JointStyle.Round)
                        {
                            this.endType = EndCapStyle.Round;
                        }
                        else
                        {
                            this.endType = EndCapStyle.Square;
                        }
                    }

                    this.BuildNormals(path);

                    if (this.endType == EndCapStyle.Polygon)
                    {
                        this.OffsetPolygon(group, path);
                    }
                    else if (this.endType == EndCapStyle.Joined)
                    {
                        this.OffsetOpenJoined(group, path);
                    }
                    else
                    {
                        this.OffsetOpenPath(group, path);
                    }
                }
            }

            this.solution.AddRange(group.OutPaths);
            group.OutPaths.Clear();
        }

        private static void GetBoundsAndLowestPolyIdx(PathsF paths, out int index, out BoundsF bounds)
        {
            // TODO: default?
            bounds = new BoundsF(false); // ie invalid rect
            float pX = float.MinValue;
            index = -1;
            for (int i = 0; i < paths.Count; i++)
            {
                foreach (Vector2 pt in paths[i])
                {
                    if (pt.Y >= bounds.Bottom)
                    {
                        if (pt.Y > bounds.Bottom || pt.X < pX)
                        {
                            index = i;
                            pX = pt.X;
                            bounds.Bottom = pt.Y;
                        }
                    }
                    else if (pt.Y < bounds.Top)
                    {
                        bounds.Top = pt.Y;
                    }

                    if (pt.X > bounds.Right)
                    {
                        bounds.Right = pt.X;
                    }
                    else if (pt.X < bounds.Left)
                    {
                        bounds.Left = pt.X;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void BuildNormals(PathF path)
        {
            int cnt = path.Count;
            this.normals.Clear();
            this.normals.Capacity = cnt;

            for (int i = 0; i < cnt - 1; i++)
            {
                this.normals.Add(GetUnitNormal(path[i], path[i + 1]));
            }

            this.normals.Add(GetUnitNormal(path[cnt - 1], path[0]));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OffsetOpenJoined(Group group, PathF path)
        {
            this.OffsetPolygon(group, path);

            // TODO: Just reverse inline?
            path = ClipperUtils.ReversePath(path);
            this.BuildNormals(path);
            this.OffsetPolygon(group, path);
        }

        private void OffsetOpenPath(Group group, PathF path)
        {
            group.OutPath = new(path.Count);
            int highI = path.Count - 1;

            // Further reduced extraneous vertices in solutions (#499)
            if (MathF.Abs(this.groupDelta) < Tolerance)
            {
                group.OutPath.Add(path[0]);
            }
            else
            {
                // do the line start cap
                switch (this.endType)
                {
                    case EndCapStyle.Butt:
                        group.OutPath.Add(path[0] - (this.normals[0] * this.groupDelta));
                        group.OutPath.Add(this.GetPerpendic(path[0], this.normals[0]));
                        break;
                    case EndCapStyle.Round:
                        this.DoRound(group, path, 0, 0, MathF.PI);
                        break;
                    default:
                        this.DoSquare(group, path, 0, 0);
                        break;
                }
            }

            // offset the left side going forward
            for (int i = 1, k = 0; i < highI; i++)
            {
                this.OffsetPoint(group, path, i, ref k);
            }

            // reverse normals ...
            for (int i = highI; i > 0; i--)
            {
                this.normals[i] = Vector2.Negate(this.normals[i - 1]);
            }

            this.normals[0] = this.normals[highI];

            // do the line end cap
            switch (this.endType)
            {
                case EndCapStyle.Butt:
                    group.OutPath.Add(new Vector2(
                        path[highI].X - (this.normals[highI].X * this.groupDelta),
                        path[highI].Y - (this.normals[highI].Y * this.groupDelta)));
                    group.OutPath.Add(this.GetPerpendic(path[highI], this.normals[highI]));
                    break;
                case EndCapStyle.Round:
                    this.DoRound(group, path, highI, highI, MathF.PI);
                    break;
                default:
                    this.DoSquare(group, path, highI, highI);
                    break;
            }

            // offset the left side going back
            for (int i = highI, k = 0; i > 0; i--)
            {
                this.OffsetPoint(group, path, i, ref k);
            }

            group.OutPaths.Add(group.OutPath);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 GetUnitNormal(Vector2 pt1, Vector2 pt2)
        {
            Vector2 dxy = pt2 - pt1;
            if (dxy == Vector2.Zero)
            {
                return default;
            }

            dxy *= 1F / MathF.Sqrt(ClipperUtils.DotProduct(dxy, dxy));
            return new(dxy.Y, -dxy.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void OffsetPolygon(Group group, PathF path)
        {
            // Dereference the current outpath.
            group.OutPath = new(path.Count);
            int cnt = path.Count, prev = cnt - 1;
            for (int i = 0; i < cnt; i++)
            {
                this.OffsetPoint(group, path, i, ref prev);
            }

            group.OutPaths.Add(group.OutPath);
        }

        private void OffsetPoint(Group group, PathF path, int j, ref int k)
        {
            // Further reduced extraneous vertices in solutions (#499)
            if (MathF.Abs(this.groupDelta) < Tolerance)
            {
                group.OutPath.Add(path[j]);
                return;
            }

            // Let A = change in angle where edges join
            // A == 0: ie no change in angle (flat join)
            // A == PI: edges 'spike'
            // sin(A) < 0: right turning
            // cos(A) < 0: change in angle is more than 90 degree
            float sinA = ClipperUtils.CrossProduct(this.normals[j], this.normals[k]);
            float cosA = ClipperUtils.DotProduct(this.normals[j], this.normals[k]);
            if (sinA > 1F)
            {
                sinA = 1F;
            }
            else if (sinA < -1F)
            {
                sinA = -1F;
            }

            // almost straight - less than 1 degree (#424)
            if (cosA > 0.99F)
            {
                this.DoMiter(group, path, j, k, cosA);
            }
            else if (cosA > -0.99F && (sinA * this.groupDelta < 0F))
            {
                // is concave
                group.OutPath.Add(this.GetPerpendic(path[j], this.normals[k]));

                // this extra point is the only (simple) way to ensure that
                // path reversals are fully cleaned with the trailing clipper
                group.OutPath.Add(path[j]); // (#405)
                group.OutPath.Add(this.GetPerpendic(path[j], this.normals[j]));
            }
            else if (this.joinType == JointStyle.Miter)
            {
                // miter unless the angle is so acute the miter would exceeds ML
                if (cosA > this.mitLimSqr - 1)
                {
                    this.DoMiter(group, path, j, k, cosA);
                }
                else
                {
                    this.DoSquare(group, path, j, k);
                }
            }
            else if (this.joinType == JointStyle.Square)
            {
                // angle less than 8 degrees or a squared join
                this.DoSquare(group, path, j, k);
            }
            else
            {
                this.DoRound(group, path, j, k, MathF.Atan2(sinA, cosA));
            }

            k = j;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector2 GetPerpendic(Vector2 pt, Vector2 norm)
            => pt + (norm * this.groupDelta);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoSquare(Group group, PathF path, int j, int k)
        {
            Vector2 vec;
            if (j == k)
            {
                vec = new Vector2(this.normals[0].Y, -this.normals[0].X);
            }
            else
            {
                vec = GetAvgUnitVector(
                  new Vector2(-this.normals[k].Y, this.normals[k].X),
                  new Vector2(this.normals[j].Y, -this.normals[j].X));
            }

            // now offset the original vertex delta units along unit vector
            Vector2 ptQ = path[j];
            ptQ = TranslatePoint(ptQ, this.absGroupDelta * vec.X, this.absGroupDelta * vec.Y);

            // get perpendicular vertices
            Vector2 pt1 = TranslatePoint(ptQ, this.groupDelta * vec.Y, this.groupDelta * -vec.X);
            Vector2 pt2 = TranslatePoint(ptQ, this.groupDelta * -vec.Y, this.groupDelta * vec.X);

            // get 2 vertices along one edge offset
            Vector2 pt3 = this.GetPerpendic(path[k], this.normals[k]);

            if (j == k)
            {
                Vector2 pt4 = pt3 + (vec * this.groupDelta);
                Vector2 pt = IntersectPoint(pt1, pt2, pt3, pt4);

                // get the second intersect point through reflecion
                group.OutPath.Add(ReflectPoint(pt, ptQ));
                group.OutPath.Add(pt);
            }
            else
            {
                Vector2 pt4 = this.GetPerpendic(path[j], this.normals[k]);
                Vector2 pt = IntersectPoint(pt1, pt2, pt3, pt4);

                group.OutPath.Add(pt);

                // Get the second intersect point through reflecion
                group.OutPath.Add(ReflectPoint(pt, ptQ));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DoMiter(Group group, PathF path, int j, int k, float cosA)
        {
            float q = this.groupDelta / (cosA + 1);
            Vector2 pv = path[j];
            Vector2 nk = this.normals[k];
            Vector2 nj = this.normals[j];
            group.OutPath.Add(pv + ((nk + nj) * q));
        }

        private void DoRound(Group group, PathF path, int j, int k, float angle)
        {
            Vector2 pt = path[j];
            Vector2 offsetVec = this.normals[k] * new Vector2(this.groupDelta);
            if (j == k)
            {
                offsetVec = Vector2.Negate(offsetVec);
            }

            group.OutPath.Add(pt + offsetVec);

            // avoid 180deg concave
            if (angle > -MathF.PI + .01F)
            {
                int steps = Math.Max(2, (int)Math.Ceiling(this.stepsPerRad * MathF.Abs(angle)));

                // ie 1 less than steps
                for (int i = 1; i < steps; i++)
                {
                    offsetVec = new((offsetVec.X * this.stepCos) - (this.stepSin * offsetVec.Y), (offsetVec.X * this.stepSin) + (offsetVec.Y * this.stepCos));

                    group.OutPath.Add(pt + offsetVec);
                }
            }

            group.OutPath.Add(this.GetPerpendic(pt, this.normals[j]));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 TranslatePoint(Vector2 pt, float dx, float dy)
            => pt + new Vector2(dx, dy);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 ReflectPoint(Vector2 pt, Vector2 pivot)
            => pivot + (pivot - pt);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 IntersectPoint(Vector2 pt1a, Vector2 pt1b, Vector2 pt2a, Vector2 pt2b)
        {
            // vertical
            if (ClipperUtils.IsAlmostZero(pt1a.X - pt1b.X))
            {
                if (ClipperUtils.IsAlmostZero(pt2a.X - pt2b.X))
                {
                    return default;
                }

                float m2 = (pt2b.Y - pt2a.Y) / (pt2b.X - pt2a.X);
                float b2 = pt2a.Y - (m2 * pt2a.X);
                return new Vector2(pt1a.X, (m2 * pt1a.X) + b2);
            }

            // vertical
            if (ClipperUtils.IsAlmostZero(pt2a.X - pt2b.X))
            {
                float m1 = (pt1b.Y - pt1a.Y) / (pt1b.X - pt1a.X);
                float b1 = pt1a.Y - (m1 * pt1a.X);
                return new Vector2(pt2a.X, (m1 * pt2a.X) + b1);
            }
            else
            {
                float m1 = (pt1b.Y - pt1a.Y) / (pt1b.X - pt1a.X);
                float b1 = pt1a.Y - (m1 * pt1a.X);
                float m2 = (pt2b.Y - pt2a.Y) / (pt2b.X - pt2a.X);
                float b2 = pt2a.Y - (m2 * pt2a.X);
                if (ClipperUtils.IsAlmostZero(m1 - m2))
                {
                    return default;
                }

                float x = (b2 - b1) / (m1 - m2);
                return new Vector2(x, (m1 * x) + b1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 GetAvgUnitVector(Vector2 vec1, Vector2 vec2)
            => NormalizeVector(vec1 + vec2);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Hypotenuse(Vector2 vector)
            => MathF.Sqrt(Vector2.Dot(vector, vector));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 NormalizeVector(Vector2 vector)
        {
            float h = Hypotenuse(vector);
            if (ClipperUtils.IsAlmostZero(h))
            {
                return default;
            }

            float inverseHypot = 1 / h;
            return vector * inverseHypot;
        }

        private class Group
        {
            public Group(PathsF paths, JointStyle joinType, EndCapStyle endType = EndCapStyle.Polygon)
            {
                this.InPaths = new PathsF(paths);
                this.JoinType = joinType;
                this.EndType = endType;
                this.OutPath = new PathF();
                this.OutPaths = new PathsF();
                this.PathsReversed = false;
            }

            public PathF OutPath { get; set; }

            public PathsF OutPaths { get; }

            public JointStyle JoinType { get; }

            public EndCapStyle EndType { get; set; }

            public bool PathsReversed { get; set; }

            public PathsF InPaths { get; }
        }
    }

    internal class PathsF : List<PathF>
    {
        public PathsF()
        {
        }

        public PathsF(IEnumerable<PathF> items)
            : base(items)
        {
        }

        public PathsF(int capacity)
            : base(capacity)
        {
        }
    }

    internal class PathF : List<Vector2>
    {
        public PathF()
        {
        }

        public PathF(IEnumerable<Vector2> items)
            : base(items)
        {
        }

        public PathF(int capacity)
            : base(capacity)
        {
        }
    }
}
