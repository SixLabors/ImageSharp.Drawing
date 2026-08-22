// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

public class DrawingClipStateTests
{
    private static DrawingClipDescriptor Rectangle(float x, float y, float width, float height)
        => DrawingClipDescriptor.CreateRectangle(
            new RectangleF(x, y, width, height),
            ClipOperation.Intersection,
            DrawingClipEdgeMode.Hard);

    [Fact]
    public void Empty_HasNoClips()
    {
        DrawingClipState state = DrawingClipState.Empty;

        Assert.Equal(0, state.Count);
        Assert.False(state.HasClips);
        Assert.False(state.TryGetConservativeBounds(Point.Empty, out _));
        Assert.False(state.TryGetTargetBoundsClip(Point.Empty, out _));
    }

    [Fact]
    public void FromPaths_EmptyList_ReturnsEmpty()
    {
        DrawingClipState state = DrawingClipState.FromPaths([], ClipOperation.Intersection, DrawingClipEdgeMode.Hard);

        Assert.False(state.HasClips);
    }

    [Fact]
    public void FromPaths_MultiplePaths_FormOneOperand()
    {
        IPath[] paths =
        [
            new RectanglePolygon(0, 0, 10, 10),
            new RectanglePolygon(20, 0, 10, 10),
        ];

        DrawingClipState state = DrawingClipState.FromPaths(paths, ClipOperation.Intersection, DrawingClipEdgeMode.Antialiased);

        Assert.Equal(1, state.Count);
        Assert.Equal(2, state.GetDescriptor(0).Paths.Count);
    }

    [Fact]
    public void AppendDescriptor_GrowsThroughInlineSlotsAndOverflow()
    {
        DrawingClipState state = DrawingClipState.Empty;
        for (int i = 0; i < 6; i++)
        {
            state = state.Append(Rectangle(i * 10, 0, 10, 10));

            Assert.Equal(i + 1, state.Count);
            for (int j = 0; j <= i; j++)
            {
                Assert.Equal(new RectangleF(j * 10, 0, 10, 10), state.GetDescriptor(j).Rectangle);
            }
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 2)]
    public void AppendState_PreservesStackOrder(int firstCount, int secondCount)
    {
        DrawingClipState first = DrawingClipState.Empty;
        for (int i = 0; i < firstCount; i++)
        {
            first = first.Append(Rectangle(i * 10, 0, 10, 10));
        }

        DrawingClipState second = DrawingClipState.Empty;
        for (int i = 0; i < secondCount; i++)
        {
            second = second.Append(Rectangle((firstCount + i) * 10, 0, 10, 10));
        }

        DrawingClipState combined = first.Append(second);

        Assert.Equal(firstCount + secondCount, combined.Count);
        for (int i = 0; i < combined.Count; i++)
        {
            Assert.Equal(new RectangleF(i * 10, 0, 10, 10), combined.GetDescriptor(i).Rectangle);
        }
    }

