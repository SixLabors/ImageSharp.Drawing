// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Opaque native destination with backend capability attachments.
/// </summary>
public sealed class NativeSurface
{
    private readonly ConcurrentDictionary<Type, object> capabilities = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeSurface"/> class.
    /// </summary>
    /// <param name="pixelType">Pixel format information for the destination surface.</param>
    public NativeSurface(PixelTypeInfo pixelType)
        => this.PixelType = pixelType;

    /// <summary>
    /// Gets pixel format information for this destination surface.
    /// </summary>
    public PixelTypeInfo PixelType { get; }

    /// <summary>
    /// Sets or replaces a capability object.
    /// </summary>
    /// <typeparam name="TCapability">Capability type.</typeparam>
    /// <param name="capability">Capability instance.</param>
    public void SetCapability<TCapability>(TCapability capability)
        where TCapability : class
    {
        Guard.NotNull(capability, nameof(capability));
        this.capabilities[typeof(TCapability)] = capability;
    }

    /// <summary>
    /// Attempts to get a capability object by type.
    /// </summary>
    /// <typeparam name="TCapability">Capability type.</typeparam>
    /// <param name="capability">Capability instance when available.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGetCapability<TCapability>([NotNullWhen(true)] out TCapability? capability)
        where TCapability : class
    {
        if (this.capabilities.TryGetValue(typeof(TCapability), out object? value) && value is TCapability typed)
        {
            capability = typed;
            return true;
        }

        capability = null;
        return false;
    }
}
