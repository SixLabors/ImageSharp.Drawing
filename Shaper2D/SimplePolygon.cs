using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;
using Shaper2D.Primitives;
using System.Collections.ObjectModel;

namespace Shaper2D
{
    internal class SimplePolygon
    {
        private Lazy<RectangleF> bounds;
        private IReadOnlyList<ILineSegment> segments;
        public bool IsHole { get; set; } = false;

        public SimplePolygon(ILineSegment segments)
            : this(new[] { segments })
        {
        }

        public SimplePolygon(IEnumerable<Vector2> points)
            : this(new LinearLineSegment(points))
        {
        }

        public SimplePolygon(IEnumerable<ILineSegment> segments)
        {
            this.segments = new ReadOnlyCollection<ILineSegment>(segments.ToList());

            bounds = new Lazy<RectangleF>(CalculateBounds);
        }

        public int Corners
        {
            get
            {
                CalculatePoints();
                return polyCorners;
            }
        }


        public Vector2 this[int index]
        {
            get
            {
                CalculatePoints();

                var boundexIndex = Math.Abs(index) % polyCorners;
                if (index < 0)
                {
                    // back counting 
                    index = polyCorners - boundexIndex;
                }
                else
                {
                    index = boundexIndex;
                }

                return new Vector2(polyX[index], polyY[index]);
            }
        }

        public RectangleF Bounds => bounds.Value;

        float[] constant;
        float[] multiple;
        bool calcualted = false;
        object locker = new object();

        float[] polyY;
        float[] polyX;
        int polyCorners;
        bool calcualtedPoints = false;
        object lockerPoints = new object();

        private void CalculatePoints()
        {
            if (calcualtedPoints) return;
            lock (lockerPoints)
            {
                if (calcualtedPoints) return;

                var points = Simplify(segments).ToArray();
                polyX = points.Select(x => (float)x.X).ToArray();
                polyY = points.Select(x => (float)x.Y).ToArray();
                polyCorners = points.Length;
                calcualtedPoints = true;
            }
        }

        private void CalculateConstants()
        {
            if (calcualted) return;
            lock (locker)
            {
                if (calcualted) return;

                // ensure points are availible
                CalculatePoints();

                constant = new float[polyCorners];
                multiple = new float[polyCorners];
                int i, j = polyCorners - 1;

                for (i = 0; i < polyCorners; i++)
                {
                    if (polyY[j] == polyY[i])
                    {
                        constant[i] = polyX[i];
                        multiple[i] = 0;
                    }
                    else
                    {
                        constant[i] = polyX[i] - (polyY[i] * polyX[j]) / (polyY[j] - polyY[i]) + (polyY[i] * polyX[i]) / (polyY[j] - polyY[i]);
                        multiple[i] = (polyX[j] - polyX[i]) / (polyY[j] - polyY[i]);
                    }
                    j = i;
                }



                calcualted = true;
            }
        }

        bool PointInPolygon(float x, float y)
        {
            if (!Bounds.Contains((int)x, (int)y))
            {
                return false;
            }

            // things we cound do to make this more efficient
            // pre calculate simple regions that are inside the polygo and see if its contained in one of those

            CalculateConstants();


            var j = polyCorners - 1;
            bool oddNodes = false;

            for (var i = 0; i < polyCorners; i++)
            {
                if ((polyY[i] < y && polyY[j] >= y
                || polyY[j] < y && polyY[i] >= y))
                {
                    oddNodes ^= (y * multiple[i] + constant[i] < x);
                }
                j = i;
            }

            return oddNodes;
        }

        private float DistanceSquared(float x1, float y1, float x2, float y2, float xp, float yp)
        {
            var px = x2 - x1;
            var py = y2 - y1;

            float something = px * px + py * py;

            var u = ((xp - x1) * px + (yp - y1) * py) / something;

            if (u > 1)
            {
                u = 1;
            }
            else if (u < 0)
            {
                u = 0;
            }

            var x = x1 + u * px;
            var y = y1 + u * py;

            var dx = x - xp;
            var dy = y - yp;

            return dx * dx + dy * dy;
        }

        private float CalculateDistance(Vector2 vector)
        {
            return CalculateDistance(vector.X, vector.Y);
        }

