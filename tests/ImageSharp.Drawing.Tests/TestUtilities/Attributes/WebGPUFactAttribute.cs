// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities.Attributes;

/// <summary>
/// A <see cref="FactAttribute"/> that skips when WebGPU is not available on the current system.
/// </summary>
public class WebGPUFactAttribute : FactAttribute
{
    public WebGPUFactAttribute()
    {
        using WebGPUDrawingBackend backend = new();
        if (!backend.IsSupported)
        {
            this.Skip = "WebGPU is not available on this system.";
        }
    }
}

/// <summary>
/// A <see cref="TheoryAttribute"/> that skips when WebGPU is not available on the current system.
/// </summary>
public class WebGPUTheoryAttribute : TheoryAttribute
{
    public WebGPUTheoryAttribute()
    {
        using WebGPUDrawingBackend backend = new();
        if (!backend.IsSupported)
        {
            this.Skip = "WebGPU is not available on this system.";
        }
    }
}
