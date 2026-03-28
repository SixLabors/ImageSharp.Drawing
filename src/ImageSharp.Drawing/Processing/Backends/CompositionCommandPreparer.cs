// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Clips and normalizes composition commands for backend execution.
/// </summary>
public static class CompositionCommandPreparer
{
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
