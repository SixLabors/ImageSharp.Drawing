// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal enum WebGpuBrushKind : uint
{
    SolidColor = 0
}

internal readonly struct WebGpuBrushData
{
    public WebGpuBrushData(WebGpuBrushKind kind, Vector4 solidColor)
    {
        this.Kind = kind;
        this.SolidColor = solidColor;
    }

    public WebGpuBrushKind Kind { get; }

    public Vector4 SolidColor { get; }

    public static bool TryCreate(Brush brush, out WebGpuBrushData brushData)
    {
        Guard.NotNull(brush, nameof(brush));

        if (brush is SolidBrush solidBrush)
        {
            brushData = new WebGpuBrushData(WebGpuBrushKind.SolidColor, solidBrush.Color.ToScaledVector4());
            return true;
        }

        brushData = default;
        return false;
    }
}
