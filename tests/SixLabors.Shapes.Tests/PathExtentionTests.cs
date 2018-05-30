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
            this.mockPath = new Mock<IPath>();
            this.mockPath.Setup(x => x.Bounds).Returns(() => this.bounds);
        }

        [Fact]
        public void RotateInRadians()
        {
            float angle = (float)Math.PI;

            this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    Matrix3x2 targetMatrix = Matrix3x2.CreateRotation(angle, RectangleF.Center(this.bounds));

                    Assert.Equal(targetMatrix, m);

                }).Returns(this.mockPath.Object);

            this.mockPath.Object.Rotate(angle);

            this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void RotateInDegrees()
        {
            float angle = 90;

            this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here
                    float radians = (float)(Math.PI * angle / 180.0);

                    Matrix3x2 targetMatrix = Matrix3x2.CreateRotation(radians, RectangleF.Center(this.bounds));

                    Assert.Equal(targetMatrix, m);

                }).Returns(this.mockPath.Object);

            this.mockPath.Object.RotateDegree(angle);

            this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void TranslateVector()
        {
            Vector2 point = new Vector2(98, 120);

            this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    Matrix3x2 targetMatrix = Matrix3x2.CreateTranslation(point);

                    Assert.Equal(targetMatrix, m);

                }).Returns(this.mockPath.Object);

            this.mockPath.Object.Translate(point);

            this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void TranslateXY()
        {
            float x = 76;
            float y = 7;

            this.mockPath.Setup(p => p.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    Matrix3x2 targetMatrix = Matrix3x2.CreateTranslation(new Vector2(x, y));

                    Assert.Equal(targetMatrix, m);

                }).Returns(this.mockPath.Object);

            this.mockPath.Object.Translate(x, y);

            this.mockPath.Verify(p => p.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }
    }
}
