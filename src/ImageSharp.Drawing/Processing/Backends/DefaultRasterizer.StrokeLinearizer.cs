// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal static partial class DefaultRasterizer
{
    /// <summary>
    /// Base retained stroke linearizer that expands stroked centerlines once into row-local line storage.
    /// </summary>
    /// <typeparam name="TL">The mutable per-row retained line collector type.</typeparam>
    private abstract class StrokeLinearizer<TL> : Linearizer<TL>
        where TL : class
    {
        private const float StrokeMicroSegmentEpsilon = 1F / 64F;

        private readonly StrokeStyle stroke;

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeLinearizer{TL}"/> class.
        /// </summary>
        /// <param name="geometry">The stroked centerline geometry.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="minX">The minimum destination X bound after clipping.</param>
        /// <param name="minY">The minimum destination Y bound after clipping.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index.</param>
        /// <param name="rowBandCount">The retained row-band count.</param>
        /// <param name="samplingOffsetX">The horizontal sampling offset.</param>
        /// <param name="samplingOffsetY">The vertical sampling offset.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        protected StrokeLinearizer(
            LinearGeometry geometry,
            Matrix4x4 residual,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY,
            MemoryAllocator allocator)
            : base(geometry, residual, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, samplingOffsetX, samplingOffsetY, allocator)
            => this.stroke = stroke;

        private enum ContourInterest
        {
            Outside,
            Clipped,
            Contained
        }

        /// <inheritdoc />
        protected override bool ProcessCore()
        {
            ReadOnlySpan<LinearContour> contours = this.Geometry.GetContours();
            for (int contourIndex = 0; contourIndex < contours.Length; contourIndex++)
            {
                LinearContour contour = contours[contourIndex];
                if (contour.PointCount == 0)
                {
                    continue;
                }

                ReadOnlySpan<PointF> contourPoints = this.Geometry.GetContourPoints(contour);
                ContourInterest contourInterest = this.GetContourInterest(contourPoints);
                if (contourInterest == ContourInterest.Outside)
                {
                    continue;
                }

                bool isClosed = this.IsContourClosedForEmission(contourPoints, contour.IsClosed);

                this.ProcessContour(contourPoints, isClosed, contourInterest == ContourInterest.Contained);
            }

            if (!this.HasAnyCoverage)
            {
                return false;
            }

            this.FinalizeLines();
            return true;
        }

        /// <summary>
        /// Classifies one stroked contour against the interest bounds.
        /// </summary>
        /// <param name="contourPoints">The contour points.</param>
        /// <returns>The contour's relationship to the interest bounds.</returns>
        private ContourInterest GetContourInterest(ReadOnlySpan<PointF> contourPoints)
        {
            RectangleF translatedBounds = InflateStrokeBounds(this.GetPointBounds(contourPoints), this.stroke);
            translatedBounds.Offset(this.TranslateX + this.SamplingOffsetX - this.MinX, this.TranslateY + this.SamplingOffsetY - this.MinY);

            if (translatedBounds.Right <= 0F ||
                translatedBounds.Bottom <= 0F ||
                translatedBounds.Left >= this.Width ||
                translatedBounds.Top >= this.Height)
            {
                return ContourInterest.Outside;
            }

            if (translatedBounds.Left >= 0F &&
                translatedBounds.Top >= 0F &&
                translatedBounds.Right <= this.Width &&
                translatedBounds.Bottom <= this.Height)
            {
                return ContourInterest.Contained;
            }

            return ContourInterest.Clipped;
        }

        /// <summary>
        /// Returns whether a contour should be treated as closed when emitting stroke geometry.
        /// </summary>
        /// <param name="contourPoints">The contour points.</param>
        /// <param name="isDeclaredClosed">Indicates whether the contour is explicitly closed.</param>
        /// <returns><see langword="true"/> when the contour should be stroked as closed; otherwise <see langword="false"/>.</returns>
        private bool IsContourClosedForEmission(ReadOnlySpan<PointF> contourPoints, bool isDeclaredClosed)
        {
            if (contourPoints.Length < 3)
            {
                return false;
            }

            PointF first = this.TransformPoint(contourPoints[0]);
            PointF last = this.TransformPoint(contourPoints[^1]);

            if (isDeclaredClosed || first == last)
            {
                return true;
            }

            Vector2 delta = first - last;
            float closeThreshold = MathF.Max(this.stroke.Width, 1E-3F);
            return delta.LengthSquared() <= closeThreshold * closeThreshold;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private PointF TransformPoint(PointF point)
            => this.HasResidual ? PointF.Transform(point, this.Residual) : point;

        /// <summary>
        /// Processes one centerline contour.
        /// </summary>
        /// <param name="contourPoints">The contiguous contour points.</param>
        /// <param name="isClosed">Indicates whether the contour is closed.</param>
        /// <param name="contained">Indicates whether the stroked contour is fully contained within the interest.</param>
        private void ProcessContour(ReadOnlySpan<PointF> contourPoints, bool isClosed, bool contained)
        {
            StrokeContourSegment[] rentedSegments = ArrayPool<StrokeContourSegment>.Shared.Rent(contourPoints.Length);
            try
            {
                int segmentCount = this.BuildContourSegments(
                    contourPoints,
                    isClosed,
                    rentedSegments,
                    out int distinctPointCount,
                    out PointF pointLike);

                if (segmentCount == 0)
                {
                    this.EmitPointStrokeContour(pointLike, contained);
                    return;
                }

                if (segmentCount == 1 || distinctPointCount == 2)
                {
                    StrokeContourSegment segment = rentedSegments[0];
                    this.EmitOpenSegmentStrokeContour(segment.Start, segment.End, contained);
                    return;
                }

                if (isClosed)
                {
                    this.EmitClosedStrokeContour(rentedSegments.AsSpan(0, segmentCount), contained);
                    return;
                }

                this.EmitOpenStrokeContour(rentedSegments.AsSpan(0, segmentCount), contained);
            }
            finally
            {
                ArrayPool<StrokeContourSegment>.Shared.Return(rentedSegments);
            }
        }

        /// <summary>
        /// Builds one contour-local stroke segment array while collapsing immediate duplicate points.
        /// </summary>
        /// <param name="contourPoints">The contiguous contour points.</param>
        /// <param name="isClosed">Indicates whether the contour is closed.</param>
        /// <param name="segments">The destination segment buffer.</param>
        /// <param name="distinctPointCount">Receives the number of distinct contour points.</param>
        /// <param name="pointLike">Receives the fallback point for degenerate contours.</param>
        /// <returns>The number of emitted contour segments.</returns>
        private int BuildContourSegments(
            ReadOnlySpan<PointF> contourPoints,
            bool isClosed,
            Span<StrokeContourSegment> segments,
            out int distinctPointCount,
            out PointF pointLike)
        {
            pointLike = default;
            distinctPointCount = 0;
            if (contourPoints.IsEmpty)
            {
                return 0;
            }

            Matrix4x4 residual = this.Residual;
            bool hasResidual = this.HasResidual;
            PointF firstPoint = hasResidual ? PointF.Transform(contourPoints[0], residual) : contourPoints[0];
            PointF previousPoint = firstPoint;
            pointLike = firstPoint;
            distinctPointCount = 1;
            int segmentCount = 0;

            for (int i = 1; i < contourPoints.Length; i++)
            {
                PointF point = hasResidual ? PointF.Transform(contourPoints[i], residual) : contourPoints[i];
                if (point == previousPoint)
                {
                    continue;
                }

                if (TryCreateStrokeContourSegment(previousPoint, point, out StrokeContourSegment segment))
                {
                    distinctPointCount++;
                    segments[segmentCount++] = segment;
                    previousPoint = point;
                }

                pointLike = point;
            }

            if (isClosed &&
                distinctPointCount > 1 &&
                previousPoint == firstPoint)
            {
                distinctPointCount--;
            }

            if (isClosed &&
                segmentCount > 1 &&
                previousPoint != firstPoint &&
                TryCreateStrokeContourSegment(previousPoint, firstPoint, out StrokeContourSegment closingSegment))
            {
                segments[segmentCount++] = closingSegment;
            }

            return segmentCount;
        }

        /// <summary>
        /// Creates one contour-local stroke segment descriptor.
        /// </summary>
        /// <param name="start">The segment start point.</param>
        /// <param name="end">The segment end point.</param>
        /// <param name="segment">Receives the segment descriptor.</param>
        /// <returns><see langword="true"/> when a non-degenerate segment exists.</returns>
        private static bool TryCreateStrokeContourSegment(PointF start, PointF end, out StrokeContourSegment segment)
        {
            if (Vector2.DistanceSquared(start, end) <= StrokeMicroSegmentEpsilon * StrokeMicroSegmentEpsilon)
            {
                segment = default;
                return false;
            }

            if (!TryGetDirection(start, end, out Vector2 tangent, out float length))
            {
                segment = default;
                return false;
            }

            segment = new StrokeContourSegment(start, end, tangent, length);
            return true;
        }

        /// <summary>
        /// Gets the point bounds for one contour.
        /// </summary>
        /// <param name="contourPoints">The contiguous contour points.</param>
        /// <returns>The contour point bounds.</returns>
        private RectangleF GetPointBounds(ReadOnlySpan<PointF> contourPoints)
        {
            Matrix4x4 residual = this.Residual;
            bool hasResidual = this.HasResidual;
            PointF first = hasResidual ? PointF.Transform(contourPoints[0], residual) : contourPoints[0];
            float minX = first.X;
            float minY = first.Y;
            float maxX = minX;
            float maxY = minY;

            for (int i = 1; i < contourPoints.Length; i++)
            {
                PointF point = hasResidual ? PointF.Transform(contourPoints[i], residual) : contourPoints[i];
                minX = MathF.Min(minX, point.X);
                minY = MathF.Min(minY, point.Y);
                maxX = MathF.Max(maxX, point.X);
                maxY = MathF.Max(maxY, point.Y);
            }

            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        /// <summary>
        /// Emits one stroked open segment.
        /// </summary>
        /// <param name="start">The segment start point.</param>
        /// <param name="end">The segment end point.</param>
        /// <param name="contained">Indicates whether the segment is fully contained within the interest.</param>
        private void EmitOpenSegmentStrokeContour(PointF start, PointF end, bool contained)
        {
            if (!TryGetDirection(start, end, out Vector2 tangent, out _))
            {
                this.EmitPointStrokeContour(start, contained);
                return;
            }

            float halfWidth = this.stroke.HalfWidth;
            Vector2 normal = GetStrokeOffsetNormal(tangent) * halfWidth;
            Vector2 extension = this.stroke.LineCap == LineCap.Square ? tangent * halfWidth : Vector2.Zero;
            Vector2 startVector = start;
            Vector2 endVector = end;
            PointF p0 = startVector + normal - extension;
            PointF p1 = endVector + normal + extension;
            PointF p2 = endVector - normal + extension;
            PointF p3 = startVector - normal - extension;

            this.EmitLine(p0, p1, contained);

            if (this.stroke.LineCap == LineCap.Round)
            {
                this.EmitDirectedArcContour(endVector, normal, -normal, contained);
            }
            else
            {
                this.EmitLine(p1, p2, contained);
            }

            this.EmitLine(p2, p3, contained);

            if (this.stroke.LineCap == LineCap.Round)
            {
                this.EmitDirectedArcContour(startVector, -normal, normal, contained);
            }
            else
            {
                this.EmitLine(p3, p0, contained);
            }
        }

        /// <summary>
        /// Emits one stroked open multi-segment contour from precomputed contour-local segments.
        /// </summary>
        /// <param name="segments">The precomputed contour-local segments.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void EmitOpenStrokeContour(ReadOnlySpan<StrokeContourSegment> segments, bool contained)
        {
            StrokeContourSegment startSegment = segments[0];
            StrokeContourSegment endSegment = segments[^1];
            float halfWidth = this.stroke.HalfWidth;
            Vector2 startNormal = startSegment.Normal * halfWidth;
            Vector2 endNormal = endSegment.Normal * halfWidth;
            Vector2 startExtension = this.stroke.LineCap == LineCap.Square ? startSegment.Tangent * halfWidth : Vector2.Zero;
            Vector2 endExtension = this.stroke.LineCap == LineCap.Square ? endSegment.Tangent * halfWidth : Vector2.Zero;
            Vector2 startPoint = startSegment.Start;
            Vector2 endPoint = endSegment.End;
            ContourState strokeContour = default;
            this.AppendContourPoint(ref strokeContour, startPoint + startNormal - startExtension, contained);

            for (int i = 1; i < segments.Length; i++)
            {
                StrokeContourSegment previousSegment = segments[i - 1];
                StrokeContourSegment nextSegment = segments[i];

                this.AppendSideJoinContour(
                    ref strokeContour,
                    previousSegment.End,
                    previousSegment.Tangent,
                    nextSegment.Tangent,
                    previousSegment.Length,
                    nextSegment.Length,
                    1F,
                    contained);
            }

            this.AppendContourPoint(ref strokeContour, endPoint + endNormal + endExtension, contained);

            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, endPoint, endNormal, -endNormal, contained);
            }
            else
            {
                this.AppendContourPoint(ref strokeContour, endPoint - endNormal + endExtension, contained);
            }

            for (int i = segments.Length - 1; i >= 1; i--)
            {
                StrokeContourSegment previousSegment = segments[i];
                StrokeContourSegment nextSegment = segments[i - 1];

                this.AppendSideJoinContour(
                    ref strokeContour,
                    previousSegment.Start,
                    -previousSegment.Tangent,
                    -nextSegment.Tangent,
                    previousSegment.Length,
                    nextSegment.Length,
                    1F,
                    contained);
            }

            this.AppendContourPoint(ref strokeContour, startPoint - startNormal - startExtension, contained);

            if (this.stroke.LineCap == LineCap.Round)
            {
                this.AppendDirectedArcContour(ref strokeContour, startPoint, -startNormal, startNormal, contained);
            }

            this.CloseContour(ref strokeContour, contained);
        }

        /// <summary>
        /// Emits the two stroked contours for a closed contour from precomputed contour-local segments.
        /// </summary>
        /// <param name="segments">The precomputed contour-local segments.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void EmitClosedStrokeContour(ReadOnlySpan<StrokeContourSegment> segments, bool contained)
        {
            ContourState leftContour = default;
            for (int i = 0; i < segments.Length; i++)
            {
                StrokeContourSegment previousSegment = i == 0 ? segments[^1] : segments[i - 1];
                StrokeContourSegment nextSegment = segments[i];

                this.AppendSideJoinContour(
                    ref leftContour,
                    nextSegment.Start,
                    previousSegment.Tangent,
                    nextSegment.Tangent,
                    previousSegment.Length,
                    nextSegment.Length,
                    1F,
                    contained);
            }

            this.CloseContour(ref leftContour, contained);

            ContourState reversedContour = default;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                StrokeContourSegment previousSegment = segments[i];
                StrokeContourSegment nextSegment = i == 0 ? segments[^1] : segments[i - 1];

                this.AppendSideJoinContour(
                    ref reversedContour,
                    previousSegment.Start,
                    -previousSegment.Tangent,
                    -nextSegment.Tangent,
                    previousSegment.Length,
                    nextSegment.Length,
                    1F,
                    contained);
            }

            this.CloseContour(ref reversedContour, contained);
        }

        /// <summary>
        /// Emits a point-like stroke as a cap contour.
        /// </summary>
        /// <param name="point">The point-like stroke location.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void EmitPointStrokeContour(PointF point, bool contained)
        {
            Vector2 center = point;
            float halfWidth = this.stroke.HalfWidth;
            if (this.stroke.LineCap == LineCap.Round)
            {
                Vector2 startOffset = new(halfWidth, 0F);
                this.EmitDirectedArcContour(center, startOffset, -startOffset, contained);
                this.EmitDirectedArcContour(center, -startOffset, startOffset, contained);
                return;
            }

            PointF p0 = center + new Vector2(-halfWidth, -halfWidth);
            PointF p1 = center + new Vector2(halfWidth, -halfWidth);
            PointF p2 = center + new Vector2(halfWidth, halfWidth);
            PointF p3 = center + new Vector2(-halfWidth, halfWidth);
            this.EmitLine(p0, p1, contained);
            this.EmitLine(p1, p2, contained);
            this.EmitLine(p2, p3, contained);
            this.EmitLine(p3, p0, contained);
        }

        /// <summary>
        /// Emits one round cap or join arc directly into the retained line storage.
        /// </summary>
        /// <param name="center">The arc center.</param>
        /// <param name="fromOffset">The start offset from the center.</param>
        /// <param name="toOffset">The end offset from the center.</param>
        /// <param name="contained">Indicates whether the arc is fully contained within the interest.</param>
        private void EmitDirectedArcContour(
            Vector2 center,
            Vector2 fromOffset,
            Vector2 toOffset,
            bool contained)
        {
            if (fromOffset == Vector2.Zero || toOffset == Vector2.Zero)
            {
                this.EmitLine(center + fromOffset, center + toOffset, contained);
                return;
            }

            float radius = fromOffset.Length();
            if (radius <= StrokeDirectionEpsilon)
            {
                this.EmitLine(center + fromOffset, center + toOffset, contained);
                return;
            }

            double startAngle = Math.Atan2(fromOffset.Y, fromOffset.X);
            double endAngle = Math.Atan2(toOffset.Y, toOffset.X);
            double sweep = NormalizePositiveAngle(endAngle - startAngle);
            int subdivisionCount = GetArcSubdivisionCount(radius, sweep, this.stroke.ArcDetailScale);
            double step = sweep / (subdivisionCount + 1);

            PointF previousPoint = center + fromOffset;
            for (int i = 1; i <= subdivisionCount; i++)
            {
                float angle = (float)(startAngle + (step * i));
                PointF point = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                this.EmitLine(previousPoint, point, contained);
                previousPoint = point;
            }

            this.EmitLine(previousPoint, center + toOffset, contained);
        }

        /// <summary>
        /// Appends a contour arc directly to the active stroke contour.
        /// </summary>
        /// <param name="contour">The active contour state.</param>
        /// <param name="center">The arc center.</param>
        /// <param name="fromOffset">The start offset from the center.</param>
        /// <param name="toOffset">The end offset from the center.</param>
        /// <param name="contained">Indicates whether the arc is fully contained within the interest.</param>
        private void AppendDirectedArcContour(
            ref ContourState contour,
            Vector2 center,
            Vector2 fromOffset,
            Vector2 toOffset,
            bool contained)
        {
            if (fromOffset == Vector2.Zero || toOffset == Vector2.Zero)
            {
                this.AppendContourPoint(ref contour, center + toOffset, contained);
                return;
            }

            float radius = fromOffset.Length();
            if (radius <= StrokeDirectionEpsilon)
            {
                this.AppendContourPoint(ref contour, center + toOffset, contained);
                return;
            }

            double startAngle = Math.Atan2(fromOffset.Y, fromOffset.X);
            double endAngle = Math.Atan2(toOffset.Y, toOffset.X);
            double sweep = NormalizePositiveAngle(endAngle - startAngle);
            int subdivisionCount = GetArcSubdivisionCount(radius, sweep, this.stroke.ArcDetailScale);
            double step = sweep / (subdivisionCount + 1);

            for (int i = 1; i <= subdivisionCount; i++)
            {
                float angle = (float)(startAngle + (step * i));
                this.AppendContourPoint(
                    ref contour,
                    center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius),
                    contained);
            }

            this.AppendContourPoint(ref contour, center + toOffset, contained);
        }

        /// <summary>
        /// Appends one side join point sequence directly to the active stroke contour.
        /// </summary>
        /// <param name="contour">The active contour state.</param>
        /// <param name="point">The join point.</param>
        /// <param name="previousTangent">The normalized tangent of the previous segment.</param>
        /// <param name="nextTangent">The normalized tangent of the next segment.</param>
        /// <param name="previousLength">The length of the previous segment.</param>
        /// <param name="nextLength">The length of the next segment.</param>
        /// <param name="sideSign">The side selector for the contour being emitted.</param>
        /// <param name="contained">Indicates whether the join is fully contained within the interest.</param>
        private void AppendSideJoinContour(
            ref ContourState contour,
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            float previousLength,
            float nextLength,
            float sideSign,
            bool contained)
        {
            Vector2 previousOffset = GetStrokeOffsetNormal(previousTangent) * (this.stroke.HalfWidth * sideSign);
            Vector2 nextOffset = GetStrokeOffsetNormal(nextTangent) * (this.stroke.HalfWidth * sideSign);
            float dot = Vector2.Dot(previousTangent, nextTangent);
            float cross = Cross(nextTangent, previousTangent);

            if (MathF.Abs(cross) <= StrokeParallelEpsilon && dot > 0F)
            {
                this.AppendContourPoint(ref contour, point + nextOffset, contained);
                return;
            }

            // Mirror PolygonStroker.CalcJoin(): the next x previous turn sign decides whether the
            // active outline sees this corner as inner or outer. That keeps forward and reverse
            // side emission aligned with the CPU outline stroker's bevel/miter decisions.
            bool isInnerJoin = cross > 0F;

            if (isInnerJoin)
            {
                this.AppendInnerJoinContour(
                    ref contour,
                    point,
                    previousTangent,
                    nextTangent,
                    previousOffset,
                    nextOffset,
                    previousLength,
                    nextLength,
                    contained);
                return;
            }

            this.AppendOuterJoinContour(
                ref contour,
                point,
                previousTangent,
                nextTangent,
                previousOffset,
                nextOffset,
                contained);
        }

        /// <summary>
        /// Appends one outer join directly to the active stroke contour.
        /// </summary>
        /// <param name="contour">The active contour state.</param>
        /// <param name="point">The join point.</param>
        /// <param name="previousTangent">The normalized tangent of the previous segment.</param>
        /// <param name="nextTangent">The normalized tangent of the next segment.</param>
        /// <param name="previousOffset">The offset vector on the previous segment.</param>
        /// <param name="nextOffset">The offset vector on the next segment.</param>
        /// <param name="contained">Indicates whether the join is fully contained within the interest.</param>
        private void AppendOuterJoinContour(
            ref ContourState contour,
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            Vector2 previousOffset,
            Vector2 nextOffset,
            bool contained)
        {
            float bevelDistance = ((previousOffset + nextOffset) * 0.5F).Length();
            if ((this.stroke.LineJoin is LineJoin.Round or LineJoin.Bevel) &&
                (this.stroke.ArcDetailScale * (this.stroke.HalfWidth - bevelDistance)) < (this.stroke.HalfWidth / 1024F))
            {
                if (TryIntersectOffsetLines(point, previousOffset, previousTangent, nextOffset, nextTangent, out Vector2 intersection))
                {
                    this.AppendContourPoint(ref contour, intersection, contained);
                }
                else
                {
                    this.AppendContourPoint(ref contour, point + previousOffset, contained);
                }

                return;
            }

            switch (this.stroke.LineJoin)
            {
                case LineJoin.Round:
                    this.AppendContourPoint(ref contour, point + previousOffset, contained);
                    this.AppendDirectedArcContour(
                        ref contour,
                        point,
                        previousOffset,
                        nextOffset,
                        contained);
                    return;

                case LineJoin.Miter:
                case LineJoin.MiterRevert:
                case LineJoin.MiterRound:
                    this.AppendMiterJoinContour(
                        ref contour,
                        point,
                        previousTangent,
                        nextTangent,
                        previousOffset,
                        nextOffset,
                        this.stroke.LineJoin,
                        this.stroke.MiterLimit,
                        contained);
                    return;

                default:
                    this.AppendContourPoint(ref contour, point + previousOffset, contained);
                    this.AppendContourPoint(ref contour, point + nextOffset, contained);
                    return;
            }
        }

        /// <summary>
        /// Appends one inner join directly to the active stroke contour.
        /// </summary>
        /// <param name="contour">The active contour state.</param>
        /// <param name="point">The join point.</param>
        /// <param name="previousTangent">The normalized tangent of the previous segment.</param>
        /// <param name="nextTangent">The normalized tangent of the next segment.</param>
        /// <param name="previousOffset">The offset vector on the previous segment.</param>
        /// <param name="nextOffset">The offset vector on the next segment.</param>
        /// <param name="previousLength">The length of the previous segment.</param>
        /// <param name="nextLength">The length of the next segment.</param>
        /// <param name="contained">Indicates whether the join is fully contained within the interest.</param>
        private void AppendInnerJoinContour(
            ref ContourState contour,
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            Vector2 previousOffset,
            Vector2 nextOffset,
            float previousLength,
            float nextLength,
            bool contained)
        {
            double limit = Math.Min(previousLength, nextLength) / Math.Max(this.stroke.HalfWidth, StrokeDirectionEpsilon);
            if (limit < this.stroke.InnerMiterLimit)
            {
                limit = this.stroke.InnerMiterLimit;
            }

            switch (this.stroke.InnerJoin)
            {
                case InnerJoin.Miter:
                    this.AppendMiterJoinContour(
                        ref contour,
                        point,
                        previousTangent,
                        nextTangent,
                        previousOffset,
                        nextOffset,
                        LineJoin.MiterRevert,
                        limit,
                        contained);
                    return;

                case InnerJoin.Jag:
                case InnerJoin.Round:
                    Vector2 offsetDelta = previousOffset - nextOffset;
                    float offsetDeltaSquared = offsetDelta.LengthSquared();
                    if (offsetDeltaSquared < previousLength * previousLength && offsetDeltaSquared < nextLength * nextLength)
                    {
                        this.AppendMiterJoinContour(
                            ref contour,
                            point,
                            previousTangent,
                            nextTangent,
                            previousOffset,
                            nextOffset,
                            LineJoin.MiterRevert,
                            limit,
                            contained);
                        return;
                    }

                    this.AppendContourPoint(ref contour, point + previousOffset, contained);
                    this.AppendContourPoint(ref contour, point, contained);
                    if (this.stroke.InnerJoin == InnerJoin.Round)
                    {
                        this.AppendContourPoint(ref contour, point + nextOffset, contained);
                        this.AppendDirectedArcContour(
                            ref contour,
                            point,
                            nextOffset,
                            previousOffset,
                            contained);
                    }

                    this.AppendContourPoint(ref contour, point, contained);
                    this.AppendContourPoint(ref contour, point + nextOffset, contained);
                    return;

                default:
                    this.AppendContourPoint(ref contour, point + previousOffset, contained);
                    this.AppendContourPoint(ref contour, point + nextOffset, contained);
                    return;
            }
        }

        /// <summary>
        /// Appends one miter-family join directly to the active stroke contour.
        /// </summary>
        /// <param name="contour">The active contour state.</param>
        /// <param name="point">The join point.</param>
        /// <param name="previousTangent">The normalized tangent of the previous segment.</param>
        /// <param name="nextTangent">The normalized tangent of the next segment.</param>
        /// <param name="previousOffset">The offset vector on the previous segment.</param>
        /// <param name="nextOffset">The offset vector on the next segment.</param>
        /// <param name="lineJoin">The miter-family join behavior to apply.</param>
        /// <param name="miterLimit">The effective miter limit.</param>
        /// <param name="contained">Indicates whether the join is fully contained within the interest.</param>
        private void AppendMiterJoinContour(
            ref ContourState contour,
            Vector2 point,
            Vector2 previousTangent,
            Vector2 nextTangent,
            Vector2 previousOffset,
            Vector2 nextOffset,
            LineJoin lineJoin,
            double miterLimit,
            bool contained)
        {
            Vector2 previousPoint = point + previousOffset;
            Vector2 nextPoint = point + nextOffset;
            float bevelDistance = ((previousOffset + nextOffset) * 0.5F).Length();
            float limit = (float)(Math.Max(miterLimit, 1D) * Math.Max(previousOffset.Length(), nextOffset.Length()));

            if (TryIntersectOffsetLines(point, previousOffset, previousTangent, nextOffset, nextTangent, out Vector2 intersection))
            {
                float intersectionDistance = Vector2.Distance(point, intersection);
                if (intersectionDistance <= limit)
                {
                    this.AppendContourPoint(ref contour, intersection, contained);
                    return;
                }

                switch (lineJoin)
                {
                    case LineJoin.MiterRevert:
                        this.AppendContourPoint(ref contour, previousPoint, contained);
                        this.AppendContourPoint(ref contour, nextPoint, contained);
                        return;

                    case LineJoin.MiterRound:
                        this.AppendDirectedArcContour(
                            ref contour,
                            point,
                            previousOffset,
                            nextOffset,
                            contained);
                        return;

                    default:
                        if (intersectionDistance <= bevelDistance + StrokeDirectionEpsilon)
                        {
                            this.AppendContourPoint(ref contour, previousPoint, contained);
                            this.AppendContourPoint(ref contour, nextPoint, contained);
                            return;
                        }

                        float ratio = (limit - bevelDistance) / (intersectionDistance - bevelDistance);
                        this.AppendContourPoint(ref contour, previousPoint + ((intersection - previousPoint) * ratio), contained);
                        this.AppendContourPoint(ref contour, nextPoint + ((intersection - nextPoint) * ratio), contained);
                        return;
                }
            }

            switch (lineJoin)
            {
                case LineJoin.MiterRevert:
                    this.AppendContourPoint(ref contour, previousPoint, contained);
                    this.AppendContourPoint(ref contour, nextPoint, contained);
                    return;

                case LineJoin.MiterRound:
                    this.AppendDirectedArcContour(
                        ref contour,
                        point,
                        previousOffset,
                        nextOffset,
                        contained);
                    return;

                default:
                    float fallbackLimit = (float)Math.Max(miterLimit, 1D) * (previousOffset.Length() <= 0F ? nextOffset.Length() : previousOffset.Length());
                    this.AppendContourPoint(ref contour, previousPoint + (previousTangent * fallbackLimit), contained);
                    this.AppendContourPoint(ref contour, nextPoint - (nextTangent * fallbackLimit), contained);
                    return;
            }
        }

        /// <summary>
        /// Appends one point to the active contour.
        /// </summary>
        /// <param name="state">The active contour state.</param>
        /// <param name="point">The point to append.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void AppendContourPoint(ref ContourState state, PointF point, bool contained)
        {
            if (!state.HasPoint)
            {
                state.HasPoint = true;
                state.FirstPoint = point;
                state.PreviousPoint = point;
                return;
            }

            if (state.PreviousPoint == point)
            {
                return;
            }

            this.EmitLine(state.PreviousPoint, point, contained);
            state.PreviousPoint = point;
        }

        /// <summary>
        /// Closes the active contour.
        /// </summary>
        /// <param name="state">The active contour state.</param>
        /// <param name="contained">Indicates whether the contour is fully contained within the interest.</param>
        private void CloseContour(ref ContourState state, bool contained)
        {
            if (!state.HasPoint || state.PreviousPoint == state.FirstPoint)
            {
                return;
            }

            this.EmitLine(state.PreviousPoint, state.FirstPoint, contained);
            state.PreviousPoint = state.FirstPoint;
        }

        /// <summary>
        /// Emits one stroked boundary edge into retained line storage.
        /// </summary>
        /// <param name="start">The edge start point.</param>
        /// <param name="end">The edge end point.</param>
        /// <param name="contained">Indicates whether the edge is fully contained within the interest.</param>
        private void EmitLine(PointF start, PointF end, bool contained)
        {
            if (contained)
            {
                this.AddContainedLineF24Dot8(
                    FloatToFixed24Dot8(((start.X + this.TranslateX) - this.MinX) + this.SamplingOffsetX),
                    FloatToFixed24Dot8(((start.Y + this.TranslateY) - this.MinY) + this.SamplingOffsetY),
                    FloatToFixed24Dot8(((end.X + this.TranslateX) - this.MinX) + this.SamplingOffsetX),
                    FloatToFixed24Dot8(((end.Y + this.TranslateY) - this.MinY) + this.SamplingOffsetY));
                return;
            }

            this.AddUncontainedLine(
                ((start.X + this.TranslateX) - this.MinX) + this.SamplingOffsetX,
                ((start.Y + this.TranslateY) - this.MinY) + this.SamplingOffsetY,
                ((end.X + this.TranslateX) - this.MinX) + this.SamplingOffsetX,
                ((end.Y + this.TranslateY) - this.MinY) + this.SamplingOffsetY);
        }

        /// <summary>
        /// Returns the stroke offset normal matching PolygonStroker's dx/dy convention.
        /// </summary>
        /// <param name="tangent">The normalized segment tangent.</param>
        /// <returns>The stroke-side offset normal.</returns>
        private static Vector2 GetStrokeOffsetNormal(Vector2 tangent) => new(tangent.Y, -tangent.X);

        private readonly struct StrokeContourSegment
        {
            public StrokeContourSegment(PointF start, PointF end, Vector2 tangent, float length)
            {
                this.Start = start;
                this.End = end;
                this.Tangent = tangent;
                this.Normal = GetStrokeOffsetNormal(tangent);
                this.Length = length;
            }

            public PointF Start { get; }

            public PointF End { get; }

            public Vector2 Tangent { get; }

            public Vector2 Normal { get; }

            public float Length { get; }
        }

        private struct ContourState
        {
            public bool HasPoint;
            public PointF FirstPoint;
            public PointF PreviousPoint;
        }
    }

    /// <summary>
    /// Stroke linearizer that finalizes retained lines into the 32-bit-X encoding.
    /// </summary>
    private sealed class StrokeLinearizerX32Y16 : StrokeLinearizer<LineArrayX32Y16>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeLinearizerX32Y16"/> class.
        /// </summary>
        /// <param name="geometry">The stroked centerline geometry.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="minX">The minimum destination X bound after clipping.</param>
        /// <param name="minY">The minimum destination Y bound after clipping.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index.</param>
        /// <param name="rowBandCount">The retained row-band count.</param>
        /// <param name="samplingOffsetX">The horizontal sampling offset.</param>
        /// <param name="samplingOffsetY">The vertical sampling offset.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        public StrokeLinearizerX32Y16(
            LinearGeometry geometry,
            Matrix4x4 residual,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY,
            MemoryAllocator allocator)
            : base(geometry, residual, stroke, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, samplingOffsetX, samplingOffsetY, allocator)
            => this.FinalLines = new LineArrayX32Y16Block?[rowBandCount];

        /// <summary>
        /// Gets the finalized retained line blocks for each row band.
        /// </summary>
        public LineArrayX32Y16Block?[] FinalLines { get; }

        /// <inheritdoc />
        protected override LineArrayX32Y16 CreateLineArray() => new();

        /// <inheritdoc />
        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(x0, y0, x1, y1);

        /// <inheritdoc />
        protected override void FinalizeLines()
        {
            for (int i = 0; i < this.RowBandCount; i++)
            {
                LineArrayX32Y16? lineArray = this.LineArrays[i];
                this.FinalLines[i] = lineArray?.GetFrontBlock();
                this.FirstBlockLineCounts[i] = lineArray?.GetFrontBlockLineCount() ?? 0;
            }
        }

        /// <summary>
        /// Executes the retained stroke linearization pass and returns the finalized payload.
        /// </summary>
        /// <param name="result">The finalized retained raster data.</param>
        /// <returns><see langword="true"/> when retained coverage was produced; otherwise <see langword="false"/>.</returns>
        internal bool TryProcess(out LinearizedRasterData<LineArrayX32Y16Block> result)
        {
            if (!this.ProcessCore())
            {
                result = null!;
                return false;
            }

            result = new LinearizedRasterData<LineArrayX32Y16Block>(
                this.Geometry,
                new TileBounds(this.MinX, this.FirstBandIndex, this.Width, this.RowBandCount),
                this.FinalLines,
                this.FirstBlockLineCounts,
                this.StartCoverTable);

            return true;
        }
    }

    /// <summary>
    /// Stroke linearizer that finalizes retained lines into the packed 16-bit-X encoding.
    /// </summary>
    private sealed class StrokeLinearizerX16Y16 : StrokeLinearizer<LineArrayX16Y16>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeLinearizerX16Y16"/> class.
        /// </summary>
        /// <param name="geometry">The stroked centerline geometry.</param>
        /// <param name="residual">The residual transform applied to each source point during emission.</param>
        /// <param name="stroke">The stroke style.</param>
        /// <param name="translateX">The destination-space X translation applied at composition time.</param>
        /// <param name="translateY">The destination-space Y translation applied at composition time.</param>
        /// <param name="minX">The minimum destination X bound after clipping.</param>
        /// <param name="minY">The minimum destination Y bound after clipping.</param>
        /// <param name="width">The visible destination width in pixels.</param>
        /// <param name="height">The visible destination height in pixels.</param>
        /// <param name="firstBandIndex">The first retained row-band index.</param>
        /// <param name="rowBandCount">The retained row-band count.</param>
        /// <param name="samplingOffsetX">The horizontal sampling offset.</param>
        /// <param name="samplingOffsetY">The vertical sampling offset.</param>
        /// <param name="allocator">The allocator used for retained start-cover storage.</param>
        public StrokeLinearizerX16Y16(
            LinearGeometry geometry,
            Matrix4x4 residual,
            StrokeStyle stroke,
            int translateX,
            int translateY,
            int minX,
            int minY,
            int width,
            int height,
            int firstBandIndex,
            int rowBandCount,
            float samplingOffsetX,
            float samplingOffsetY,
            MemoryAllocator allocator)
            : base(geometry, residual, stroke, translateX, translateY, minX, minY, width, height, firstBandIndex, rowBandCount, samplingOffsetX, samplingOffsetY, allocator)
            => this.FinalLines = new LineArrayX16Y16Block?[rowBandCount];

        /// <summary>
        /// Gets the finalized retained line blocks for each row band.
        /// </summary>
        public LineArrayX16Y16Block?[] FinalLines { get; }

        /// <inheritdoc />
        protected override LineArrayX16Y16 CreateLineArray() => new();

        /// <inheritdoc />
        protected override void AppendLine(int rowIndex, int x0, int y0, int x1, int y1)
            => this.GetOrCreateLineArray(rowIndex).AppendLine(x0, y0, x1, y1);

        /// <inheritdoc />
        protected override void FinalizeLines()
        {
            for (int i = 0; i < this.RowBandCount; i++)
            {
                LineArrayX16Y16? lineArray = this.LineArrays[i];
                this.FinalLines[i] = lineArray?.GetFrontBlock();
                this.FirstBlockLineCounts[i] = lineArray?.GetFrontBlockLineCount() ?? 0;
            }
        }

        /// <summary>
        /// Executes the retained stroke linearization pass and returns the finalized payload.
        /// </summary>
        /// <param name="result">The finalized retained raster data.</param>
        /// <returns><see langword="true"/> when retained coverage was produced; otherwise <see langword="false"/>.</returns>
        internal bool TryProcess(out LinearizedRasterData<LineArrayX16Y16Block> result)
        {
            if (!this.ProcessCore())
            {
                result = null!;
                return false;
            }

            result = new LinearizedRasterData<LineArrayX16Y16Block>(
                this.Geometry,
                new TileBounds(this.MinX, this.FirstBandIndex, this.Width, this.RowBandCount),
                this.FinalLines,
                this.FirstBlockLineCounts,
                this.StartCoverTable);

            return true;
        }
    }
}
