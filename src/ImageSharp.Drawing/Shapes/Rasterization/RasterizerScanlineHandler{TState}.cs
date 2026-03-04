// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Shapes.Rasterization;

/// <summary>
/// Delegate invoked for each rasterized scanline.
/// </summary>
/// <typeparam name="TState">The caller-provided state type.</typeparam>
/// <param name="y">The destination y coordinate.</param>
/// <param name="scanline">Coverage values for the scanline.</param>
/// <param name="state">Caller-provided mutable state.</param>
internal delegate void RasterizerScanlineHandler<TState>(int y, Span<float> scanline, ref TState state)
    where TState : struct;
