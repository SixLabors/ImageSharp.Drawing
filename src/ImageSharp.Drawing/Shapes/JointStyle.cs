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
    /// Joints will generate to a long point unless the end of the point will exceed 20 times the width then we generate the joint using <see cref="Square"/>.
    /// </summary>
    Miter = 2
}
