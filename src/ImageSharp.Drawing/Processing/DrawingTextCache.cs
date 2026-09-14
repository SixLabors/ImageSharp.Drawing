// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Stores reusable text drawing data shared by one or more drawing canvases.
/// </summary>
/// <remarks>
/// This class is thread-safe. Concurrent drawing operations must use separate canvas instances.
/// </remarks>
public sealed class DrawingTextCache
{
    /// <summary>
    /// The default maximum number of text cache entries.
    /// </summary>
    public const int DefaultCapacity = 16384;

    /// <summary>
    /// Divisor applied to <see cref="Capacity"/> to derive the run-path capacity.
    /// Run paths can retain large combined geometry, so keep fewer of them than
    /// individual glyph entries.
    /// </summary>
    private const int RunPathCapacityDivisor = 4;

    /// <summary>
    /// Protects cache metadata and the scratch pool, never glyph construction or drawing.
    /// </summary>
    private readonly object sync = new();

    /// <summary>
    /// Idle draw buffers. Renting transfers exclusive ownership until the renderer is disposed.
    /// </summary>
    private readonly Stack<DrawingScratch> scratchPool = new();

    // Two tiers are cached. The glyph cache holds reusable outline data keyed by glyph identity,
    // size and pen. It avoids rebuilding glyph outlines and supplies the geometry used by every
    // run. Layered glyphs retain their complete layer and composite-group sequence.
    //
    // The run-path cache is derived from the glyph cache: the glyphs of a whole uniform run are
    // merged into a single positioned path, so redrawing the run requires one composition command
    // instead of one per glyph. The key uses run-local positions, allowing the same run to be
    // reused at different locations, including during fractional scrolling. Only whole-run
    // repeats benefit; a run that differs by one glyph misses and reuses the individual glyph
    // entries to construct its own combined path.
    //
    // Combined paths retain additional geometry, so the run-path cache has a smaller capacity
    // than the glyph cache. This limits memory retention while preserving reuse of individual
    // glyphs across different runs.
    //
    // Both caches are LRU: the dictionary provides O(1) lookup while the linked list
    // tracks usage order (most recently used at the head, eviction from the tail).

    /// <summary>
    /// The base glyph cache: one flattened glyph outline per key, keyed by
    /// <see cref="RichTextGlyphRenderer.CacheKey"/> (glyph id, size and pen) and shared across all
    /// runs. The run-path cache is assembled from these entries.
    /// </summary>
    private readonly Dictionary<RichTextGlyphRenderer.CacheKey, LinkedListNode<Entry>> entries = [];

    /// <summary>
    /// Usage-ordered list of glyph entries; least recently used at the tail.
    /// </summary>
    private readonly LinkedList<Entry> usage = new();

    /// <summary>
    /// The derived run-path cache: a whole uniform run's glyphs merged into one positioned path,
    /// keyed run-locally by <see cref="RunPathCacheKey"/> so a repeat at any position hits. This
    /// collapses a run to a single composition command at the cost of storing the merged geometry.
    /// </summary>
    private readonly Dictionary<RunPathCacheKey, LinkedListNode<RunPathEntry>> runPathEntries = [];

    /// <summary>
    /// Usage-ordered list of run-path entries; least recently used at the tail.
    /// </summary>
    private readonly LinkedList<RunPathEntry> runPathUsage = new();

    /// <summary>
    /// Maximum number of run-path entries, derived from <see cref="Capacity"/>.
    /// </summary>
    private readonly int runPathCapacity;

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingTextCache"/> class.
    /// </summary>
    public DrawingTextCache()
        : this(DefaultCapacity)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingTextCache"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of text cache entries.</param>
    public DrawingTextCache(int capacity)
    {
        Guard.MustBeGreaterThan(capacity, 0, nameof(capacity));

        this.Capacity = capacity;
        this.runPathCapacity = Math.Max(1, capacity / RunPathCapacityDivisor);
    }

