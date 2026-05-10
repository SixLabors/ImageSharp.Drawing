// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One prepared draw-order command batch consumed by a drawing backend.
/// </summary>
public readonly struct DrawingCommandBatch
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCommandBatch"/> struct.
    /// </summary>
    /// <param name="commands">The draw-order scene commands.</param>
    /// <param name="hasLayers">Indicates whether the command stream contains layer boundaries.</param>
    public DrawingCommandBatch(
        IReadOnlyList<CompositionSceneCommand> commands,
        bool hasLayers)
    {
        this.Commands = commands;
        this.HasLayers = hasLayers;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCommandBatch"/> struct.
    /// </summary>
    /// <param name="commands">The backing command buffer.</param>
    /// <param name="commandCount">The number of commands in the prepared batch.</param>
    /// <param name="hasLayers">Indicates whether the command stream contains layer boundaries.</param>
    internal DrawingCommandBatch(
        CompositionSceneCommand[] commands,
        int commandCount,
        bool hasLayers)
        : this(new ArraySegment<CompositionSceneCommand>(commands, 0, commandCount), hasLayers)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCommandBatch"/> struct.
    /// </summary>
    /// <param name="commands">The backing command buffer.</param>
    /// <param name="startIndex">The first command index.</param>
    /// <param name="commandCount">The number of commands in the prepared batch.</param>
    /// <param name="hasLayers">Indicates whether the command stream contains layer boundaries.</param>
    internal DrawingCommandBatch(
        CompositionSceneCommand[] commands,
        int startIndex,
        int commandCount,
        bool hasLayers)
        : this(new ArraySegment<CompositionSceneCommand>(commands, startIndex, commandCount), hasLayers)
    {
    }

    /// <summary>
    /// Gets the draw-order scene commands.
    /// </summary>
    public IReadOnlyList<CompositionSceneCommand> Commands { get; }

    /// <summary>
    /// Gets the total number of draw-order commands in the scene.
    /// </summary>
    public int CommandCount => this.Commands.Count;

    /// <summary>
    /// Gets a value indicating whether this scene contains inline layer commands.
    /// </summary>
    public bool HasLayers { get; }
}
