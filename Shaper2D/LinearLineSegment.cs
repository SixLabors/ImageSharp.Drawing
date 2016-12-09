using Shaper2D.Primitives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Shaper2D
{
    public class LinearLineSegment : ILineSegment
    {
        public IReadOnlyList<Vector2> ControlPoints { get; }


        internal LinearLineSegment(IEnumerable<Vector2> points)
        {
            //Guard.NotNull(points, nameof(points));
            //Guard.MustBeGreaterThanOrEqualTo(points.Count(), 2, nameof(points));

            ControlPoints = new ReadOnlyCollection<Vector2>(points.ToList());
        }

        public LinearLineSegment(params PointF[] points)
           : this(points?.Select(x => x.ToVector2()))
        {
        }

        public LinearLineSegment(IEnumerable<PointF> points)
           : this(points?.Select(x => x.ToVector2()))
        {

        }

        public IEnumerable<Vector2> Simplify()
        {
            return ControlPoints;
        }
    }
}
