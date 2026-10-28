// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// One prepared draw-order command batch consumed by a drawing backend.
/// </summary>
public readonly struct DrawingCommandBatch
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCommandBatch"/> struct.
    /// </summary>
    /// <remarks>
    /// Batches created through this constructor never contain apply barriers, clip controls or
    /// retained scene commands. Only the internal canvas batcher produces those commands.
    /// </remarks>
    /// <param name="commands">The draw-order scene commands.</param>
    /// <param name="hasLayers">Indicates whether the command stream contains layer boundaries.</param>
    public DrawingCommandBatch(
        IReadOnlyList<CompositionSceneCommand> commands,
        bool hasLayers)
    {
        this.Commands = commands;
        this.HasLayers = hasLayers;
        this.HasApply = false;
        this.HasClipControls = false;
        this.HasScenes = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingCommandBatch"/> struct.
    /// </summary>
    /// <param name="commands">The backing command buffer.</param>
    /// <param name="startIndex">The first command index.</param>
    /// <param name="commandCount">The number of commands in the prepared batch.</param>
    /// <param name="hasLayers">Indicates whether the command stream contains layer boundaries.</param>
    /// <param name="hasApply">Indicates whether the command stream contains apply barriers.</param>
    /// <param name="hasClipControls">Indicates whether the command stream contains clip controls.</param>
    /// <param name="hasScenes">Indicates whether the command stream contains retained scene commands.</param>
    internal DrawingCommandBatch(
        CompositionSceneCommand[] commands,
        int startIndex,
        int commandCount,
        bool hasLayers,
        bool hasApply,
        bool hasClipControls,
        bool hasScenes)
    {
        this.Commands = new ArraySegment<CompositionSceneCommand>(commands, startIndex, commandCount);
        this.HasLayers = hasLayers;
        this.HasApply = hasApply;
        this.HasClipControls = hasClipControls;
        this.HasScenes = hasScenes;
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

    /// <summary>
    /// Gets a value indicating whether this scene contains apply barriers.
    /// </summary>
    public bool HasApply { get; }

    /// <summary>
    /// Gets a value indicating whether this scene contains clip control commands.
    /// </summary>
    public bool HasClipControls { get; }

    /// <summary>
    /// Gets a value indicating whether this scene contains retained scene commands.
    /// </summary>
    /// <remarks>
    /// This is internal because the retained scene command is internal. Only the canvas batcher sets it.
    /// </remarks>
    internal bool HasScenes { get; }

    /// <summary>
    /// Reads the next part of a batch that contains retained scene commands. A part is the run of
    /// commands up to the next retained scene command outside every layer, and that retained scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this only when <see cref="HasScenes"/> is <see langword="true"/>. Only the canvas batcher
    /// records retained scene commands, and its batches always wrap an <see cref="ArraySegment{T}"/>.
    /// </para>
    /// <para>
    /// No layer is open at a retained scene command outside every layer, and the batcher closes the
    /// open clips before it records one. Each run is therefore a balanced command stream that a
    /// backend can render on its own, before the retained scene.
    /// </para>
    /// </remarks>
    /// <param name="index">The first command index of the part. On return, the first command index of the next part.</param>
    /// <param name="commands">The commands of the part before the retained scene command.</param>
    /// <returns>The retained scene that ends the part, or <see langword="null"/> when the part ends at the end of the batch.</returns>
    internal DrawingBackendScene? ReadRootPart(ref int index, out DrawingCommandBatch commands)
    {
        ArraySegment<CompositionSceneCommand> segment = (ArraySegment<CompositionSceneCommand>)this.Commands;
        int start = index;
        bool hasLayers = false;
        bool hasApply = false;
        bool hasClipControls = false;
        bool hasScenes = false;
        DrawingBackendScene? scene = null;

        // The flags of the run are computed again here, so a backend that renders the run alone
        // takes the same path that it takes for a batch recorded without the retained scene.
        for (; index < segment.Count; index++)
        {
            CompositionSceneCommand command = segment[index];
            if (command is RetainedSceneCompositionSceneCommand sceneCommand)
            {
                if (!sceneCommand.IsInsideLayer)
                {
                    scene = sceneCommand.Scene;
                    break;
                }

                hasScenes = true;
            }
            else if (command is PathCompositionSceneCommand pathCommand)
            {
                switch (pathCommand.Command.Kind)
                {
                    case CompositionCommandKind.BeginLayer:
                    case CompositionCommandKind.EndLayer:
                        hasLayers = true;
                        break;

                    case CompositionCommandKind.Apply:
                        hasApply = true;
                        break;

                    case CompositionCommandKind.BeginClip:
                    case CompositionCommandKind.EndClip:
                        hasClipControls = true;
                        break;
                }
            }
        }

        commands = new DrawingCommandBatch(
            segment.Array!,
            segment.Offset + start,
            index - start,
            hasLayers,
            hasApply,
            hasClipControls,
            hasScenes);

        if (scene is not null)
        {
            index++;
        }

        return scene;
    }

    /// <summary>
    /// Finds the retained scenes that hold ordered work, and the layers that must render as scoped
    /// layers around them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a backend knows whether one of its scenes holds ordered work, such as an apply barrier,
    /// so the backend supplies <paramref name="hasOrderedWork"/>. A batch with such a scene needs the
    /// ordered path of the backend, the same as a batch with an apply barrier.
    /// </para>
    /// <para>
    /// Ordered work must see every pixel drawn before it. Each layer open around such a scene is
    /// therefore scoped, the same as each layer open around an apply drawn directly in it.
    /// </para>
    /// </remarks>
    /// <param name="hasOrderedWork">Returns whether a retained scene of the backend holds ordered work.</param>
    /// <param name="scopedLayers">
    /// Receives, for each command index, whether the BeginLayer command at that index opens a layer
    /// that must be scoped, or <see langword="null"/> when no layer must be.
    /// </param>
    /// <returns><see langword="true"/> when a retained scene in the batch holds ordered work.</returns>
    internal bool FindOrderedScenes(Func<DrawingBackendScene, bool> hasOrderedWork, out bool[]? scopedLayers)
    {
        IReadOnlyList<CompositionSceneCommand> commands = this.Commands;
        List<int>? openLayers = null;
        bool found = false;
        scopedLayers = null;

        for (int i = 0; i < commands.Count; i++)
        {
            switch (commands[i])
            {
                case PathCompositionSceneCommand { Command.Kind: CompositionCommandKind.BeginLayer }:
                    (openLayers ??= []).Add(i);
                    break;

                case PathCompositionSceneCommand { Command.Kind: CompositionCommandKind.EndLayer }:
                    // The canvas never seals a range inside a layer, so each EndLayer closes a
                    // BeginLayer of this batch.
                    openLayers!.RemoveAt(openLayers.Count - 1);
                    break;

                case RetainedSceneCompositionSceneCommand sceneCommand when hasOrderedWork(sceneCommand.Scene):
                    found = true;
                    if (openLayers is { Count: > 0 })
                    {
                        scopedLayers ??= new bool[commands.Count];
                        for (int j = 0; j < openLayers.Count; j++)
                        {
                            scopedLayers[openLayers[j]] = true;
                        }
                    }

                    break;
            }
        }

        return found;
    }
}
