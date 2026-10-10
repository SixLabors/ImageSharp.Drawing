// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <inheritdoc cref="PolygonClipper.StrokeOptions" />
public sealed class StrokeOptions : IEquatable<StrokeOptions?>
{
    private double arcDetailScale = 1D;

    /// <inheritdoc cref="PolygonClipper.StrokeOptions.MiterLimit" />
    public double MiterLimit { get; set; } = 4D;

    /// <inheritdoc cref="PolygonClipper.StrokeOptions.ArcDetailScale" />
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than zero.</exception>
    public double ArcDetailScale
    {
        get => this.arcDetailScale;
        set
        {
            Guard.MustBeGreaterThan(value, 0, nameof(this.ArcDetailScale));
            this.arcDetailScale = value;
        }
    }

    /// <inheritdoc cref="PolygonClipper.StrokeOptions.LineJoin" />
    public LineJoin LineJoin { get; set; } = LineJoin.Bevel;

    /// <inheritdoc cref="PolygonClipper.StrokeOptions.LineCap" />
    public LineCap LineCap { get; set; } = LineCap.Butt;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as StrokeOptions);

    /// <inheritdoc/>
    public bool Equals(StrokeOptions? other)
        => other is not null &&
           this.MiterLimit == other.MiterLimit &&
           this.ArcDetailScale == other.ArcDetailScale &&
           this.LineJoin == other.LineJoin &&
           this.LineCap == other.LineCap;

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            this.MiterLimit,
            this.ArcDetailScale,
            this.LineJoin,
            this.LineCap);
}
