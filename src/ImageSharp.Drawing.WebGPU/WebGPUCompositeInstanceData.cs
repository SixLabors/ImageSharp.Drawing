// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

[StructLayout(LayoutKind.Sequential)]
internal struct WebGPUCompositeInstanceData
{
    public uint SourceOffsetX;
    public uint SourceOffsetY;
    public uint DestinationX;
    public uint DestinationY;
    public uint DestinationWidth;
    public uint DestinationHeight;
    public uint TargetWidth;
    public uint TargetHeight;
    public uint BrushKind;
    public uint Padding0;
    public uint Padding1;
    public uint Padding2;
    public Vector4 SolidBrushColor;
    public float BlendPercentage;
    public float Padding3;
    public float Padding4;
    public float Padding5;
}
