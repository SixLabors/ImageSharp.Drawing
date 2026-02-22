// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Prepared composition data emitted by <see cref="DrawingCanvasBatcher{TPixel}"/> and consumed by backends.
/// </summary>
internal sealed class CompositionBatch
{
    public CompositionBatch(
        CompositionCoverageDefinition definition,
        IReadOnlyList<PreparedCompositionCommand> commands)
    {
        this.Definition = definition;
        this.Commands = commands;
    }

    /// <summary>
    /// Gets the coverage definition that should be rasterized once per flush.
    /// </summary>
    public CompositionCoverageDefinition Definition { get; }

    /// <summary>
    /// Gets normalized composition commands in original draw order.
    /// </summary>
    public IReadOnlyList<PreparedCompositionCommand> Commands { get; }
}
