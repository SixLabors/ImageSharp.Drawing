// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Represents a backend-specific native drawing target.
/// </summary>
public abstract class NativeSurface
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeSurface"/> class.
    /// </summary>
    protected NativeSurface()
    {
    }

    /// <summary>
    /// Gets a backend-specific native target by type.
    /// </summary>
    /// <typeparam name="TNativeTarget">Native target type.</typeparam>
    /// <returns>The native target.</returns>
    public abstract TNativeTarget GetNativeTarget<TNativeTarget>()
        where TNativeTarget : class;
}
