using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SixLabors.Shapes
{
    public enum PathTypes
    {
        /// <summary>
        /// Denotes a path containing a single simple open path
        /// </summary>
        Open,

        /// <summary>
        /// Denotes a path describing a single simple closed shape
        /// </summary>
        Closed,

        /// <summary>
        /// Denotes a path containing one or more child paths that could be open or closed.
        /// </summary>
        Mixed
    }
}
