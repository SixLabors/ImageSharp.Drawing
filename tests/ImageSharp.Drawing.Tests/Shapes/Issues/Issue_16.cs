// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    /// <summary>
    /// see https://github.com/issues/16
    /// Also for furter details see https://github.com/SixLabors/Fonts/issues/22
    /// </summary>
    public class Issue_16
    {
        [Fact]
        public void IndexOutoufRangeException()
        {
            var p = new InternalPath(new PointF[] { new Vector2(0, 0), new Vector2(0.000000001f, 0), new Vector2(0, 0.000000001f) }, true);

            IEnumerable<PointF> inter = p.FindIntersections(Vector2.One, Vector2.Zero);

            // if simplified to single point then we should never have an intersection
            Assert.Empty(inter);
        }
    }
}
