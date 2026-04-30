// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Base type for retained drawing backend scenes.
/// </summary>
public abstract class DrawingBackendScene : IDisposable
{
    private readonly IReadOnlyList<IDisposable>? ownedResources;
    private bool isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingBackendScene"/> class.
    /// </summary>
    /// <param name="bounds">The target bounds used to create the scene.</param>
    /// <param name="ownedResources">Resources that must stay alive for the retained scene.</param>
    protected DrawingBackendScene(
        Rectangle bounds,
        IReadOnlyList<IDisposable>? ownedResources)
    {
        this.Bounds = bounds;
        this.ownedResources = ownedResources;
    }

    /// <summary>
    /// Gets the target bounds used to create the scene.
    /// </summary>
    public Rectangle Bounds { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.DisposeCore();
        this.DisposeOwnedResources();
        this.isDisposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes backend-specific resources retained by this scene.
    /// </summary>
    protected virtual void DisposeCore()
    {
    }

    /// <summary>
    /// Disposes resources retained for image-brush commands in this scene.
    /// </summary>
    private void DisposeOwnedResources()
    {
        if (this.ownedResources is null)
        {
            return;
        }

        for (int i = 0; i < this.ownedResources.Count; i++)
        {
            this.ownedResources[i].Dispose();
        }
    }
}
