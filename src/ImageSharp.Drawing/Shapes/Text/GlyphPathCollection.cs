// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.ObjectModel;
using System.Numerics;
using SixLabors.Fonts.Rendering;

namespace SixLabors.ImageSharp.Drawing.Shapes.Text;

/// <summary>
/// A geometry + paint container for a single glyph, preserving painted layer boundaries.
/// Use this when you need to render colored (layered) glyphs or to make informed
/// decisions when projecting to monochrome geometry.
/// </summary>
public sealed class GlyphPathCollection
{
    private readonly List<IPath> paths;
    private readonly ReadOnlyCollection<IPath> readOnlyPaths;
    private readonly List<GlyphLayerInfo> layers;
    private readonly ReadOnlyCollection<GlyphLayerInfo> readOnlyLayers;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlyphPathCollection"/> class.
    /// </summary>
    /// <param name="paths">All paths emitted for the glyph in z-order.</param>
    /// <param name="layers">Layer descriptors referring to spans within <paramref name="paths"/>.</param>
    internal GlyphPathCollection(List<IPath> paths, List<GlyphLayerInfo> layers)
    {
        Guard.NotNull(paths, nameof(paths));
        Guard.NotNull(layers, nameof(layers));

        this.paths = paths;
        this.layers = layers;

        this.readOnlyPaths = new ReadOnlyCollection<IPath>(this.paths);
        this.readOnlyLayers = new ReadOnlyCollection<GlyphLayerInfo>(this.layers);
        this.Paths = new PathCollection(this.paths);
    }

    /// <summary>
    /// Gets the flattened geometry for the glyph (all paths in z-order).
    /// This is equivalent to concatenating all layer spans.
    /// </summary>
    public IPathCollection Paths { get; }

    /// <summary>
    /// Gets a read-only view of all individual paths in z-order.
    /// </summary>
    public IReadOnlyList<IPath> PathList => this.readOnlyPaths;

    /// <summary>
    /// Gets a read-only list of layer descriptors preserving paint, fill rule and path spans.
    /// </summary>
    public IReadOnlyList<GlyphLayerInfo> Layers => this.readOnlyLayers;

    /// <summary>
    /// Gets the number of layers.
    /// </summary>
    public int LayerCount => this.layers.Count;

    /// <summary>
    /// Gets an axis-aligned bounding box of the entire glyph in device space.
    /// </summary>
    public RectangleF Bounds => this.Paths.Bounds;

    /// <summary>
    /// Transforms the glyph using the specified matrix.
    /// </summary>
    /// <param name="matrix">The transform matrix.</param>
    /// <returns>
    /// A new <see cref="GlyphPathCollection"/> with the matrix applied to it.
    /// </returns>
    public GlyphPathCollection Transform(Matrix3x2 matrix)
    {
        List<IPath> transformed = new(this.paths.Count);

        for (int i = 0; i < this.paths.Count; i++)
        {
            transformed.Add(this.paths[i].Transform(matrix));
        }

        List<GlyphLayerInfo> transformedLayers = new(this.layers.Count);
        for (int i = 0; i < this.layers.Count; i++)
        {
            transformedLayers.Add(GlyphLayerInfo.Transform(this.layers[i], matrix));
        }

        return new GlyphPathCollection(transformed, transformedLayers);
    }

    /// <summary>
    /// Creates a <see cref="PathCollection"/> containing only the paths from layers that
    /// satisfy <paramref name="predicate"/>. Useful to project to monochrome.
    /// </summary>
    /// <param name="predicate">A filter deciding whether to keep a layer.</param>
    /// <returns>A new <see cref="PathCollection"/> with the selected paths.</returns>
    public PathCollection ToPathCollection(Func<GlyphLayerInfo, bool>? predicate = null)
    {
        List<IPath> kept = [];
        for (int i = 0; i < this.layers.Count; i++)
        {
            GlyphLayerInfo li = this.layers[i];
            if (predicate?.Invoke(li) == false)
            {
                continue;
            }

            int end = li.StartIndex + li.Count;
            for (int p = li.StartIndex; p < end; p++)
            {
                kept.Add(this.paths[p]);
            }
        }

        return new PathCollection(kept);
    }

    /// <summary>
    /// Gets a <see cref="PathCollection"/> view of a single layer's geometry.
    /// </summary>
    /// <param name="layerIndex">The zero-based layer index.</param>
    /// <returns>A path collection comprising only that layer's span.</returns>
    public PathCollection GetLayerPaths(int layerIndex)
    {
        Guard.MustBeLessThan(layerIndex, this.layers.Count, nameof(layerIndex));

        GlyphLayerInfo li = this.layers[layerIndex];
        List<IPath> chunk = new(li.Count);
        int end = li.StartIndex + li.Count;
        for (int p = li.StartIndex; p < end; p++)
        {
            chunk.Add(this.paths[p]);
        }

        return new PathCollection(chunk);
    }

    /// <summary>
    /// Builder used by glyph renderers to populate a <see cref="GlyphPathCollection"/>.
    /// </summary>
    internal sealed class Builder
    {
        private readonly List<IPath> paths = [];
        private readonly List<GlyphLayerInfo> layers = [];

        /// <summary>
        /// Adds a completed path to the collection (current z-order position).
        /// </summary>
        /// <param name="path">The path to add.</param>
        public void AddPath(IPath path) => this.paths.Add(path);

        /// <summary>
        /// Adds a layer descriptor pointing at the most recently added paths.
        /// </summary>
        /// <param name="startIndex">Start index within the path list (inclusive).</param>
        /// <param name="count">Number of paths belonging to this layer.</param>
        /// <param name="paint">The paint for this layer (may be null for default).</param>
        /// <param name="fillRule">The fill rule for this layer.</param>
        /// <param name="bounds">Optional cached bounds for this layer.</param>
        /// <param name="kind">Optional semantic kind (eg. Decoration).</param>
        public void AddLayer(
            int startIndex,
            int count,
            Paint? paint,
            FillRule fillRule,
            RectangleF bounds,
            GlyphLayerKind kind = GlyphLayerKind.Glyph)
        {
            if (startIndex < 0 || count < 0 || startIndex + count > this.paths.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Layer span is out of range of the current path list.");
            }

            this.layers.Add(new GlyphLayerInfo(startIndex, count, paint, fillRule, bounds, kind));
        }

        /// <summary>
        /// Builds the immutable <see cref="GlyphPathCollection"/>.
        /// </summary>
        /// <returns>The collection.</returns>
        public GlyphPathCollection Build() => new(this.paths, this.layers);
    }
}
