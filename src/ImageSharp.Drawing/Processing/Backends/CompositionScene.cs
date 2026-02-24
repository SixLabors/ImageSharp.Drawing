// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One flush-time scene packet containing normalized composition commands in draw order.
/// </summary>
internal sealed class CompositionScene
{
    public CompositionScene(IReadOnlyList<CompositionCommand> commands)
    {
        this.Commands = commands;
    }

    /// <summary>
    /// Gets normalized composition commands in submission order.
    /// </summary>
    public IReadOnlyList<CompositionCommand> Commands { get; }
}
