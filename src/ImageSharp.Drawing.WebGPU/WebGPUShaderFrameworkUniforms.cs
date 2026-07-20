// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Maps effect-local coordinates to the current source texture and its valid captured region.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct WebGPUShaderFrameworkUniforms
{
    private readonly int sourceX;
    private readonly int sourceY;
    private readonly int validMinimumX;
    private readonly int validMinimumY;
    private readonly int validMaximumX;
    private readonly int validMaximumY;
    private readonly int inputWidth;
    private readonly int inputHeight;

    /// <summary>
    /// The byte size required by the matching WGSL uniform structure.
    /// </summary>
    public const ulong ByteLength = 32;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderFrameworkUniforms"/> struct.
    /// </summary>
    /// <param name="sourceOrigin">The source texture coordinate corresponding to effect-local zero.</param>
    /// <param name="validMinimum">The inclusive minimum valid effect-local coordinate.</param>
    /// <param name="validMaximum">The exclusive maximum valid effect-local coordinate.</param>
    /// <param name="inputSize">The complete effect input size.</param>
    public WebGPUShaderFrameworkUniforms(Point sourceOrigin, Point validMinimum, Point validMaximum, Size inputSize)
    {
        this.sourceX = sourceOrigin.X;
        this.sourceY = sourceOrigin.Y;
        this.validMinimumX = validMinimum.X;
        this.validMinimumY = validMinimum.Y;
        this.validMaximumX = validMaximum.X;
        this.validMaximumY = validMaximum.Y;
        this.inputWidth = inputSize.Width;
        this.inputHeight = inputSize.Height;
    }

    /// <summary>
    /// Gets the source texture X coordinate corresponding to effect-local zero.
    /// </summary>
    public int SourceX => this.sourceX;

    /// <summary>
    /// Gets the source texture Y coordinate corresponding to effect-local zero.
    /// </summary>
    public int SourceY => this.sourceY;

    /// <summary>
    /// Gets the inclusive valid local X coordinate.
    /// </summary>
    public int ValidMinimumX => this.validMinimumX;

    /// <summary>
    /// Gets the inclusive valid local Y coordinate.
    /// </summary>
    public int ValidMinimumY => this.validMinimumY;

    /// <summary>
    /// Gets the exclusive valid local X coordinate.
    /// </summary>
    public int ValidMaximumX => this.validMaximumX;

    /// <summary>
    /// Gets the exclusive valid local Y coordinate.
    /// </summary>
    public int ValidMaximumY => this.validMaximumY;

    /// <summary>
    /// Gets the complete effect input width.
    /// </summary>
    public int InputWidth => this.inputWidth;

    /// <summary>
    /// Gets the complete effect input height.
    /// </summary>
    public int InputHeight => this.inputHeight;
}
