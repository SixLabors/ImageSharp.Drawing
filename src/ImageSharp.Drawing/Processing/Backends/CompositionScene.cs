// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One flush-time scene packet containing normalized composition commands in draw order.
/// </summary>
public sealed class CompositionScene
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompositionScene"/> class.
    /// </summary>
    /// <param name="commands">The composition commands in submission order.</param>
    /// <param name="hasLayers">Indicates whether the command stream contains layer boundaries.</param>
    public CompositionScene(IReadOnlyList<CompositionCommand> commands, bool hasLayers)
    {
        this.Commands = commands;
        this.HasLayers = hasLayers;
    }

    /// <summary>
    /// Gets normalized composition commands in submission order.
    /// </summary>
    public IReadOnlyList<CompositionCommand> Commands { get; }

    /// <summary>
    /// Gets a value indicating whether this scene contains inline layer commands.
    /// </summary>
    public bool HasLayers { get; }
}
