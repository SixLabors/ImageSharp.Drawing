// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class BrushesTests
{
    public static TheoryData<string> PatternFactoryNames { get; } = new()
    {
        nameof(Brushes.Horizontal),
        nameof(Brushes.Min),
        nameof(Brushes.Vertical),
        nameof(Brushes.ForwardDiagonal),
        nameof(Brushes.BackwardDiagonal),
        nameof(Brushes.Cross),
        nameof(Brushes.DiagonalCross),
    };

    [Fact]
    public void Solid_CreatesBrushWithColor()
    {
        SolidBrush brush = Brushes.Solid(Color.Red);

        Assert.Equal(Color.Red, brush.Color);
    }

    [Theory]
    [MemberData(nameof(PatternFactoryNames))]
    public void PatternFactories_SingleColor_UseTransparentBackground(string name)
    {
        PatternBrush brush = InvokePatternFactory(name, Color.Red, null);

        Assert.Contains(Color.Red, brush.Pattern.Data);
        Assert.Contains(Color.Transparent, brush.Pattern.Data);
    }

    [Theory]
    [MemberData(nameof(PatternFactoryNames))]
    public void PatternFactories_TwoColors_UseBothColors(string name)
    {
        PatternBrush brush = InvokePatternFactory(name, Color.Red, Color.Blue);

        Assert.Contains(Color.Red, brush.Pattern.Data);
        Assert.Contains(Color.Blue, brush.Pattern.Data);
        Assert.DoesNotContain(Color.Transparent, brush.Pattern.Data);
    }

    private static PatternBrush InvokePatternFactory(string name, Color foreColor, Color? backColor)
        => (name, backColor) switch
        {
            (nameof(Brushes.Horizontal), null) => Brushes.Horizontal(foreColor),
            (nameof(Brushes.Horizontal), _) => Brushes.Horizontal(foreColor, backColor.Value),
            (nameof(Brushes.Min), null) => Brushes.Min(foreColor),
            (nameof(Brushes.Min), _) => Brushes.Min(foreColor, backColor.Value),
            (nameof(Brushes.Vertical), null) => Brushes.Vertical(foreColor),
            (nameof(Brushes.Vertical), _) => Brushes.Vertical(foreColor, backColor.Value),
            (nameof(Brushes.ForwardDiagonal), null) => Brushes.ForwardDiagonal(foreColor),
            (nameof(Brushes.ForwardDiagonal), _) => Brushes.ForwardDiagonal(foreColor, backColor.Value),
            (nameof(Brushes.BackwardDiagonal), null) => Brushes.BackwardDiagonal(foreColor),
            (nameof(Brushes.BackwardDiagonal), _) => Brushes.BackwardDiagonal(foreColor, backColor.Value),
            (nameof(Brushes.Cross), null) => Brushes.Cross(foreColor),
            (nameof(Brushes.Cross), _) => Brushes.Cross(foreColor, backColor.Value),
            (nameof(Brushes.DiagonalCross), null) => Brushes.DiagonalCross(foreColor),
            (nameof(Brushes.DiagonalCross), _) => Brushes.DiagonalCross(foreColor, backColor.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
}
