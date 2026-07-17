// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

internal partial struct WGPUExtent3D
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WGPUExtent3D"/> struct.
    /// </summary>
    /// <param name="width">The texture width.</param>
    /// <param name="height">The texture height.</param>
    /// <param name="depthOrArrayLayers">The texture depth or array-layer count.</param>
    public WGPUExtent3D(uint width, uint height, uint depthOrArrayLayers)
    {
        this.width = width;
        this.height = height;
        this.depthOrArrayLayers = depthOrArrayLayers;
    }
}
