// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;

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
    /// Attempts to get a backend capability by type.
    /// </summary>
    /// <typeparam name="TCapability">Capability type.</typeparam>
    /// <param name="capability">Capability instance when available.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public abstract bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class;
}
