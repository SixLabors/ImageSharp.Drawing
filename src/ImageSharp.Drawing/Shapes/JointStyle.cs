// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// The style to apply to the joints when generating an outline.
/// </summary>
public enum JointStyle
{
    /// <summary>
    /// Joints are squared off 1 width distance from the corner.
    /// </summary>
    Square = 0,

    /// <summary>
    /// Rounded joints. Joints generate with a rounded profile.
    /// </summary>
    Round = 1,

    /// <summary>
    /// Joints will generate to a long point unless the end of the point will exceed 4 times the width then we generate the joint using <see cref="Square"/>.
    /// </summary>
    Miter = 2
}

/// <summary>
/// Specifies how the connection between two consecutive line segments (a join)
/// is rendered when stroking paths or polygons.
/// </summary>
internal enum LineJoin
{
    /// <summary>
    /// Joins lines by extending their outer edges until they meet at a sharp corner.
    /// The miter length is limited by the miter limit; if exceeded, the join may fall back to a bevel.
    /// </summary>
    MiterJoin = 0,

    /// <summary>
    /// Joins lines by extending their outer edges to form a miter,
    /// but if the miter length exceeds the miter limit, the join is truncated
    /// at the limit distance rather than falling back to a bevel.
    /// </summary>
    MiterJoinRevert = 1,

    /// <summary>
    /// Joins lines by connecting them with a circular arc centered at the join point,
    /// producing a smooth, rounded corner.
    /// </summary>
    RoundJoin = 2,

    /// <summary>
    /// Joins lines by connecting the outer corners directly with a straight line,
    /// forming a flat edge at the join point.
    /// </summary>
    BevelJoin = 3,

    /// <summary>
    /// Joins lines by forming a miter, but if the miter limit is exceeded,
    /// the join falls back to a round join instead of a bevel.
    /// </summary>
    MiterJoinRound = 4
}

/// <summary>
/// Specifies how inner corners of a stroked path or polygon are rendered
/// when the path turns sharply inward. These settings control how the interior
/// edge of the stroke is joined at such corners.
/// </summary>
internal enum InnerJoin
{
    /// <summary>
    /// Joins inner corners by connecting the edges with a straight line,
    /// producing a flat, beveled appearance.
    /// </summary>
    InnerBevel,

    /// <summary>
    /// Joins inner corners by extending the inner edges until they meet at a sharp point.
    /// This can create long, narrow joins for acute angles.
    /// </summary>
    InnerMiter,

    /// <summary>
    /// Joins inner corners with a notched appearance,
    /// forming a small cut or indentation at the join.
    /// </summary>
    InnerJag,

    /// <summary>
    /// Joins inner corners using a circular arc between the edges,
    /// creating a smooth, rounded interior transition.
    /// </summary>
    InnerRound
}
