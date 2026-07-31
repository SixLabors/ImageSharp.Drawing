// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Opaque base type for a backend-specific native (non-CPU) drawing target, such as a
/// GPU surface.
/// </summary>
/// <remarks>
/// A canvas frame exposes an instance through <see cref="ICanvasFrame{TPixel}.TryGetNativeSurface"/>.
/// The concrete backend that owns the frame downcasts this to its own surface type; the base
/// type carries no members because its contents are entirely backend-defined.
/// </remarks>
public abstract class NativeSurface
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NativeSurface"/> class.
    /// </summary>
    protected NativeSurface()
    {
    }
}
