// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using Moq;

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes;

public class PathExtentionTests
{
    private RectangleF bounds;
    private readonly Mock<IPath> mockPath;
    private readonly Mock<IPathCollection> mockPathCollection;

    public PathExtentionTests()
    {
        this.bounds = new RectangleF(10, 10, 20, 20);
        this.mockPath = new Mock<IPath>();
        this.mockPath.Setup(x => x.Bounds).Returns(() => this.bounds);
        this.mockPathCollection = new Mock<IPathCollection>();
        this.mockPathCollection.Setup(x => x.Bounds).Returns(() => this.bounds);
    }

    [Fact]
    public void RotateInRadians()
    {
        const float Angle = (float)Math.PI;

        this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix4x4>()))
            .Callback<Matrix4x4>(m =>
            {
                // validate matrix in here
                Matrix4x4 targetMatrix = new(Matrix3x2.CreateRotation(Angle, RectangleF.Center(this.bounds)));

                Assert.Equal(targetMatrix, m);
            }).Returns(this.mockPath.Object);

        this.mockPath.Object.Rotate(Angle);

        this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix4x4>()), Times.Once);
    }

    [Fact]
    public void RotateInDegrees()
    {
        const float Angle = 90;

        this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix4x4>()))
            .Callback<Matrix4x4>(m =>
            {
                // validate matrix in here
                const float Radians = (float)(Math.PI * Angle / 180.0);

                Matrix4x4 targetMatrix = new(Matrix3x2.CreateRotation(Radians, RectangleF.Center(this.bounds)));

                Assert.Equal(targetMatrix, m);
            }).Returns(this.mockPath.Object);

        this.mockPath.Object.RotateDegree(Angle);

        this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix4x4>()), Times.Once);
    }

    [Fact]
    public void TranslateVector()
    {
        Vector2 point = new(98, 120);

        this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix4x4>()))
            .Callback<Matrix4x4>(m =>
            {
                // validate matrix in here
                Matrix4x4 targetMatrix = Matrix4x4.CreateTranslation(point.X, point.Y, 0);

                Assert.Equal(targetMatrix, m);
            }).Returns(this.mockPath.Object);

        this.mockPath.Object.Translate(point);

        this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix4x4>()), Times.Once);
    }

    [Fact]
    public void TranslateXY()
    {
        const float X = 76;
        const float Y = 7;

        this.mockPath.Setup(p => p.Transform(It.IsAny<Matrix4x4>()))
            .Callback<Matrix4x4>(m =>
            {
                // validate matrix in here
                Matrix4x4 targetMatrix = Matrix4x4.CreateTranslation(X, Y, 0);

                Assert.Equal(targetMatrix, m);
            }).Returns(this.mockPath.Object);

        this.mockPath.Object.Translate(X, Y);

        this.mockPath.Verify(p => p.Transform(It.IsAny<Matrix4x4>()), Times.Once);
    }

    [Fact]
    public void TranslatePoint()
    {
        PointF point = new(98, 120);
        Matrix4x4 targetMatrix = Matrix4x4.CreateTranslation(point.X, point.Y, 0);

        this.VerifyPathTransform(path => path.Translate(point), targetMatrix);
    }

    [Fact]
    public void ScaleXY()
    {
        Matrix4x4 targetMatrix = Matrix4x4.CreateScale(2, 3, 1, new Vector3(RectangleF.Center(this.bounds), 0));

        this.VerifyPathTransform(path => path.Scale(2, 3), targetMatrix);
    }

    [Fact]
    public void ScaleUniform()
    {
        Matrix4x4 targetMatrix = Matrix4x4.CreateScale(4, 4, 1, new Vector3(RectangleF.Center(this.bounds), 0));

        this.VerifyPathTransform(path => path.Scale(4), targetMatrix);
    }

    [Fact]
    public void CollectionRotateInRadians()
    {
        const float Angle = (float)Math.PI;
        Matrix4x4 targetMatrix = new(Matrix3x2.CreateRotation(Angle, RectangleF.Center(this.bounds)));

        this.VerifyPathCollectionTransform(path => path.Rotate(Angle), targetMatrix);
    }

    [Fact]
    public void CollectionRotateInDegrees()
    {
        const float Angle = 90;
        const float Radians = (float)(Math.PI * Angle / 180.0);
        Matrix4x4 targetMatrix = new(Matrix3x2.CreateRotation(Radians, RectangleF.Center(this.bounds)));

        this.VerifyPathCollectionTransform(path => path.RotateDegree(Angle), targetMatrix);
    }

    [Fact]
    public void CollectionTranslatePoint()
    {
        PointF point = new(98, 120);
        Matrix4x4 targetMatrix = Matrix4x4.CreateTranslation(point.X, point.Y, 0);

        this.VerifyPathCollectionTransform(path => path.Translate(point), targetMatrix);
    }

    [Fact]
    public void CollectionTranslateXY()
    {
        const float X = 76;
        const float Y = 7;
        Matrix4x4 targetMatrix = Matrix4x4.CreateTranslation(X, Y, 0);

        this.VerifyPathCollectionTransform(path => path.Translate(X, Y), targetMatrix);
    }

    [Fact]
    public void CollectionScaleXY()
    {
        Matrix4x4 targetMatrix = Matrix4x4.CreateScale(2, 3, 1, new Vector3(RectangleF.Center(this.bounds), 0));

        this.VerifyPathCollectionTransform(path => path.Scale(2, 3), targetMatrix);
    }

    [Fact]
    public void CollectionScaleUniform()
    {
        Matrix4x4 targetMatrix = Matrix4x4.CreateScale(4, 4, 1, new Vector3(RectangleF.Center(this.bounds), 0));

        this.VerifyPathCollectionTransform(path => path.Scale(4), targetMatrix);
    }

    private void VerifyPathTransform(Action<IPath> transform, Matrix4x4 targetMatrix)
    {
        this.mockPath.Setup(x => x.Transform(It.IsAny<Matrix4x4>()))
            .Callback<Matrix4x4>(m => Assert.Equal(targetMatrix, m))
            .Returns(this.mockPath.Object);

        transform(this.mockPath.Object);

        this.mockPath.Verify(x => x.Transform(It.IsAny<Matrix4x4>()), Times.Once);
    }

    private void VerifyPathCollectionTransform(Action<IPathCollection> transform, Matrix4x4 targetMatrix)
    {
        this.mockPathCollection.Setup(x => x.Transform(It.IsAny<Matrix4x4>()))
            .Callback<Matrix4x4>(m => Assert.Equal(targetMatrix, m))
            .Returns(this.mockPathCollection.Object);

        transform(this.mockPathCollection.Object);

        this.mockPathCollection.Verify(x => x.Transform(It.IsAny<Matrix4x4>()), Times.Once);
    }
}
