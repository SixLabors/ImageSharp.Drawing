using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Shaper2D
{
    public interface ILineSegment
    {
        /// <summary>
        /// Simplifies the specified quality.
        /// </summary>
        /// <returns></returns>
        IEnumerable<Vector2> Simplify();
    }
}
