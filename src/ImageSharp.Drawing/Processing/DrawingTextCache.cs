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
/// Two tiers are cached. The glyph cache holds one flattened outline per glyph, keyed by glyph id,
/// size and pen; it is the base layer that avoids re-flattening the same glyph and is shared by
/// every run. The run-path cache is derived from it: the glyphs of a whole uniform run merged into a
/// single positioned path, so that redrawing the run collapses to one composition command instead of
/// one per glyph. The run path is keyed in run-local space, so the same run content drawn at any
/// position, including a fractionally scrolled one, is a hit. The cost is memory, because the merged
/// path holds a copy of each glyph's geometry; far fewer run paths than glyph entries are kept, only
/// whole-run repeats benefit, and a run that differs by a single glyph misses and falls back to the
/// per-glyph commands.
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
    public int Count => this.entries.Count;

    /// <summary>
    /// Gets the reusable drawing-operation scratch list handed to text renderers. Canvases are
    /// per-frame objects while this cache survives across frames, so hosting the scratch here
    /// keeps its capacity instead of regrowing a list of large operation structs every draw.
    /// The list is cleared at the start of each text draw; like the caches on this type it
    /// assumes single-threaded use.
    /// </summary>
    internal List<DrawingOperation> OperationScratch { get; } = [];

    /// <summary>
    /// Gets the reusable render-pass sort buffer used when text operations are lowered to
    /// composition commands, hosted here for the same lifetime reason as
    /// <see cref="OperationScratch"/>. The consuming batcher retains the commands, never this
    /// buffer.
    /// </summary>
    internal List<(byte RenderPass, int Sequence, Backends.CompositionSceneCommand Command)> CommandSortScratch { get; } = [];

    /// <summary>
    /// Removes all cached text drawing data.
    /// </summary>
    public void Clear()
    {
        this.entries.Clear();
        this.usage.Clear();
        this.runPathEntries.Clear();
        this.runPathUsage.Clear();
        this.OperationScratch.Clear();
        this.CommandSortScratch.Clear();
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

    /// <summary>
    /// Gets existing glyph drawing data for the specified key, or creates a new cache entry.
    /// </summary>
    /// <param name="key">The glyph cache key.</param>
    /// <returns>
    /// The glyph drawing data associated with <paramref name="key"/>.
    /// </returns>
    internal List<RichTextGlyphRenderer.GlyphRenderData> GetOrAdd(RichTextGlyphRenderer.CacheKey key)
    {
        if (this.TryGetValue(key, out List<RichTextGlyphRenderer.GlyphRenderData>? value))
        {
            return value;
        }

        value = [];
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

        return value;
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

    /// <summary>
    /// Stores a positioned glyph-run path.
    /// </summary>
    /// <param name="key">The positioned run path key.</param>
    /// <param name="path">The positioned path to cache.</param>
    internal void AddRunPath(RunPathCacheKey key, IPath path)
    {
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
}
