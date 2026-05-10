// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Retained scene created by the WebGPU drawing backend.
/// </summary>
public sealed class WebGPUDrawingBackendScene : DrawingBackendScene
{
    // These arenas contain mutable GPU scratch/resource buffers. They are cached on the
    // scene between renders, but every render must rent them into locals before use.
    // The Interlocked.Exchange rent/return methods below make concurrent renders of the
    // same retained scene allocate or use distinct arenas instead of sharing scratch state.
    private WebGPUSceneResourceArena? resourceArena;
    private WebGPUSceneSchedulingArena? schedulingArena;

    // Volatile works on int, so the uint scratch capacities are stored bit-for-bit in
    // signed fields. The values are compared and restored as uint in the accessors.
    // Each counter is monotonic: concurrent renders may race to report usage, but the
    // retained capacity never shrinks below the largest value seen for that counter.
    private int bumpLines;
    private int bumpBinning;
    private int bumpPathRows;
    private int bumpPathTiles;
    private int bumpSegCounts;
    private int bumpSegments;
    private int bumpBlendSpill;
    private int bumpPtcl;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUDrawingBackendScene"/> class.
    /// </summary>
    /// <param name="encodedScene">The retained encoded scene.</param>
    /// <param name="bounds">The target bounds used to encode the scene.</param>
    /// <param name="bumpSizes">The initial scratch capacities for the scene.</param>
    /// <param name="ownedResources">Resources that must stay alive for the retained scene.</param>
    internal WebGPUDrawingBackendScene(
        WebGPUEncodedScene encodedScene,
        Rectangle bounds,
        WebGPUSceneBumpSizes bumpSizes,
        IReadOnlyList<IDisposable>? ownedResources)
        : base(bounds, ownedResources)
    {
        this.EncodedScene = encodedScene;
        this.UpdateBumpSizes(bumpSizes);
    }

    /// <summary>
    /// Gets the retained encoded scene when this is a leaf scene.
    /// </summary>
    internal WebGPUEncodedScene? EncodedScene { get; }

    /// <summary>
    /// Gets the scratch capacities for the scene.
    /// </summary>
    internal WebGPUSceneBumpSizes BumpSizes
        => new(
            unchecked((uint)Volatile.Read(ref this.bumpLines)),
            unchecked((uint)Volatile.Read(ref this.bumpBinning)),
            unchecked((uint)Volatile.Read(ref this.bumpPathRows)),
            unchecked((uint)Volatile.Read(ref this.bumpPathTiles)),
            unchecked((uint)Volatile.Read(ref this.bumpSegCounts)),
            unchecked((uint)Volatile.Read(ref this.bumpSegments)),
            unchecked((uint)Volatile.Read(ref this.bumpBlendSpill)),
            unchecked((uint)Volatile.Read(ref this.bumpPtcl)));

    /// <summary>
    /// Gets or sets the backend that should receive this scene's arenas when the scene is disposed.
    /// </summary>
    internal WebGPUDrawingBackend? ArenaOwner { get; set; }

    /// <summary>
    /// Updates the scratch capacities retained by this scene.
    /// </summary>
    internal void UpdateBumpSizes(WebGPUSceneBumpSizes bumpSizes)
    {
        UpdateBumpSize(ref this.bumpLines, bumpSizes.Lines);
        UpdateBumpSize(ref this.bumpBinning, bumpSizes.Binning);
        UpdateBumpSize(ref this.bumpPathRows, bumpSizes.PathRows);
        UpdateBumpSize(ref this.bumpPathTiles, bumpSizes.PathTiles);
        UpdateBumpSize(ref this.bumpSegCounts, bumpSizes.SegCounts);
        UpdateBumpSize(ref this.bumpSegments, bumpSizes.Segments);
        UpdateBumpSize(ref this.bumpBlendSpill, bumpSizes.BlendSpill);
        UpdateBumpSize(ref this.bumpPtcl, bumpSizes.Ptcl);
    }

    /// <summary>
    /// Rents reusable scene resource buffers for one render.
    /// </summary>
    internal WebGPUSceneResourceArena? RentResourceArena()
        => Interlocked.Exchange(ref this.resourceArena, null);

    /// <summary>
    /// Rents reusable scheduling scratch buffers for one render.
    /// </summary>
    internal WebGPUSceneSchedulingArena? RentSchedulingArena()
        => Interlocked.Exchange(ref this.schedulingArena, null);

    /// <summary>
    /// Returns reusable arenas after one render.
    /// </summary>
    internal void ReturnArenas(
        WebGPUSceneResourceArena? resourceArena,
        WebGPUSceneSchedulingArena? schedulingArena,
        WebGPUDrawingBackend arenaOwner)
    {
        if (resourceArena is null && schedulingArena is null)
        {
            return;
        }

        WebGPUSceneResourceArena? displacedResourceArena = null;
        WebGPUSceneSchedulingArena? displacedSchedulingArena = null;
        this.ArenaOwner = arenaOwner;

        // Return is also an atomic exchange. If two renders complete concurrently, both
        // arenas are valid reuse candidates but only one can remain scene-local; the
        // other is handed to the backend cache so it can still serve eager flushes.
        if (resourceArena is not null)
        {
            displacedResourceArena = Interlocked.Exchange(ref this.resourceArena, resourceArena);
        }

        if (schedulingArena is not null)
        {
            displacedSchedulingArena = Interlocked.Exchange(ref this.schedulingArena, schedulingArena);
        }

        if (displacedResourceArena is not null || displacedSchedulingArena is not null)
        {
            arenaOwner.ReturnArenas(displacedResourceArena, displacedSchedulingArena);
        }
    }

    /// <summary>
    /// Updates one retained scratch-capacity counter without allowing a concurrent render to shrink it.
    /// </summary>
    private static void UpdateBumpSize(ref int target, uint value)
    {
        while (true)
        {
            int current = Volatile.Read(ref target);
            uint currentValue = unchecked((uint)current);

            // Reported scratch usage only ever increases the reusable capacity. Keeping
            // the max avoids a later render repeating an already-discovered grow pass.
            if (value <= currentValue)
            {
                return;
            }

            int replacement = unchecked((int)value);

            // CompareExchange retries only when another render updated this counter
            // between the read and write; the next loop observes that new maximum.
            if (Interlocked.CompareExchange(ref target, replacement, current) == current)
            {
                return;
            }
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        if (this.EncodedScene is not null &&
            !ReferenceEquals(this.EncodedScene, WebGPUEncodedScene.Empty))
        {
            this.EncodedScene.Dispose();
        }

        // Disposal uses the same rent path as rendering so it cannot release an arena
        // currently rented by another render. A scene should still not be disposed while
        // user code intends to keep rendering it, but this prevents cached arena slots
        // from being shared or double-released during ordinary teardown races.
        WebGPUDrawingBackend? arenaOwner = this.ArenaOwner;
        WebGPUSceneResourceArena? resourceArena = this.RentResourceArena();
        WebGPUSceneSchedulingArena? schedulingArena = this.RentSchedulingArena();

        if (arenaOwner is not null)
        {
            arenaOwner.ReturnArenas(resourceArena, schedulingArena);
        }
        else
        {
            WebGPUSceneSchedulingArena.Dispose(schedulingArena);
            WebGPUSceneResourceArena.Dispose(resourceArena);
        }

        this.ArenaOwner = null;
    }
}
