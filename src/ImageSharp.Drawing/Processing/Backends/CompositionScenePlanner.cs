// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Converts scene command streams into backend-ready prepared batches.
/// </summary>
public static class CompositionScenePlanner
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
        int index = 0;
        while (index < commandCount)
        {
            CompositionCommand definitionCommand = commands[index];
            int definitionKey = definitionCommand.DefinitionKey;
            int remainingCount = commandCount - index;
            List<PreparedCompositionCommand> preparedCommands = new(EstimatePreparedCommandCapacity(remainingCount));
            for (; index < commandCount; index++)
            {
                CompositionCommand command = commands[index];
                if (command.DefinitionKey != definitionKey)
                {
                    break;
                }

                if (TryPrepareCommand(in command, in targetBounds, out PreparedCompositionCommand prepared))
                {
                    preparedCommands.Add(prepared);
                }
            }

            if (preparedCommands.Count == 0)
            {
                continue;
            }

            CompositionCoverageDefinition definition =
                new(
                    definitionKey,
                    definitionCommand.Path,
                    definitionCommand.RasterizerOptions,
                    definitionCommand.DestinationOffset,
                    definitionCommand.StrokeOptions,
                    definitionCommand.StrokeWidth,
                    definitionCommand.StrokePattern);

            batches.Add(new CompositionBatch(definition, preparedCommands));
        }

        return batches;
    }

    /// <summary>
    /// Estimates initial capacity for the outer batch list from total scene command count.
    /// </summary>
    /// <param name="commandCount">Total number of scene commands.</param>
    /// <returns>Suggested initial capacity for the batch list.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimateBatchCapacity(int commandCount)
    {
        // Typical scenes reuse coverage definitions, so batch count is usually
        // meaningfully lower than command count.
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

    /// <summary>
    /// Estimates initial capacity for one contiguous prepared-command run.
    /// </summary>
    /// <param name="remainingCount">Commands remaining from the current scan index.</param>
    /// <returns>Suggested initial capacity for the current prepared-command list.</returns>
    /// <remarks>
    /// This estimate is intentionally capped for large tails because the list is
    /// allocated per run during scanning rather than once per scene.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EstimatePreparedCommandCapacity(int remainingCount)
    {
        // Most adjacent commands share a definition in small-medium scenes.
        if (remainingCount <= 16)
        {
            return remainingCount;
        }

        if (remainingCount <= 128)
        {
            return remainingCount / 2;
        }

        return 64;
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
    /// Clips one scene command to target bounds and computes coverage source offset mapping.
    /// </summary>
    /// <param name="command">The source command.</param>
    /// <param name="targetBounds">Target frame bounds in absolute coordinates.</param>
    /// <param name="prepared">Prepared command when clipping produces visible output.</param>
    /// <returns><see langword="true"/> when the command has visible output in target bounds.</returns>
    public static bool TryPrepareCommand(
        in CompositionCommand command,
        in Rectangle targetBounds,
        out PreparedCompositionCommand prepared)
    {
        Rectangle interest = command.RasterizerOptions.Interest;
        Rectangle commandDestination = new(
            command.DestinationOffset.X + interest.X,
            command.DestinationOffset.Y + interest.Y,
            interest.Width,
            interest.Height);

        Rectangle clippedDestination = Rectangle.Intersect(targetBounds, commandDestination);
        if (clippedDestination.Width <= 0 || clippedDestination.Height <= 0)
        {
            prepared = default;
            return false;
        }

        Rectangle destinationLocalRegion = new(
            clippedDestination.X - targetBounds.X,
            clippedDestination.Y - targetBounds.Y,
            clippedDestination.Width,
            clippedDestination.Height);

        Point sourceOffset = new(
            clippedDestination.X - commandDestination.X,
            clippedDestination.Y - commandDestination.Y);

        prepared = new PreparedCompositionCommand(
            destinationLocalRegion,
            sourceOffset,
            command.Brush,
            command.BrushBounds,
            command.GraphicsOptions,
            command.DestinationOffset);

        return true;
    }
}
