// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents one ordered command range in the canvas replay timeline.
/// </summary>
/// <remarks>
/// Command ranges reference contiguous draw commands. They are not backend scene objects yet.
/// Retained scenes recorded through <see cref="DrawingCanvas.RenderScene"/> are commands inside
/// a range. This type therefore stays compact and keeps the exact order that the canvas recorded.
/// </remarks>
internal readonly struct DrawingCanvasTimelineEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCanvasTimelineEntry"/> struct.
    /// </summary>
    /// <param name="index">The command start index.</param>
    /// <param name="count">The number of commands.</param>
    /// <param name="hasLayers">Indicates whether the command range contains layer boundary commands.</param>
    /// <param name="hasApply">Indicates whether the command range contains apply barriers.</param>
    /// <param name="hasClipControls">Indicates whether the command range contains ordered clip-control commands.</param>
    /// <param name="hasScenes">Indicates whether the command range contains retained scene commands.</param>
    public DrawingCanvasTimelineEntry(
        int index,
        int count,
        bool hasLayers,
        bool hasApply,
        bool hasClipControls,
        bool hasScenes)
    {
        this.Index = index;
        this.Count = count;
        this.HasLayers = hasLayers;
        this.HasApply = hasApply;
        this.HasClipControls = hasClipControls;
        this.HasScenes = hasScenes;
    }

    /// <summary>
    /// Gets the command start index.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the number of commands represented by the entry.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets a value indicating whether the command range contains layer boundary commands.
    /// </summary>
    public bool HasLayers { get; }

    /// <summary>
    /// Gets a value indicating whether the command range contains apply barriers.
    /// </summary>
    public bool HasApply { get; }

    /// <summary>
    /// Gets a value indicating whether the command range contains ordered clip-control commands.
    /// </summary>
    public bool HasClipControls { get; }

    /// <summary>
    /// Gets a value indicating whether the command range contains retained scene commands.
    /// </summary>
    public bool HasScenes { get; }
}
