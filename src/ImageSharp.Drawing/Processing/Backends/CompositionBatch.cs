// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Prepared composition data emitted by <see cref="DrawingCanvasBatcher{TPixel}"/> and consumed by backends.
/// </summary>
internal sealed class CompositionBatch
{
    public CompositionBatch(
        in CompositionCoverageDefinition definition,
        IReadOnlyList<PreparedCompositionCommand> commands,
        int flushId = 0,
        bool isFinalBatchInFlush = true)
    {
        this.Definition = definition;
        this.Commands = commands;
        this.FlushId = flushId;
        this.IsFinalBatchInFlush = isFinalBatchInFlush;
    }

    /// <summary>
    /// Gets the coverage definition that should be rasterized once per flush.
    /// </summary>
    public CompositionCoverageDefinition Definition { get; }

    /// <summary>
    /// Gets normalized composition commands in original draw order.
    /// </summary>
    public IReadOnlyList<PreparedCompositionCommand> Commands { get; }

    /// <summary>
    /// Gets the batcher flush identifier shared by all batches emitted from one canvas flush call.
    /// </summary>
    public int FlushId { get; }

    /// <summary>
    /// Gets a value indicating whether this is the last batch emitted for the current flush identifier.
    /// </summary>
    public bool IsFinalBatchInFlush { get; }
}
