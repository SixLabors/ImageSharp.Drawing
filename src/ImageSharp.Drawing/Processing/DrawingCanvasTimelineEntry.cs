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
    /// An existing retained scene recorded through <see cref="DrawingCanvas.RenderScene"/>.
    /// </summary>
    Scene
}

/// <summary>
/// Represents one ordered item in the canvas replay timeline.
/// </summary>
/// <remarks>
/// Command ranges reference contiguous draw commands; they are not backend scene objects yet.
/// Retained scene references point into a side buffer by index, keeping this type compact while
/// preserving the exact order in which the canvas recorded replay work.
/// </remarks>
internal readonly struct DrawingCanvasTimelineEntry
{
    private DrawingCanvasTimelineEntry(
        DrawingCanvasTimelineEntryKind kind,
        int index,
        int count,
        bool hasLayers,
        bool hasApply)
    {
        this.Kind = kind;
        this.Index = index;
        this.Count = count;
        this.HasLayers = hasLayers;
        this.HasApply = hasApply;
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
    /// Gets a value indicating whether the command range contains apply barriers.
    /// </summary>
    public bool HasApply { get; }

    /// <summary>
    /// Creates a command-range entry.
    /// </summary>
    /// <param name="startIndex">The first command index.</param>
    /// <param name="count">The command count.</param>
    /// <param name="hasLayers">Indicates whether the command range contains layer boundary commands.</param>
    /// <param name="hasApply">Indicates whether the command range contains apply barriers.</param>
    /// <returns>The command-range entry.</returns>
    public static DrawingCanvasTimelineEntry CreateCommandRange(int startIndex, int count, bool hasLayers, bool hasApply)
        => new(DrawingCanvasTimelineEntryKind.CommandRange, startIndex, count, hasLayers, hasApply);

    /// <summary>
    /// Creates an entry for an existing retained scene recorded through <see cref="DrawingCanvas.RenderScene"/>.
    /// </summary>
    /// <param name="index">The retained-scene reference index.</param>
    /// <returns>The retained-scene entry.</returns>
    public static DrawingCanvasTimelineEntry CreateScene(int index)
        => new(DrawingCanvasTimelineEntryKind.Scene, index, 0, false, false);
}
