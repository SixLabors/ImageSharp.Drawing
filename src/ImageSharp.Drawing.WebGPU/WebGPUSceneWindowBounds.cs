// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Holds the scratch bounds of the draws that reach one tile-row window.
/// </summary>
internal readonly struct WebGPUSceneWindowBounds
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUSceneWindowBounds"/> struct.
    /// </summary>
    /// <param name="pathRows">The path-row bound.</param>
    /// <param name="tiles">The path-tile bound.</param>
    /// <param name="crossings">The tile-crossing bound.</param>
    /// <param name="ptclWords">The PTCL word bound, without the per-tile chunk allowance.</param>
    public WebGPUSceneWindowBounds(long pathRows, long tiles, long crossings, long ptclWords)
    {
        this.PathRows = pathRows;
        this.Tiles = tiles;
        this.Crossings = crossings;
        this.PtclWords = ptclWords;
    }

    /// <summary>
    /// Gets the path-row bound.
    /// </summary>
    public long PathRows { get; }

    /// <summary>
    /// Gets the path-tile bound.
    /// </summary>
    public long Tiles { get; }

    /// <summary>
    /// Gets the tile-crossing bound.
    /// </summary>
    public long Crossings { get; }

    /// <summary>
    /// Gets the PTCL word bound, without the per-tile chunk allowance.
    /// </summary>
    public long PtclWords { get; }
}
