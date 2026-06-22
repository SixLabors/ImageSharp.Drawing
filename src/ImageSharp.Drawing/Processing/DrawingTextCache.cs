// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Text;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Stores reusable text drawing data shared by one or more drawing canvases.
/// </summary>
public sealed class DrawingTextCache
{
    /// <summary>
    /// The default maximum number of text cache entries.
    /// </summary>
    public const int DefaultCapacity = 16384;

    private readonly Dictionary<RichTextGlyphRenderer.CacheKey, LinkedListNode<Entry>> entries = [];
    private readonly LinkedList<Entry> usage = new();

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
    }

    /// <summary>
    /// Gets the maximum number of text cache entries.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets the number of text cache entries.
    /// </summary>
    public int Count => this.entries.Count;

    /// <summary>
    /// Removes all cached text drawing data.
    /// </summary>
    public void Clear()
    {
        this.entries.Clear();
        this.usage.Clear();
    }

    /// <summary>
    /// Attempts to get cached glyph drawing data for the specified key.
    /// </summary>
    /// <param name="key">The glyph cache key.</param>
    /// <param name="value">The cached glyph drawing data when available.</param>
    /// <returns><see langword="true"/> when cached data exists; otherwise, <see langword="false"/>.</returns>
    internal bool TryGetValue(RichTextGlyphRenderer.CacheKey key, [NotNullWhen(true)] out List<RichTextGlyphRenderer.GlyphRenderData>? value)
    {
        if (!this.entries.TryGetValue(key, out LinkedListNode<Entry>? node))
        {
            value = null;
            return false;
        }

        this.usage.Remove(node);
        this.usage.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>
    /// Gets existing glyph drawing data for the specified key, or creates a new cache entry.
    /// </summary>
    /// <param name="key">The glyph cache key.</param>
    /// <returns>The glyph drawing data associated with <paramref name="key"/>.</returns>
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

        if (this.entries.Count > this.Capacity)
        {
            LinkedListNode<Entry> last = this.usage.Last!;
            this.usage.RemoveLast();
            _ = this.entries.Remove(last.Value.Key);
        }

        return value;
    }

    private readonly struct Entry
    {
        public Entry(RichTextGlyphRenderer.CacheKey key, List<RichTextGlyphRenderer.GlyphRenderData> value)
        {
            this.Key = key;
            this.Value = value;
        }

        public RichTextGlyphRenderer.CacheKey Key { get; }

        public List<RichTextGlyphRenderer.GlyphRenderData> Value { get; }
    }
}
