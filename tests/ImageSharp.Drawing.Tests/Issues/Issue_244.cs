// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_244
{
    [Fact]
    public void DoesNotHang()
    {
        PathBuilder pathBuilder = new();
        Matrix3x2 transform = Matrix3x2.CreateRotation(-0.04433158f, new Vector2(948, 640));
        pathBuilder.SetTransform(transform);
        pathBuilder.AddQuadraticBezier(new PointF(-2147483648, 677), new PointF(-2147483648, 675), new PointF(-2147483648, 675));
        IPath path = pathBuilder.Build();

        IPath outline = path.GenerateOutline(2);

        Assert.NotEqual(Rectangle.Empty, outline.Bounds);
    }
}
