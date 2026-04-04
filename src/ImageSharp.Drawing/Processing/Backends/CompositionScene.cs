// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One flush-time scene packet containing draw-order commands.
/// </summary>
public sealed class CompositionScene
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionScene"/> class.
    /// </summary>
    /// <param name="commands">The draw-order scene commands.</param>
    /// <param name="hasLayers">Indicates whether the command stream contains layer boundaries.</param>
    public CompositionScene(
        IReadOnlyList<CompositionSceneCommand> commands,
        bool hasLayers)
    {
        this.Commands = commands;
        this.HasLayers = hasLayers;
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
