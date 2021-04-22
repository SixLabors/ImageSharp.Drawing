// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues
{
    // https://github.com/SixLabors/ImageSharp.Drawing/issues/48
    // index out of range error if zero length ChildPath of a ComplexPolygon
    public class Issues_48
    {
        [Fact]
        public void DoNotThowIfPolygonHasZeroLength()
        {
            var emptyPoly = new ComplexPolygon(new Polygon());
            emptyPoly.FindIntersections(new PointF(0, 0), new PointF(100, 100));
        }
    }
}
