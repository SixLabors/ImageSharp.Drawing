// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.Drawing
{
    internal interface IInternalPathOwner
    {
        IReadOnlyList<InternalPath> GetRingsAsInternalPath();
    }
}