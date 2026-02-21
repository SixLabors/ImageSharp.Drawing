// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

internal enum WebGPUBrushKind : uint
{
    SolidColor = 0
}

internal readonly struct WebGPUBrushData
{
    public WebGPUBrushData(WebGPUBrushKind kind, Vector4 solidColor)
    {
        this.Kind = kind;
        this.SolidColor = solidColor;
    }

    public WebGPUBrushKind Kind { get; }

    public Vector4 SolidColor { get; }

    public static bool TryCreate(Brush brush, Rectangle brushBounds, out WebGPUBrushData brushData)
    {
        Guard.NotNull(brush, nameof(brush));
        _ = brushBounds;

        if (brush is SolidBrush solidBrush)
        {
            brushData = new WebGPUBrushData(WebGPUBrushKind.SolidColor, solidBrush.Color.ToScaledVector4());
            return true;
        }

        brushData = default;
        return false;
    }
}
