// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing
{
    internal static class DrawingHelpers
    {
        /// <summary>
        /// Convert a <see cref="DenseMatrix{Color}"/> to a <see cref="DenseMatrix{T}"/> of the given pixel type.
        /// </summary>
        public static DenseMatrix<TPixel> ToPixelMatrix<TPixel>(this DenseMatrix<Color> colorMatrix)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            var result = new DenseMatrix<TPixel>(colorMatrix.Columns, colorMatrix.Rows);
            Color.ToPixel(colorMatrix.Span, result.Span);
            return result;
        }
    }
}
