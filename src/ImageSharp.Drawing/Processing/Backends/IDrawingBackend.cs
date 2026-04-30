// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Defines the contract for creating and rendering retained drawing scenes for canvas targets.
/// </summary>
public interface IDrawingBackend
{
    /// <summary>
    /// Creates a retained backend scene from a prepared command batch.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">Destination frame used for target-dependent scene creation.</param>
    /// <param name="commandBatch">Scene commands in submission order.</param>
    /// <param name="ownedResources">Resources that must stay alive for the returned scene.</param>
    /// <returns>A retained backend scene.</returns>
    public DrawingBackendScene CreateScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        DrawingCommandBatch commandBatch,
        IReadOnlyList<IDisposable>? ownedResources = null)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Renders a retained backend scene into the target.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">Active processing configuration.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="scene">The retained backend scene to render.</param>
    public void RenderScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        DrawingBackendScene scene)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Reads source pixels from the target into the destination region.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="sourceRectangle">Source rectangle in target-local coordinates.</param>
    /// <param name="destination">The destination region that receives the copied pixels.</param>
    public void ReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>;
}