        private float CalculateDistance(float x, float y)
        {

            float distance = float.MaxValue;
            for (var i = 0; i < polyCorners; i++)
            {
                var next = i + 1;
                if (next == polyCorners)
                {
                    next = 0;
                }

                var lastDistance = DistanceSquared(polyX[i], polyY[i], polyX[next], polyY[next], x, y);

                if (lastDistance < distance)
                {
                    distance = lastDistance;
                }
            }

            return (float)Math.Sqrt(distance);
        }

        public float Distance(Vector2 vector)
        {
            return Distance(vector.X, vector.Y);
        }

        public float Distance(float x, float y)
        {
            // we will do a nieve point in polygon test here for now 
            // TODO optermise here and actually return a distance
            if (PointInPolygon(x, y))
            {
                if (IsHole)
                {
                    return CalculateDistance(x, y);
                }
                //we are on line or inside
                return 0;
            }

            if (IsHole)
            {
                return 0;
            }
            return CalculateDistance(x, y);
        }

        private RectangleF CalculateBounds()
        {
            // ensure points are availible
            CalculatePoints();
            var minX = polyX.Min();
            var maxX = polyX.Max();
            var minY = polyY.Min();
            var maxY = polyY.Max();

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        private IEnumerable<Vector2> Simplify(IEnumerable<ILineSegment> segments)
        {
            //used to deduplicate
            HashSet<Vector2> pointHash = new HashSet<Vector2>();

            foreach (var segment in segments)
            {
                var points = segment.Simplify();
                foreach (var p in points)
                {
                    if (!pointHash.Contains(p))
                    {
                        pointHash.Add(p);
                        yield return p;
                    }
                }
            }
        }


        Vector2? LineIntersectionPoint(Vector2 ps1, Vector2 pe1, Vector2 ps2, Vector2 pe2)
        {
            // Get A,B,C of first line - points : ps1 to pe1
            float A1 = pe1.Y - ps1.Y;
            float B1 = ps1.X - pe1.X;
            float C1 = A1 * ps1.X + B1 * ps1.Y;

            // Get A,B,C of second line - points : ps2 to pe2
            float A2 = pe2.Y - ps2.Y;
            float B2 = ps2.X - pe2.X;
            float C2 = A2 * ps2.X + B2 * ps2.Y;

            // Get delta and check if the lines are parallel
            float delta = A1 * B2 - A2 * B1;
            if (delta == 0)
            {
                return null;
            }

            // now return the Vector2 intersection point
            return new Vector2(
                (B2 * C1 - B1 * C2) / delta,
                (A1 * C2 - A2 * C1) / delta
            );
        }

        private RectangleF GetBounds(Vector2 start, Vector2 end)
        {
            var minX = Math.Min(start.X, end.X);
            var maxX = Math.Min(start.X, end.X);

            var minY = Math.Min(start.Y, end.Y);
            var maxY = Math.Min(start.Y, end.Y);

            return new RectangleF((int)minX, (int)minY, (int)maxX - (int)minX, (int)maxY - (int)minY);
        }

        //public Vector2? GetIntersectionPoint(SimplePolygon polygon)
        //{
        //    for (var i = 0; i < this.Corners; i++)
        //    {
        //        var prevPoint = this[i - 1];

        //        var currentPoint = this[i];

        //        RectangleF src = GetBounds(prevPoint, currentPoint);


        //        for (var j = 0; j < polygon.Corners; j++)
        //        {

        //            var prevPointOther = polygon[j - 1];

        //            var currentPointOther = polygon[j];

        //            RectangleF target = GetBounds(prevPointOther, currentPointOther);

        //            //first do they have overlapping bounding boxes

        //            if (src.Intersects(target))
        //            {
        //                //there boxes intersect lets find where there lines touch

        //                LineIntersectionPoint()
        //            }
        //        }


        //    }
        //    // does this poly intersect with another


        //}

        public SimplePolygon Clone()
        {
            List<Vector2> points = new List<Vector2>();
            for (var i = 0; i < this.polyCorners; i++)
            {
                points.Add(this[i]);
            }
            return new SimplePolygon(points) { IsHole = this.IsHole };
        }
    }
}
