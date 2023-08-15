// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Tests;

/// <summary>
/// see https://github.com/issues/19
/// Also for furter details see https://github.com/SixLabors/Fonts/issues/22
/// </summary>
public class Issue_19
{
    [Fact]
    public void PAthLoosingSelfIntersectingPoint()
    {
        var line1 = new PointF[] { new Vector2(117f, 199f), new Vector2(31f, 210f), new Vector2(35f, 191f), new Vector2(117f, 199f), new Vector2(2f, 9f) };
        var path = new Path(new LinearLineSegment(line1));
        IReadOnlyList<PointF> pathPoints = path.Flatten().First().Points.ToArray();

        // all points must not be in the outline;
        foreach (PointF v in line1)
        {
            Assert.Contains(v, pathPoints);
        }
    }

    [Fact]
    public void InternalPathLoosingSelfIntersectingPoint()
    {
        var line1 = new PointF[] { new Vector2(117f, 199f), new Vector2(31f, 210f), new Vector2(35f, 191f), new Vector2(117f, 199f), new Vector2(2f, 9f) };
        var path = new InternalPath(new LinearLineSegment(line1), false);
        IReadOnlyList<PointF> pathPoints = path.Points().ToArray();

        // all points must not be in the outline;
        foreach (PointF v in line1)
        {
            Assert.Contains(v, pathPoints);
        }
    }
}