    /// <summary>
    /// Gets the maximum number of glyph cache entries. The smaller run-path cache
    /// capacity is derived from this value.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets the number of glyph cache entries. Run-path entries are not included.
    /// </summary>
    public int Count
    {
        get
        {
            lock (this.sync)
            {
                return this.entries.Count;
            }
        }
    }

    /// <summary>
    /// Rents exclusive working buffers for one text draw, retaining capacity across frames.
    /// </summary>
    /// <returns>The working buffers owned by the renderer until it is disposed.</returns>
    internal DrawingScratch RentScratch()
    {
        lock (this.sync)
        {
            if (this.scratchPool.Count > 0)
            {
                return this.scratchPool.Pop();
            }
        }

        return new DrawingScratch();
    }

    /// <summary>
    /// Returns working buffers after all operations have been consumed or the draw has failed.
    /// </summary>
    /// <param name="scratch">The exclusively owned working buffers to return.</param>
    internal void ReturnScratch(DrawingScratch scratch)
    {
        // Drop per-draw references outside the lock while retaining the lists' capacity.
        scratch.Operations.Clear();
        scratch.SortBuffer.Clear();
        scratch.CompositeLayers.Clear();

        lock (this.sync)
        {
            // Match the backend worker pool's bound so a concurrency spike does not retain
            // arbitrarily many large operation buffers for the lifetime of this cache.
            if (this.scratchPool.Count < Environment.ProcessorCount)
            {
                this.scratchPool.Push(scratch);
            }
        }
    }

    /// <summary>
    /// Removes all cached text drawing data.
    /// </summary>
    /// <remarks>
    /// Draws already in progress can continue using previously cached data and can populate
    /// the cache again after this method returns.
    /// </remarks>
    public void Clear()
    {
        lock (this.sync)
        {
            this.entries.Clear();
            this.usage.Clear();
            this.runPathEntries.Clear();
            this.runPathUsage.Clear();

            // Active renderers own their buffers and cached values independently of these
            // indexes. Clearing must not mutate either while a draw is consuming them.
            this.scratchPool.Clear();
        }
    }

