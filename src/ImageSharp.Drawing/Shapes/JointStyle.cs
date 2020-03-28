// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// The style we use to generate the joints when outlining.
    /// </summary>
    public enum JointStyle
    {
        /// <summary>
        /// Joints will generate to a long point unless the end of the point will exceed 20 times the width then we generate the joint using <see cref="JointStyle.Square"/>.
        /// </summary>
        Miter = 2,

        /// <summary>
        /// Rounded joints. Joints generate with a rounded profile.
        /// </summary>
        Round = 1,

        /// <summary>
        /// Joints are squared off 1 width distance from the corner.
        /// </summary>
        Square = 0
    }
}
