// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using Avalonia;
using Avalonia.Platform;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;

namespace SixLabors.ImageSharp.Drawing.Renderers.Avalonia;

/// <summary>
/// ImageSharp-backed region implementation for Avalonia clipping and hit testing.
/// </summary>
internal sealed class RegionImpl : IPlatformRenderInterfaceRegion
{
    private readonly Region region = new();
    private readonly List<LtrbPixelRect> rects = [];
    private bool rectsValid = true;

    /// <inheritdoc />
    public bool IsEmpty => this.region.IsEmpty;

    /// <inheritdoc />
    public LtrbPixelRect Bounds
    {
        get
        {
            Rectangle bounds = this.region.Bounds;
            return new()
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Right = bounds.Right,
                Bottom = bounds.Bottom
            };
        }
    }

    /// <inheritdoc />
    public IList<LtrbPixelRect> Rects
    {
        get
        {
            if (this.rectsValid)
            {
                return this.rects;
            }

            this.rects.Clear();
            IReadOnlyList<Rectangle> rectangles = this.region.Rectangles;
            for (int i = 0; i < rectangles.Count; i++)
            {
                Rectangle rectangle = rectangles[i];
                this.rects.Add(new LtrbPixelRect
                {
                    Left = rectangle.Left,
                    Top = rectangle.Top,
                    Right = rectangle.Right,
                    Bottom = rectangle.Bottom
                });
            }

            this.rectsValid = true;
            return this.rects;
        }
    }

    /// <inheritdoc />
    public void AddRect(LtrbPixelRect rect)
    {
        this.region.Add(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
        this.rectsValid = false;
    }

    /// <inheritdoc />
    public void Reset()
    {
        this.region.Clear();
        this.rects.Clear();
        this.rectsValid = true;
    }

    /// <inheritdoc />
    public bool Intersects(LtrbRect rect)
        => this.region.Intersects(
            Rectangle.FromLTRB(
                (int)rect.Left,
                (int)rect.Top,
                (int)Math.Ceiling(rect.Right),
                (int)Math.Ceiling(rect.Bottom)));

    /// <inheritdoc />
    public bool Contains(AvaloniaPoint pt)
        => this.region.Contains((int)pt.X, (int)pt.Y);

    /// <summary>
    /// Converts the current region to an ImageSharp path.
    /// </summary>
    /// <returns>The ImageSharp path representing the region.</returns>
    public IPath ToPath() => this.region.ToPath();

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
