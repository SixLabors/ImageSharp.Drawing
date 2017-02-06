using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace SixLabors.Shapes.Tests
{
    public class ShapeExtentionTests
    {
        private Rectangle bounds;
        private readonly Mock<IShape> mockShape;

        public ShapeExtentionTests()
        {
            this.bounds = new Rectangle(10, 10, 20, 20);
            mockShape = new Mock<IShape>();
            mockShape.Setup(x => x.Bounds).Returns(() => bounds);
        }

        [Fact]
        public void RotateInRadians()
        {
            float angle = (float)Math.PI;

            mockShape.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    var targetMatrix = Matrix3x2.CreateRotation(angle, bounds.Center);

                    Assert.Equal(targetMatrix, m);

                }).Returns(mockShape.Object);

            mockShape.Object.Rotate(angle);

            mockShape.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void RotateInDegrees()
        {
            float angle = 90;

            mockShape.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here
                    var radians = (float)(Math.PI * angle / 180.0);

                    var targetMatrix = Matrix3x2.CreateRotation(radians, bounds.Center);

                    Assert.Equal(targetMatrix, m);

                }).Returns(mockShape.Object);

            mockShape.Object.RotateDegree(angle);

            mockShape.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void TranslateVector()
        {
            Vector2 point = new Vector2(98, 120);

            mockShape.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    var targetMatrix = Matrix3x2.CreateTranslation(point);

                    Assert.Equal(targetMatrix, m);

                }).Returns(mockShape.Object);

            mockShape.Object.Translate(point);

            mockShape.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }

        [Fact]
        public void TranslateXY()
        {
            float x = 76;
            float y = 7;

            mockShape.Setup(p => p.Transform(It.IsAny<Matrix3x2>()))
                .Callback<Matrix3x2>(m =>
                {
                    //validate matrix in here

                    var targetMatrix = Matrix3x2.CreateTranslation(new Vector2(x, y));

                    Assert.Equal(targetMatrix, m);

                }).Returns(mockShape.Object);

            mockShape.Object.Translate(x, y);

            mockShape.Verify(p => p.Transform(It.IsAny<Matrix3x2>()), Times.Once);
        }
    }
}
