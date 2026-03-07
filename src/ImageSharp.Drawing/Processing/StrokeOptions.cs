// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Provides configuration options for geometric stroke generation.
/// </summary>
public sealed class StrokeOptions : IEquatable<StrokeOptions?>
{
    /// <summary>
    /// Gets or sets the miter limit used to clamp outer miter joins.
    /// </summary>
    public double MiterLimit { get; set; } = 4D;

    /// <summary>
    /// Gets or sets the inner miter limit used to clamp joins on acute interior angles.
    /// </summary>
    public double InnerMiterLimit { get; set; } = 1.01D;

    /// <summary>
    /// Gets or sets the tessellation detail scale for round joins and round caps.
    /// Higher values produce more vertices (smoother curves, more work).
    /// Lower values produce fewer vertices.
    /// </summary>
    public double ArcDetailScale { get; set; } = 1D;

    /// <summary>
    /// Gets or sets the outer line join style used for stroking corners.
    /// </summary>
    public LineJoin LineJoin { get; set; } = LineJoin.Bevel;

    /// <summary>
    /// Gets or sets the line cap style used for open path ends.
    /// </summary>
    public LineCap LineCap { get; set; } = LineCap.Butt;

    /// <summary>
    /// Gets or sets the join style used for sharp interior angles.
    /// </summary>
    public InnerJoin InnerJoin { get; set; } = InnerJoin.Miter;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => this.Equals(obj as StrokeOptions);

    /// <inheritdoc/>
    public bool Equals(StrokeOptions? other)
        => other is not null &&
           this.MiterLimit == other.MiterLimit &&
           this.InnerMiterLimit == other.InnerMiterLimit &&
           this.ArcDetailScale == other.ArcDetailScale &&
           this.LineJoin == other.LineJoin &&
           this.LineCap == other.LineCap &&
           this.InnerJoin == other.InnerJoin;

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            this.MiterLimit,
            this.InnerMiterLimit,
            this.ArcDetailScale,
            this.LineJoin,
            this.LineCap,
            this.InnerJoin);
}
