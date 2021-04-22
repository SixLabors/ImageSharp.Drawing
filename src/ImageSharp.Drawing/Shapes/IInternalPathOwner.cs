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
        /// Returns the rings as a readonly collection of <see cref="InternalPath"/> elements.
        /// </summary>
        /// <returns>The <see cref="IReadOnlyList{T}"/>.</returns>
        IReadOnlyList<InternalPath> GetRingsAsInternalPath();
    }
}
