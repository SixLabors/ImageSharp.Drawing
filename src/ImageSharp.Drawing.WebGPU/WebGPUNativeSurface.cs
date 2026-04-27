// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// WebGPU native drawing target exposed through the backend-agnostic <see cref="NativeSurface"/> contract.
/// </summary>
internal sealed class WebGPUNativeSurface : NativeSurface
{
    private readonly WebGPUSurfaceCapability capability;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUNativeSurface"/> class.
    /// </summary>
    /// <param name="capability">The WebGPU capability exposed by this surface.</param>
    internal WebGPUNativeSurface(WebGPUSurfaceCapability capability)
        => this.capability = capability;

    /// <inheritdoc />
    public override bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class
    {
        if (this.capability is TCapability typed)
        {
            capability = typed;
            return true;
        }

        capability = null;
        return false;
    }
}
