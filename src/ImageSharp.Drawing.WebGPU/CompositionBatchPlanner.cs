// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Converts scene command streams into contiguous prepared batches for the WebGPU backend.
/// </summary>
public static class CompositionBatchPlanner
{
    /// <summary>
    /// Creates contiguous prepared batches grouped by coverage definition key.
    /// </summary>
    /// <param name="commands">Scene commands in submission order.</param>
    /// <param name="targetBounds">Target frame bounds in absolute coordinates.</param>
    /// <returns>Prepared contiguous batches ready for backend execution.</returns>
    public static List<CompositionBatch> CreatePreparedBatches(
        IReadOnlyList<CompositionCommand> commands,
        in Rectangle targetBounds)
    {
        int commandCount = commands.Count;
        List<CompositionBatch> batches = new(EstimateBatchCapacity(commandCount));
        int runStart = 0;
        while (runStart < commandCount)
        {
            CompositionCommand definitionCommand = commands[runStart];
            int definitionKey = definitionCommand.DefinitionKey;

            int runEnd = runStart + 1;
            while (runEnd < commandCount && commands[runEnd].DefinitionKey == definitionKey)
            {
                runEnd++;
            }

            int runLength = runEnd - runStart;
            List<PreparedCompositionCommand> preparedCommands = new(runLength);
            for (int index = runStart; index < runEnd; index++)
            {
                CompositionCommand command = commands[index];
                if (CompositionCommandPreparer.TryPrepareCommand(in command, in targetBounds, out PreparedCompositionCommand prepared))
                {
                    preparedCommands.Add(prepared);
                }
            }

            if (preparedCommands.Count == 0)
            {
                runStart = runEnd;
                continue;
            }

            CompositionCoverageDefinition definition =
                new(
                    definitionKey,
                    definitionCommand.Geometry ?? throw new InvalidOperationException("Commands must be prepared before planning."),
                    definitionCommand.RasterizerOptions,
                    definitionCommand.DestinationOffset);

            batches.Add(new CompositionBatch(definition, preparedCommands));
            runStart = runEnd;
        }

        return batches;
    }

    /// <summary>
    /// Re-prepares batch commands after stroke expansion so destination regions
    /// and source offsets match the actual outline interest.
    /// </summary>
    /// <param name="commands">The prepared commands to update in place.</param>
    /// <param name="targetBounds">Target frame bounds in absolute coordinates.</param>
    /// <param name="interest">The actual interest rect computed from the expanded outline.</param>
    public static void ReprepareBatchCommands(
        List<PreparedCompositionCommand> commands,
        Rectangle targetBounds,
        Rectangle interest)
    {
        Span<PreparedCompositionCommand> span = CollectionsMarshal.AsSpan(commands);
        int writeIndex = 0;
        for (int i = 0; i < span.Length; i++)
        {
            ref PreparedCompositionCommand cmd = ref span[i];

            Rectangle commandDestination = new(
                cmd.DestinationOffset.X + interest.X,
                cmd.DestinationOffset.Y + interest.Y,
                interest.Width,
                interest.Height);

            Rectangle clippedDestination = Rectangle.Intersect(targetBounds, commandDestination);
            if (clippedDestination.Width <= 0 || clippedDestination.Height <= 0)
            {
                continue;
            }

            Rectangle destinationLocalRegion = new(
                clippedDestination.X - targetBounds.X,
                clippedDestination.Y - targetBounds.Y,
                clippedDestination.Width,
                clippedDestination.Height);

            Point sourceOffset = new(
                clippedDestination.X - commandDestination.X,
                clippedDestination.Y - commandDestination.Y);

            cmd.DestinationRegion = destinationLocalRegion;
            cmd.SourceOffset = sourceOffset;

            if (writeIndex != i)
            {
                span[writeIndex] = span[i];
            }

            writeIndex++;
        }

        if (writeIndex < commands.Count)
        {
            commands.RemoveRange(writeIndex, commands.Count - writeIndex);
        }
    }

    /// <summary>
    /// Estimates initial capacity for the outer batch list from total scene command count.
    /// </summary>
    /// <param name="commandCount">Total number of scene commands.</param>
    /// <returns>Suggested initial capacity for the batch list.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateBatchCapacity(int commandCount)
    {
        if (commandCount <= 8)
        {
            return commandCount;
        }

        if (commandCount <= 128)
        {
            return commandCount / 2;
        }

        return commandCount / 4;
    }
}
