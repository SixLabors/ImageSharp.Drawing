using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Moq;
using Xunit;
using SixLabors.Primitives;

namespace SixLabors.Shapes.Tests
{
    public class PathExtentionTests
    {
        private RectangleF bounds;
        private readonly Mock<IPath> mockPath;

        public PathExtentionTests()
        {
            this.bounds = new RectangleF(10, 10, 20, 20);
            mockPath = new Mock<IPath>();
            mockPath.Setup(x => x.Bounds).Returns(() => bounds);
        }

        [Fact]
        public void RotateInRadians()
        {
            float angle = (float)Math.PI;

            mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    Matrix3x2 targetMatrix = Matrix3x2.CreateRotation(angle, RectangleF.Center(bounds));

                    Assert.Equal(targetMatrix, m);

                }).Returns(mockPath.Object);

            mockPath.Object.Rotate(angle);

            mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void RotateInDegrees()
        {
            float angle = 90;

            mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here
                    float radians = (float)(Math.PI * angle / 180.0);

                    Matrix3x2 targetMatrix = Matrix3x2.CreateRotation(radians, RectangleF.Center(bounds));

                    Assert.Equal(targetMatrix, m);

                }).Returns(mockPath.Object);

            mockPath.Object.RotateDegree(angle);

            mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void TranslateVector()
        {
            Vector2 point = new Vector2(98, 120);

            mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    Matrix3x2 targetMatrix = Matrix3x2.CreateTranslation(point);

                    Assert.Equal(targetMatrix, m);

                }).Returns(mockPath.Object);

            mockPath.Object.Translate(point);

            mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void TranslateXY()
        {
            float x = 76;
            float y = 7;

            mockPath.Setup(p => p.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    Matrix3x2 targetMatrix = Matrix3x2.CreateTranslation(new Vector2(x, y));

                    Assert.Equal(targetMatrix, m);

                }).Returns(mockPath.Object);

            mockPath.Object.Translate(x, y);

            mockPath.Verify(p => p.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }
    }
}
