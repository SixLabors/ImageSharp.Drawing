using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SixLabors.Shapes.DrawShapesWithImageSharp
{
    internal static class RectangleExtensions
    {
        /// <summary>
        /// Converts a Shaper2D <see cref="SixLabors.Shapes.Rectangle"/> to an ImageSharp <see cref="Rectangle"/> by creating a <see cref="Rectangle"/> the entirely surrounds the source.
        /// </summary>
        /// <param name="source">The image this method extends.</param>
        /// <returns>A <see cref="Rectangle"/> representation of this <see cref="SixLabors.Shapes.Rectangle"/></returns>
        public static ImageSharp.Rectangle Convert(this SixLabors.Shapes.Rectangle source)
        {
            int left = (int)Math.Floor(source.Left);
            int right = (int)Math.Ceiling(source.Right);
            int top = (int)Math.Floor(source.Top);
            int bottom = (int)Math.Ceiling(source.Bottom);
            return new ImageSharp.Rectangle(left, top, right - left, bottom - top);
        }
    }
}
