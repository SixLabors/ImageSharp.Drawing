// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

internal partial struct WGPUOrigin3D
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WGPUOrigin3D"/> struct.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="z">The Z coordinate.</param>
    public WGPUOrigin3D(uint x, uint y, uint z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
}
