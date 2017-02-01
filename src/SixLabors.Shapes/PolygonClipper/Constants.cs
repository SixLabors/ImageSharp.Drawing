using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SixLabors.Shapes.PolygonClipper
{
    /// <summary>
    /// Clipper contants
    /// </summary>
    internal static class Constants
    {

        /// <summary>
        /// The unassigned
        /// </summary>
        public const int Unassigned = -1; // InitOptions that can be passed to the constructor ...

        /// <summary>
        /// The skip
        /// </summary>
        public const int Skip = -2;

        /// <summary>
        /// The horizontal delta limit
        /// </summary>
        public const double HorizontalDeltaLimit = -3.4E+38;
    }
}
