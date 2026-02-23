// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

[StructLayout(LayoutKind.Sequential)]
internal struct WebGPUCompositeInstanceData
{
    public int SourceOffsetX;
    public int SourceOffsetY;
    public int DestinationX;
    public int DestinationY;
    public int DestinationWidth;
    public int DestinationHeight;
    public int TargetWidth;
    public int TargetHeight;
    public int ImageRegionX;
    public int ImageRegionY;
    public int ImageRegionWidth;
    public int ImageRegionHeight;
    public int ImageBrushOriginX;
    public int ImageBrushOriginY;
    public int Padding0;
    public int Padding1;
    public Vector4 SolidBrushColor;
    public Vector4 BlendData;
}
