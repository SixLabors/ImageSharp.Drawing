// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Converts scene command streams into backend-ready prepared batches.
/// </summary>
internal static class CompositionScenePlanner
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
        List<CompositionBatch> batches = [];
        int index = 0;
        while (index < commands.Count)
        {
            CompositionCommand definitionCommand = commands[index];
            int definitionKey = definitionCommand.DefinitionKey;
            List<PreparedCompositionCommand> preparedCommands = [];
            for (; index < commands.Count; index++)
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
                    definitionCommand.RasterizerOptions);

            batches.Add(new CompositionBatch(definition, preparedCommands));
        }

        return batches;
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
            command.GraphicsOptions);

        return true;
    }
}