    /// <summary>
    /// Attempts to get cached glyph drawing data for the specified key.
    /// </summary>
    /// <param name="key">The glyph cache key.</param>
    /// <param name="value">The cached glyph drawing data when available.</param>
    /// <returns>
    /// <see langword="true"/> when cached data exists; otherwise, <see langword="false"/>.
    /// </returns>
    internal bool TryGetValue(RichTextGlyphRenderer.CacheKey key, [NotNullWhen(true)] out List<RichTextGlyphRenderer.GlyphRenderData>? value)
    {
        lock (this.sync)
        {
            if (!this.entries.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                value = null;
                return false;
            }

            // Move the hit to the head so the least recently used entry stays at the tail.
            this.usage.Remove(node);
            this.usage.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    /// <summary>
    /// Publishes a complete glyph. Ownership of the list transfers to the cache; it must not
    /// be modified after this call, including when another draw has already populated the key.
    /// </summary>
    /// <param name="key">The glyph cache key.</param>
    /// <param name="value">The complete glyph entries in callback order.</param>
    internal void Add(RichTextGlyphRenderer.CacheKey key, List<RichTextGlyphRenderer.GlyphRenderData> value)
    {
        lock (this.sync)
        {
            // Misses build outside the lock. Keep the first complete result if two draws
            // built the same key, rather than combining their layer sequences.
            if (this.entries.ContainsKey(key))
            {
                return;
            }

            LinkedListNode<Entry> node = new(new Entry(key, value));
            this.usage.AddFirst(node);
            this.entries.Add(key, node);

            // Evict the least recently used entry once over capacity.
            if (this.entries.Count > this.Capacity)
            {
                LinkedListNode<Entry> last = this.usage.Last!;
                this.usage.RemoveLast();
                _ = this.entries.Remove(last.Value.Key);
            }
        }
    }

    /// <summary>
    /// Attempts to get a cached positioned glyph-run path.
    /// </summary>
    /// <param name="key">The positioned run path key.</param>
    /// <param name="path">The cached positioned path when available.</param>
    /// <returns>
    /// <see langword="true"/> when cached data exists; otherwise, <see langword="false"/>.
    /// </returns>
    internal bool TryGetRunPath(RunPathCacheKey key, [NotNullWhen(true)] out IPath? path)
    {
        lock (this.sync)
        {
            if (!this.runPathEntries.TryGetValue(key, out LinkedListNode<RunPathEntry>? node))
            {
                path = null;
                return false;
            }

            // Move the hit to the head so the least recently used entry stays at the tail.
            this.runPathUsage.Remove(node);
            this.runPathUsage.AddFirst(node);
            path = node.Value.Path;
            return true;
        }
    }

    /// <summary>
    /// Stores a positioned glyph-run path.
    /// </summary>
    /// <param name="key">The positioned run path key.</param>
    /// <param name="path">The positioned path to cache.</param>
    internal void AddRunPath(RunPathCacheKey key, IPath path)
    {
        // Bounds are lazily stored as a nullable struct by paths. Initialize them before
        // sharing the path so concurrent command creation never races that first write.
        _ = path.Bounds;

        lock (this.sync)
        {
            // Positioned paths are built outside the lock, so concurrent misses may publish
            // the same key. Keep the first completed path in the cache.
            if (this.runPathEntries.ContainsKey(key))
            {
                return;
            }

            LinkedListNode<RunPathEntry> node = new(new RunPathEntry(key, path));
            this.runPathUsage.AddFirst(node);
            this.runPathEntries.Add(key, node);

            // Evict the least recently used entry once over capacity.
            if (this.runPathEntries.Count > this.runPathCapacity)
            {
                LinkedListNode<RunPathEntry> last = this.runPathUsage.Last!;
                this.runPathUsage.RemoveLast();
                _ = this.runPathEntries.Remove(last.Value.Key);
            }
        }
    }

    /// <summary>
    /// A glyph cache entry pairing the key with its render data. The key is stored so
    /// that eviction from the usage list can also remove the dictionary entry.
    /// </summary>
    private readonly struct Entry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> struct.
        /// </summary>
        /// <param name="key">The glyph cache key.</param>
        /// <param name="value">The glyph render data list.</param>
        public Entry(RichTextGlyphRenderer.CacheKey key, List<RichTextGlyphRenderer.GlyphRenderData> value)
        {
            this.Key = key;
            this.Value = value;
        }

        /// <summary>
        /// Gets the glyph cache key.
        /// </summary>
        public RichTextGlyphRenderer.CacheKey Key { get; }

        /// <summary>
        /// Gets the glyph render data list (one entry per glyph layer).
        /// </summary>
        public List<RichTextGlyphRenderer.GlyphRenderData> Value { get; }
    }

    /// <summary>
    /// Identifies a positioned glyph-run path.
    /// </summary>
    internal readonly struct RunPathCacheKey : IEquatable<RunPathCacheKey>
    {
        /// <summary>
        /// The positioned glyph path entries; only the first <see cref="count"/> items belong to the key.
        /// </summary>
        private readonly RunPathCacheEntry[] entries;

        /// <summary>
        /// The number of valid entries in <see cref="entries"/>.
        /// </summary>
        private readonly int count;

        /// <summary>
        /// The precomputed hash of all valid entries.
        /// </summary>
        private readonly int hashCode;

        /// <summary>
        /// Initializes a new instance of the <see cref="RunPathCacheKey"/> struct.
        /// </summary>
        /// <param name="entries">The positioned glyph path entries.</param>
        /// <param name="count">The number of entries that belong to the key.</param>
        public RunPathCacheKey(RunPathCacheEntry[] entries, int count)
        {
            this.entries = entries;
            this.count = count;

            // Precompute the hash once: keys are immutable and hashed on every
            // dictionary lookup, and the cheap hash comparison in Equals lets us
            // skip the per-entry comparison loop for non-matching keys.
            HashCode hash = default;
            for (int i = 0; i < count; i++)
            {
                hash.Add(entries[i]);
            }

            this.hashCode = hash.ToHashCode();
        }

        /// <summary>
        /// Gets the number of glyph path entries in the key.
        /// </summary>
        public int Count => this.count;

        /// <summary>
        /// Determines whether two <see cref="RunPathCacheKey"/> instances are equal.
        /// </summary>
        /// <param name="left">The first key to compare.</param>
        /// <param name="right">The second key to compare.</param>
        /// <returns>
        /// <see langword="true"/> if the keys are equal; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool operator ==(RunPathCacheKey left, RunPathCacheKey right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="RunPathCacheKey"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first key to compare.</param>
        /// <param name="right">The second key to compare.</param>
        /// <returns>
        /// <see langword="true"/> if the keys differ; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool operator !=(RunPathCacheKey left, RunPathCacheKey right) => !(left == right);

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is RunPathCacheKey key && this.Equals(key);

        /// <inheritdoc/>
        public bool Equals(RunPathCacheKey other)
        {
            if (this.hashCode != other.hashCode || this.count != other.count)
            {
                return false;
            }

            for (int i = 0; i < this.count; i++)
            {
                if (this.entries[i] != other.entries[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override int GetHashCode() => this.hashCode;
    }

    /// <summary>
    /// Identifies one positioned glyph path inside a cached run path.
    /// </summary>
    internal readonly struct RunPathCacheEntry : IEquatable<RunPathCacheEntry>
    {
        /// <summary>
        /// The reciprocal of the quantization step applied to <see cref="RelativeLocation"/>
        /// for key comparison. The exact value is used when baking geometry; quantizing only
        /// the comparison absorbs the float noise that absolute-position subtraction introduces,
        /// so rigidly moved runs keep matching. Runs whose exact relative layouts differ by
        /// less than half a step share the first occurrence's geometry.
        /// </summary>
        private const float RelativeLocationAccuracyMultiple = 8F;

        /// <summary>
        /// Initializes a new instance of the <see cref="RunPathCacheEntry"/> struct.
        /// </summary>
        /// <param name="path">The local glyph path.</param>
        /// <param name="relativeLocation">The exact location relative to the run origin, including the fractional component.</param>
        /// <param name="glyphKey">The stable glyph cache key.</param>
        /// <param name="hasGlyphKey">A value indicating whether <paramref name="glyphKey"/> is valid.</param>
        public RunPathCacheEntry(
            IPath path,
            Vector2 relativeLocation,
            RichTextGlyphRenderer.CacheKey glyphKey,
            bool hasGlyphKey)
        {
            this.Path = path;
            this.RelativeLocation = relativeLocation;
            this.GlyphKey = glyphKey;
            this.HasGlyphKey = hasGlyphKey;
        }

        /// <summary>
        /// Gets the local glyph path.
        /// </summary>
        public IPath Path { get; }

        /// <summary>
        /// Gets the exact location relative to the run origin, including the fractional
        /// component. Run-relative locations make the cache key position independent: the same
        /// run content drawn at a different absolute location, even a fractionally scrolled one,
        /// produces the same key.
        /// </summary>
        public Vector2 RelativeLocation { get; }

        /// <summary>
        /// Gets the stable glyph cache key.
        /// </summary>
        public RichTextGlyphRenderer.CacheKey GlyphKey { get; }

        /// <summary>
        /// Gets a value indicating whether <see cref="GlyphKey"/> is valid.
        /// </summary>
        public bool HasGlyphKey { get; }

        /// <summary>
        /// Determines whether two <see cref="RunPathCacheEntry"/> instances are equal.
        /// </summary>
        /// <param name="left">The first entry to compare.</param>
        /// <param name="right">The second entry to compare.</param>
        /// <returns>
        /// <see langword="true"/> if the entries are equal; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool operator ==(RunPathCacheEntry left, RunPathCacheEntry right) => left.Equals(right);

        /// <summary>
        /// Determines whether two <see cref="RunPathCacheEntry"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first entry to compare.</param>
        /// <param name="right">The second entry to compare.</param>
        /// <returns>
        /// <see langword="true"/> if the entries differ; otherwise, <see langword="false"/>.
        /// </returns>
        public static bool operator !=(RunPathCacheEntry left, RunPathCacheEntry right) => !(left == right);

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is RunPathCacheEntry entry && this.Equals(entry);

        /// <inheritdoc/>
        public bool Equals(RunPathCacheEntry other)
        {
            // Prefer the stable glyph key: it matches identical glyphs across separate
            // renders where the IPath instances differ. Without a key (e.g. uncached
            // path-based text) fall back to path reference identity, which only matches
            // within the same render.
            if (this.HasGlyphKey && other.HasGlyphKey)
            {
                return this.GlyphKey.Equals(other.GlyphKey)
                    && QuantizeRelativeLocation(this.RelativeLocation) == QuantizeRelativeLocation(other.RelativeLocation);
            }

            return ReferenceEquals(this.Path, other.Path)
                && QuantizeRelativeLocation(this.RelativeLocation) == QuantizeRelativeLocation(other.RelativeLocation);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
            => this.HasGlyphKey
                ? HashCode.Combine(this.GlyphKey, QuantizeRelativeLocation(this.RelativeLocation))
                : HashCode.Combine(this.Path, QuantizeRelativeLocation(this.RelativeLocation));

        /// <summary>
        /// Quantizes a run-relative location to the key comparison grid.
        /// </summary>
        /// <param name="relativeLocation">The exact run-relative location.</param>
        /// <returns>The location rounded to the comparison grid.</returns>
        private static Vector2 QuantizeRelativeLocation(Vector2 relativeLocation)
            => new(
                MathF.Round(relativeLocation.X * RelativeLocationAccuracyMultiple) / RelativeLocationAccuracyMultiple,
                MathF.Round(relativeLocation.Y * RelativeLocationAccuracyMultiple) / RelativeLocationAccuracyMultiple);
    }

    /// <summary>
    /// A run-path cache entry pairing the key with the combined positioned path. The key
    /// is stored so that eviction from the usage list can also remove the dictionary entry.
    /// </summary>
    private readonly struct RunPathEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RunPathEntry"/> struct.
        /// </summary>
        /// <param name="key">The positioned run path key.</param>
        /// <param name="path">The combined positioned path.</param>
        public RunPathEntry(RunPathCacheKey key, IPath path)
        {
            this.Key = key;
            this.Path = path;
        }

        /// <summary>
        /// Gets the positioned run path key.
        /// </summary>
        public RunPathCacheKey Key { get; }

        /// <summary>
        /// Gets the combined positioned path.
        /// </summary>
        public IPath Path { get; }
    }

    /// <summary>
    /// Working buffers leased to one renderer through operation submission and disposal.
    /// </summary>
    internal sealed class DrawingScratch
    {
        /// <summary>
        /// Gets the drawing operations emitted by this renderer.
        /// </summary>
        public List<DrawingOperation> Operations { get; } = [];

        /// <summary>
        /// Gets the render-pass index buffer, avoiding sorting full operation structs.
        /// </summary>
        public List<(byte RenderPass, int Sequence)> SortBuffer { get; } = [];

        /// <summary>
        /// Gets the stack pairing nested text composite layer commands.
        /// </summary>
        public List<DrawingCanvasLayer> CompositeLayers { get; } = [];
    }
}
