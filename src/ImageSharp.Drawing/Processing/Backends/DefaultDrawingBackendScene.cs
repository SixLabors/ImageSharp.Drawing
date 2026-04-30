// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Retained scene created by the CPU drawing backend.
/// </summary>
public sealed class DefaultDrawingBackendScene : DrawingBackendScene
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDrawingBackendScene"/> class.
    /// </summary>
    /// <param name="scene">The retained CPU flush scene.</param>
    /// <param name="bounds">The target bounds used to create the scene.</param>
    /// <param name="ownedResources">Resources that must stay alive for the retained scene.</param>
    internal DefaultDrawingBackendScene(
        FlushScene scene,
        Rectangle bounds,
        IReadOnlyList<IDisposable>? ownedResources)
        : base(bounds, ownedResources)
        => this.Scene = scene;

    /// <summary>
    /// Gets the retained CPU flush scene when this is a leaf scene.
    /// </summary>
    internal FlushScene? Scene { get; }

    /// <inheritdoc />
    protected override void DisposeCore()
        => this.Scene?.Dispose();
}
