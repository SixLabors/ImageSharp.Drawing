// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Numerics;
using Moq;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Paths
{
    public class ShapeRegionTests
    {
        public abstract class MockPath : IPath, IPathInternals
        {
            public abstract RectangleF Bounds { get; }

            public IPath AsClosedPath() => this;

            public abstract SegmentInfo PointAlongPath(float distance);

            public abstract IEnumerable<ISimplePath> Flatten();

            public abstract bool Contains(PointF point);

            public abstract IPath Transform(Matrix3x2 matrix);

            public abstract PathTypes PathType { get; }

            public abstract int MaxIntersections { get; }

            public int TestFindIntersectionsInvocationCounter { get; private set; }

            public virtual int TestYToScan => 10;

            public virtual int TestFindIntersectionsResult => 3;
        }

        private readonly Mock<MockPath> pathMock;

        private readonly RectangleF bounds;

        public ShapeRegionTests()
        {
            this.pathMock = new Mock<MockPath> { CallBase = true };

            this.bounds = new RectangleF(10.5f, 10, 10, 10);
            this.pathMock.Setup(x => x.Bounds).Returns(this.bounds);
        }

        [Fact]
        public void ShapeRegionWithPathRetainsShape()
        {
            var region = new ShapeRegion(this.pathMock.Object);

            Assert.Equal(this.pathMock.Object, region.Shape);
        }

        [Fact]
        public void ShapeRegionFromPathConvertsBoundsProxyToShape()
        {
            var region = new ShapeRegion(this.pathMock.Object);

            Assert.Equal(Math.Floor(this.bounds.Left), region.Bounds.Left);
            Assert.Equal(Math.Ceiling(this.bounds.Right), region.Bounds.Right);

            this.pathMock.Verify(x => x.Bounds);
        }

        [Fact]
        public void ShapeRegionFromShapeConvertsBoundsProxyToShape()
        {
            var region = new ShapeRegion(this.pathMock.Object);

            Assert.Equal(Math.Floor(this.bounds.Left), region.Bounds.Left);
            Assert.Equal(Math.Ceiling(this.bounds.Right), region.Bounds.Right);

            this.pathMock.Verify(x => x.Bounds);
        }
    }
}
