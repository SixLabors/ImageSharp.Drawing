// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// An internal interface for shapes which are backed by <see cref="InternalPath"/>
    /// so we can have a fast path tessellating them.
    /// </summary>
    internal interface IInternalPathOwner
    {
        /// <summary>
        /// Returns the rings as a list of <see cref="InternalPath"/>-s.
        /// </summary>
        /// <returns>Seriously this is a thin internal interface, why should I repeat 3 times what does it do?</returns>
        IReadOnlyList<InternalPath> GetRingsAsInternalPath();
    }
}