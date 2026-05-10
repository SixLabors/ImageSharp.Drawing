// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;

/// <summary>
/// A <see cref="TheoryAttribute"/> that skips when WebGPU compute is not available on the current system.
/// </summary>
public class WebGPUTheoryAttribute : TheoryAttribute
{
    public WebGPUTheoryAttribute()
    {
        if (!WebGPUProbe.IsComputeSupported)
        {
            this.Skip = WebGPUProbe.ComputeUnsupportedSkipMessage;
        }
    }
}
