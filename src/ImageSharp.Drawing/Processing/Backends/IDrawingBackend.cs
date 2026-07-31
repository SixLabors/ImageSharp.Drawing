// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Defines the contract a drawing backend implements to turn recorded canvas commands into pixels:
/// creating retained scenes from command batches, rendering those scenes into a canvas frame, and
/// transferring pixels between frames (copy and readback).
/// </summary>
public interface IDrawingBackend
{
    /// <summary>
    /// Creates a backend scene from a prepared command batch.
    /// </summary>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="targetBounds">The target bounds used for target-dependent scene data.</param>
    /// <param name="commandBatch">The scene commands in submission order.</param>
    /// <param name="ownedResources">The resources that must stay alive for the returned scene.</param>
    /// <returns>The created backend scene.</returns>
    public DrawingBackendScene CreateScene(
        Configuration configuration,
        Rectangle targetBounds,
        DrawingCommandBatch commandBatch,
        IReadOnlyList<IDisposable>? ownedResources = null);

    /// <summary>
    /// Renders a backend scene into the target.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="scene">The backend scene to render.</param>
    public void RenderScene<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        DrawingBackendScene scene)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Copies pixels from a source frame into a target frame.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="source">The source frame.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="sourceRectangle">The source rectangle in source-local coordinates.</param>
    /// <param name="targetPoint">The target point in target-local coordinates.</param>
    public void CopyPixels<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> source,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Point targetPoint)
        where TPixel : unmanaged, IPixel<TPixel>;

    /// <summary>
    /// Reads source pixels from the target into the destination region.
    /// </summary>
    /// <typeparam name="TPixel">The pixel format.</typeparam>
    /// <param name="configuration">The active processing configuration.</param>
    /// <param name="target">The target frame.</param>
    /// <param name="sourceRectangle">The source rectangle in target-local coordinates.</param>
    /// <param name="destination">The destination region that receives the copied pixels.</param>
    public void ReadRegion<TPixel>(
        Configuration configuration,
        ICanvasFrame<TPixel> target,
        Rectangle sourceRectangle,
        Buffer2DRegion<TPixel> destination)
        where TPixel : unmanaged, IPixel<TPixel>;
}
