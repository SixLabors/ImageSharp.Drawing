// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.Issues;

public class Issue_385
{
    [Theory]
    [InlineData("M 10 80 A 4444444444444444444444444444444444444445 45 0 04445 45 0 0 0 125 125 L 125 80 Z")]
    [InlineData("M 10 80 A 45 455555555555555555555555 55")]
    public void TryParseSvgPath_ReturnsFalseForMalformedArcData(string svgPath)
        => Assert.False(Path.TryParseSvgPath(svgPath, out _));
}
