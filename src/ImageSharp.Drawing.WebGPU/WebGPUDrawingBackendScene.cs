// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Retained scene created by the WebGPU drawing backend.
/// </summary>
/// <remarks>
/// A retained scene can be rendered multiple times, including concurrently. The scene caches
/// reusable GPU arenas and monotonically growing scratch capacities between renders so repeat
/// renders skip the dynamic-growth discovery passes.
/// </remarks>
public sealed class WebGPUDrawingBackendScene : DrawingBackendScene
{
    // These arenas contain mutable GPU scratch/resource buffers. They are cached on the
    // scene between renders, but every render must rent them into locals before use.
    // The Interlocked.Exchange rent/return methods below make concurrent renders of the
    // same retained scene allocate or use distinct arenas instead of sharing scratch state.
    private WebGPUSceneResourceArena? resourceArena;
    private WebGPUSceneSchedulingArena? schedulingArena;

    // Deferred-render references keep the encoded GPU payload alive past user disposal while
    // the backend may still need it for a corrective re-render (a deferred overflow readback
    // is outstanding). Teardown runs when disposal has been requested and the last reference
    // releases; the tornDown guard makes the actual teardown single-shot under races.
    private int deferredRenderReferences;
    private int teardownRequested;
    private int tornDown;

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
    /// Gets the retained encoded scene.
    /// </summary>
    internal WebGPUEncodedScene EncodedScene { get; }

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
    /// Updates the scratch capacities retained by this scene. Each counter only ever grows;
    /// values smaller than the current retained maximum are ignored.
    /// </summary>
    /// <param name="bumpSizes">The scratch capacities observed by a completed render.</param>
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
    /// Adds a deferred-render reference that keeps this scene's encoded GPU payload alive past
    /// user disposal, so a corrective re-render for an outstanding deferred overflow readback
    /// can still replay it.
    /// </summary>
    internal void AddDeferredRenderReference()
        => Interlocked.Increment(ref this.deferredRenderReferences);

    /// <summary>
    /// Releases a deferred-render reference. When disposal has already been requested and this
    /// was the last reference, the actual teardown runs now.
    /// </summary>
    internal void ReleaseDeferredRenderReference()
    {
        if (Interlocked.Decrement(ref this.deferredRenderReferences) == 0
            && Volatile.Read(ref this.teardownRequested) == 1)
        {
            this.TeardownCore();
        }
    }

    /// <summary>
    /// Rents reusable scene resource buffers for one render, leaving the scene slot empty.
    /// </summary>
    /// <returns>The cached arena, or <see langword="null"/> when the slot is empty.</returns>
    internal WebGPUSceneResourceArena? RentResourceArena()
        => Interlocked.Exchange(ref this.resourceArena, null);

    /// <summary>
    /// Rents reusable scheduling scratch buffers for one render, leaving the scene slot empty.
    /// </summary>
    /// <returns>The cached arena, or <see langword="null"/> when the slot is empty.</returns>
    internal WebGPUSceneSchedulingArena? RentSchedulingArena()
        => Interlocked.Exchange(ref this.schedulingArena, null);

    /// <summary>
    /// Returns reusable arenas after one render.
    /// </summary>
    /// <param name="resourceArena">The scene resource arena to return, or <see langword="null"/> when none was rented.</param>
    /// <param name="schedulingArena">The scheduling arena to return, or <see langword="null"/> when none was rented.</param>
    /// <param name="arenaOwner">The backend that receives displaced arenas and this scene's arenas on disposal.</param>
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

        // A return that races scene disposal would strand the arenas on a dead scene and
        // starve the backend cache into recreating GPU buffers every flush; pull them back
        // out and hand them to the backend instead.
        if (Volatile.Read(ref this.teardownRequested) == 1)
        {
            this.ReleaseCachedArenas();
        }
    }

    /// <summary>
    /// Updates one retained scratch-capacity counter without allowing a concurrent render to shrink it.
    /// </summary>
    /// <param name="target">The counter field storing the uint capacity bit-for-bit as int.</param>
    /// <param name="value">The newly observed capacity.</param>
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
        Volatile.Write(ref this.teardownRequested, 1);

        // Cached arenas return to the backend immediately even when encoded-payload teardown
        // is deferred: corrective re-renders rent fresh arenas, and keeping these parked on a
        // disposed scene starves the backend cache into recreating GPU buffers every flush.
        this.ReleaseCachedArenas();

        // Encoded-payload teardown is deferred while the backend still holds deferred-render
        // references for outstanding overflow readbacks; the last release runs it instead.
        if (Volatile.Read(ref this.deferredRenderReferences) == 0)
        {
            this.TeardownCore();
        }
    }

    /// <summary>
    /// Returns any cached arenas to the backend cache, or disposes them when no backend has
    /// been recorded. Uses the same rent path as rendering so it cannot release an arena
    /// currently rented by another render, and is safe to call repeatedly.
    /// </summary>
    private void ReleaseCachedArenas()
    {
        WebGPUDrawingBackend? arenaOwner = this.ArenaOwner;
        WebGPUSceneResourceArena? resourceArena = this.RentResourceArena();
        WebGPUSceneSchedulingArena? schedulingArena = this.RentSchedulingArena();

        if (resourceArena is null && schedulingArena is null)
        {
            return;
        }

        if (arenaOwner is not null)
        {
            arenaOwner.ReturnArenas(resourceArena, schedulingArena);
        }
        else
        {
            WebGPUSceneSchedulingArena.Dispose(schedulingArena);
            WebGPUSceneResourceArena.Dispose(resourceArena);
        }
    }

    /// <summary>
    /// Releases the encoded GPU payload exactly once, after both disposal has been requested
    /// and no deferred-render references remain. Also sweeps arenas a corrective re-render
    /// may have parked after disposal returned the original set.
    /// </summary>
    private void TeardownCore()
    {
        // Disposal request and reference release can race to this point; only one wins.
        if (Interlocked.Exchange(ref this.tornDown, 1) == 1)
        {
            return;
        }

        if (!ReferenceEquals(this.EncodedScene, WebGPUEncodedScene.Empty))
        {
            this.EncodedScene.Dispose();
        }

        this.ReleaseCachedArenas();
        this.ArenaOwner = null;
    }
}
