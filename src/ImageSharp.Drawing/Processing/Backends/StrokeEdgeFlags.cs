// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Bit flags identifying the type of a stroke edge descriptor.
/// Values match the WGSL shader constants in <c>StrokeExpandComputeShader</c>.
/// </summary>
[Flags]
public enum StrokeEdgeFlags
{
    /// <summary>
    /// Side edge: <c>(X0,Y0)→(X1,Y1)</c> is a centerline segment.
    /// </summary>
    None = 0,

    /// <summary>
    /// Join at a contour vertex.
    /// <c>(X0,Y0)</c> is the vertex, <c>(X1,Y1)</c> is the previous endpoint,
    /// <c>(AdjX,AdjY)</c> is the next endpoint.
    /// </summary>
    Join = 32,

    /// <summary>
    /// Start cap on an open contour.
    /// <c>(X0,Y0)</c> is the cap vertex, <c>(X1,Y1)</c> is the adjacent endpoint.
    /// </summary>
    CapStart = 64,

    /// <summary>
    /// End cap on an open contour.
    /// <c>(X0,Y0)</c> is the cap vertex, <c>(X1,Y1)</c> is the adjacent endpoint.
    /// </summary>
    CapEnd = 128,
}
