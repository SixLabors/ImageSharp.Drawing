// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Identifies the kind of replay item stored in a drawing canvas timeline.
/// </summary>
internal enum DrawingCanvasTimelineEntryKind
{
    /// <summary>
    /// A contiguous range of draw commands.
    /// </summary>
    CommandRange,

    /// <summary>
    /// An apply barrier.
    /// </summary>
    ApplyBarrier,

    /// <summary>
    /// An existing retained scene recorded through <see cref="DrawingCanvas.RenderScene"/>.
    /// </summary>
    Scene
}

/// <summary>
/// Represents one ordered item in the canvas replay timeline.
/// </summary>
/// <remarks>
/// Command ranges reference contiguous draw commands; they are not backend scene objects yet.
/// Apply barriers and retained scene references point into side buffers by index, keeping this
/// type compact while preserving the exact order in which the canvas recorded replay work.
/// </remarks>
internal readonly struct DrawingCanvasTimelineEntry
{
    private DrawingCanvasTimelineEntry(
        DrawingCanvasTimelineEntryKind kind,
        int index,
        int count,
        bool hasLayers)
    {
        this.Kind = kind;
        this.Index = index;
        this.Count = count;
        this.HasLayers = hasLayers;
    }

    /// <summary>
    /// Gets the kind of replay item represented by this entry.
    /// </summary>
    public DrawingCanvasTimelineEntryKind Kind { get; }

    /// <summary>
    /// Gets the command start index for command ranges, or the side-buffer index for barriers and scenes.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the number of commands represented by a command-range entry.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets a value indicating whether the command range contains layer boundary commands.
    /// </summary>
    public bool HasLayers { get; }

    /// <summary>
    /// Creates a command-range entry.
    /// </summary>
    /// <param name="startIndex">The first command index.</param>
    /// <param name="count">The command count.</param>
    /// <param name="hasLayers">Indicates whether the command range contains layer boundary commands.</param>
    /// <returns>The command-range entry.</returns>
    public static DrawingCanvasTimelineEntry CreateCommandRange(int startIndex, int count, bool hasLayers)
        => new(DrawingCanvasTimelineEntryKind.CommandRange, startIndex, count, hasLayers);

    /// <summary>
    /// Creates an apply-barrier entry.
    /// </summary>
    /// <param name="index">The apply-barrier index.</param>
    /// <returns>The apply-barrier entry.</returns>
    public static DrawingCanvasTimelineEntry CreateApplyBarrier(int index)
        => new(DrawingCanvasTimelineEntryKind.ApplyBarrier, index, 0, false);

    /// <summary>
    /// Creates an entry for an existing retained scene recorded through <see cref="DrawingCanvas.RenderScene"/>.
    /// </summary>
    /// <param name="index">The retained-scene reference index.</param>
    /// <returns>The retained-scene entry.</returns>
    public static DrawingCanvasTimelineEntry CreateScene(int index)
        => new(DrawingCanvasTimelineEntryKind.Scene, index, 0, false);
}