    [Fact]
    public void AppendState_WithEmptyOperands_ReturnsOtherOperand()
    {
        DrawingClipState populated = DrawingClipState.Empty.Append(Rectangle(0, 0, 10, 10));

        Assert.Equal(1, DrawingClipState.Empty.Append(populated).Count);
        Assert.Equal(1, populated.Append(DrawingClipState.Empty).Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Translate_ShiftsEveryDescriptor(int count)
    {
        DrawingClipState state = DrawingClipState.Empty;
        for (int i = 0; i < count; i++)
        {
            state = state.Append(Rectangle(i * 10, 0, 10, 10));
        }

        DrawingClipState translated = state.Translate(new Vector2(5, 7));

        Assert.Equal(count, translated.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(new RectangleF((i * 10) + 5, 7, 10, 10), translated.GetDescriptor(i).Rectangle);
        }
    }

    [Fact]
    public void Translate_ZeroOffset_ReturnsUnchangedState()
    {
        DrawingClipState state = DrawingClipState.Empty.Append(Rectangle(1, 2, 3, 4));
        DrawingClipState translated = state.Translate(Vector2.Zero);

        Assert.Equal(state.GetDescriptor(0).Rectangle, translated.GetDescriptor(0).Rectangle);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void Transform_TranslationMatrix_ShiftsEveryDescriptor(int count)
    {
        DrawingClipState state = DrawingClipState.Empty;
        for (int i = 0; i < count; i++)
        {
            state = state.Append(Rectangle(i * 10, 0, 10, 10));
        }

        DrawingClipState transformed = state.Transform(Matrix4x4.CreateTranslation(5, 7, 0));

        Assert.Equal(count, transformed.Count);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(new RectangleF((i * 10) + 5, 7, 10, 10), transformed.GetDescriptor(i).Rectangle);
        }
    }

    [Fact]
    public void Transform_Identity_ReturnsUnchangedState()
    {
        DrawingClipState state = DrawingClipState.Empty.Append(Rectangle(1, 2, 3, 4));
        DrawingClipState transformed = state.Transform(Matrix4x4.Identity);

        Assert.Equal(state.GetDescriptor(0).Rectangle, transformed.GetDescriptor(0).Rectangle);
    }

    [Fact]
    public void TryGetConservativeBounds_IntersectsIntersectionDescriptors()
    {
        DrawingClipState state = DrawingClipState.Empty
            .Append(Rectangle(0, 0, 20, 20))
            .Append(Rectangle(10, 10, 20, 20));

        Assert.True(state.TryGetConservativeBounds(new Point(100, 200), out Rectangle bounds));
        Assert.Equal(new Rectangle(110, 210, 10, 10), bounds);
    }

    [Fact]
    public void TryGetConservativeBounds_IgnoresDifferenceDescriptors()
    {
        DrawingClipDescriptor difference = DrawingClipDescriptor.CreateRectangle(
            new RectangleF(0, 0, 10, 10),
            ClipOperation.Difference,
            DrawingClipEdgeMode.Hard);
        DrawingClipState state = DrawingClipState.Empty.Append(difference);

        Assert.False(state.TryGetConservativeBounds(Point.Empty, out _));
    }

    [Fact]
    public void TryGetTargetBoundsClip_PixelAlignedSingleRectangle_ReturnsOffsetBounds()
    {
        DrawingClipState state = DrawingClipState.Empty.Append(Rectangle(10, 20, 30, 40));

        Assert.True(state.TryGetTargetBoundsClip(new Point(1, 2), out Rectangle bounds));
        Assert.Equal(new Rectangle(11, 22, 30, 40), bounds);
    }

    [Fact]
    public void TryGetTargetBoundsClip_FractionalRectangle_ReturnsFalse()
    {
        DrawingClipState state = DrawingClipState.Empty.Append(Rectangle(10.5F, 20, 30, 40));

        Assert.False(state.TryGetTargetBoundsClip(Point.Empty, out _));
    }

    [Fact]
    public void TryGetTargetBoundsClip_MultipleDescriptors_ReturnsFalse()
    {
        DrawingClipState state = DrawingClipState.Empty
            .Append(Rectangle(0, 0, 10, 10))
            .Append(Rectangle(0, 0, 10, 10));

        Assert.False(state.TryGetTargetBoundsClip(Point.Empty, out _));
    }

    [Fact]
    public void TryGetTargetBoundsClip_DifferenceOperation_ReturnsFalse()
    {
        DrawingClipDescriptor difference = DrawingClipDescriptor.CreateRectangle(
            new RectangleF(0, 0, 10, 10),
            ClipOperation.Difference,
            DrawingClipEdgeMode.Hard);
        DrawingClipState state = DrawingClipState.Empty.Append(difference);

        Assert.False(state.TryGetTargetBoundsClip(Point.Empty, out _));
    }

    [Fact]
    public void TryGetTargetBoundsClip_SingleRectangleIntegerRegion_ReturnsOffsetBounds()
    {
        DrawingClipDescriptor region = DrawingClipDescriptor.CreateIntegerRegion(
            [new Rectangle(10, 20, 30, 40)],
            ClipOperation.Intersection);
        DrawingClipState state = DrawingClipState.Empty.Append(region);

        Assert.True(state.TryGetTargetBoundsClip(new Point(1, 2), out Rectangle bounds));
        Assert.Equal(new Rectangle(11, 22, 30, 40), bounds);
    }

    [Fact]
    public void TryGetTargetBoundsClip_MultiRectangleIntegerRegion_ReturnsFalse()
    {
        DrawingClipDescriptor region = DrawingClipDescriptor.CreateIntegerRegion(
            [new Rectangle(0, 0, 10, 10), new Rectangle(20, 0, 10, 10)],
            ClipOperation.Intersection);
        DrawingClipState state = DrawingClipState.Empty.Append(region);

        Assert.False(state.TryGetTargetBoundsClip(Point.Empty, out _));
    }
}
