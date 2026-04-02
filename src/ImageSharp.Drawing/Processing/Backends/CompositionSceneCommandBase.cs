// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1649 // Scene command types are grouped together in one file.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Visitor contract for one flush-scoped composition scene command.
/// </summary>
public interface ICompositionSceneCommandVisitor
{
    /// <summary>
    /// Visits one fill-path or layer-based composition command.
    /// </summary>
    /// <param name="command">The command being visited.</param>
    public void Visit(PathCompositionSceneCommand command);

    /// <summary>
    /// Visits one stroked path command.
    /// </summary>
    /// <param name="command">The command being visited.</param>
    public void Visit(StrokePathCompositionSceneCommand command);

    /// <summary>
    /// Visits one explicit stroked line-segment command.
    /// </summary>
    /// <param name="command">The command being visited.</param>
    public void Visit(LineSegmentCompositionSceneCommand command);

    /// <summary>
    /// Visits one explicit stroked polyline command.
    /// </summary>
    /// <param name="command">The command being visited.</param>
    public void Visit(PolylineCompositionSceneCommand command);
}

/// <summary>
/// Base type for one draw-order command in a flush-scoped scene stream.
/// </summary>
public abstract class CompositionSceneCommand
{
    /// <summary>
    /// Dispatches the command to a visitor without a per-item kind switch at the call site.
    /// </summary>
    /// <param name="visitor">The visitor receiving the command.</param>
    public abstract void Accept(ICompositionSceneCommandVisitor visitor);
}

/// <summary>
/// Scene command wrapper for fill-path and layer-based composition commands.
/// </summary>
public sealed class PathCompositionSceneCommand : CompositionSceneCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PathCompositionSceneCommand"/> class.
    /// </summary>
    /// <param name="command">The wrapped composition command.</param>
    public PathCompositionSceneCommand(in CompositionCommand command)
        => this.Command = command;

    /// <summary>
    /// Gets the wrapped composition command.
    /// </summary>
    public CompositionCommand Command { get; internal set; }

    /// <inheritdoc />
    public override void Accept(ICompositionSceneCommandVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Scene command wrapper for stroked path commands.
/// </summary>
public sealed class StrokePathCompositionSceneCommand : CompositionSceneCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StrokePathCompositionSceneCommand"/> class.
    /// </summary>
    /// <param name="command">The wrapped stroke path command.</param>
    public StrokePathCompositionSceneCommand(in StrokePathCommand command)
        => this.Command = command;

    /// <summary>
    /// Gets the wrapped stroke path command.
    /// </summary>
    public StrokePathCommand Command { get; internal set; }

    /// <inheritdoc />
    public override void Accept(ICompositionSceneCommandVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Scene command wrapper for explicit stroked line-segment commands.
/// </summary>
public sealed class LineSegmentCompositionSceneCommand : CompositionSceneCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LineSegmentCompositionSceneCommand"/> class.
    /// </summary>
    /// <param name="command">The wrapped stroke line-segment command.</param>
    public LineSegmentCompositionSceneCommand(in StrokeLineSegmentCommand command)
        => this.Command = command;

    /// <summary>
    /// Gets the wrapped stroke line-segment command.
    /// </summary>
    public StrokeLineSegmentCommand Command { get; }

    /// <inheritdoc />
    public override void Accept(ICompositionSceneCommandVisitor visitor) => visitor.Visit(this);
}

/// <summary>
/// Scene command wrapper for explicit stroked polyline commands.
/// </summary>
public sealed class PolylineCompositionSceneCommand : CompositionSceneCommand
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PolylineCompositionSceneCommand"/> class.
    /// </summary>
    /// <param name="command">The wrapped stroke polyline command.</param>
    public PolylineCompositionSceneCommand(in StrokePolylineCommand command)
        => this.Command = command;

    /// <summary>
    /// Gets the wrapped stroke polyline command.
    /// </summary>
    public StrokePolylineCommand Command { get; }

    /// <inheritdoc />
    public override void Accept(ICompositionSceneCommandVisitor visitor) => visitor.Visit(this);
}
