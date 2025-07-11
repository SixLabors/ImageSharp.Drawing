// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using Moq;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

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
        const float Angle = (float)Math.PI;

        this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
            .Callback<Matrix3x2>(m =>
            {
                // validate matrix in here
                Matrix3x2 targetMatrix = Matrix3x2.CreateRotation(Angle, RectangleF.Center(this.bounds));

                Assert.Equal(targetMatrix, m);
            }).Returns(this.mockPath.Object);

        this.mockPath.Object.Rotate(Angle);

        this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
    }

    [Fact]
    public void RotateInDegrees()
    {
        const float Angle = 90;

        this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
            .Callback<Matrix3x2>(m =>
            {
                // validate matrix in here
                const float Radians = (float)(Math.PI * Angle / 180.0);

                Matrix3x2 targetMatrix = Matrix3x2.CreateRotation(Radians, RectangleF.Center(this.bounds));

                Assert.Equal(targetMatrix, m);
            }).Returns(this.mockPath.Object);

        this.mockPath.Object.RotateDegree(Angle);

        this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
    }

    [Fact]
    public void TranslateVector()
    {
        Vector2 point = new(98, 120);

        this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix3x2>()))
            .Callback<Matrix3x2>(m =>
            {
                // validate matrix in here
                Matrix3x2 targetMatrix = Matrix3x2.CreateTranslation(point);

                Assert.Equal(targetMatrix, m);
            }).Returns(this.mockPath.Object);

        this.mockPath.Object.Translate(point);

        this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix3x2>()), Times.Once);
    }

    [Fact]
    public void TranslateXY()
    {
        const float X = 76;
        const float Y = 7;

        this.mockPath.Setup(p => p.Transform(It.IsAny<Matrix3x2>()))
            .Callback<Matrix3x2>(m =>
            {
                // validate matrix in here
                Matrix3x2 targetMatrix = Matrix3x2.CreateTranslation(new Vector2(X, Y));

                Assert.Equal(targetMatrix, m);
            }).Returns(this.mockPath.Object);

        this.mockPath.Object.Translate(X, Y);

        this.mockPath.Verify(p => p.Transform(It.IsAny<Matrix3x2>()), Times.Once);
    }
}
