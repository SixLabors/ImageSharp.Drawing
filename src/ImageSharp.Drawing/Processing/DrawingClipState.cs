// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <summary>
/// Represents an ordered set of exact clip primitives carried by a drawing command.
/// </summary>
public readonly struct DrawingClipState
{
    // Default struct instances cannot initialize reference fields. Inactive inline slots point
    // at this descriptor, while Count remains the invariant that controls which slots are read.
    private static readonly DrawingClipDescriptor EmptyDescriptor = DrawingClipDescriptor.CreateRectangle(
        default,
        ClipOperation.Intersection,
        DrawingClipEdgeMode.Hard,
        0F);

    // Most clip stacks in UI redraws are shallow: a dirty region plus one or two control clips.
    // Keep that path allocation-free and only spill to an array for unusually deep nesting.
    private readonly DrawingClipDescriptor descriptor0;
    private readonly DrawingClipDescriptor descriptor1;
    private readonly DrawingClipDescriptor descriptor2;
    private readonly DrawingClipDescriptor descriptor3;
    private readonly DrawingClipDescriptor[]? overflowDescriptors;

    private DrawingClipState(DrawingClipDescriptor descriptor0)
    {
        this.descriptor0 = descriptor0;
        this.descriptor1 = EmptyDescriptor;
        this.descriptor2 = EmptyDescriptor;
        this.descriptor3 = EmptyDescriptor;
        this.overflowDescriptors = null;
        this.Count = 1;
    }

    private DrawingClipState(DrawingClipDescriptor descriptor0, DrawingClipDescriptor descriptor1)
    {
        this.descriptor0 = descriptor0;
        this.descriptor1 = descriptor1;
        this.descriptor2 = EmptyDescriptor;
        this.descriptor3 = EmptyDescriptor;
        this.overflowDescriptors = null;
        this.Count = 2;
    }

    private DrawingClipState(DrawingClipDescriptor descriptor0, DrawingClipDescriptor descriptor1, DrawingClipDescriptor descriptor2)
    {
        this.descriptor0 = descriptor0;
        this.descriptor1 = descriptor1;
        this.descriptor2 = descriptor2;
        this.descriptor3 = EmptyDescriptor;
        this.overflowDescriptors = null;
        this.Count = 3;
    }

    private DrawingClipState(
        DrawingClipDescriptor descriptor0,
        DrawingClipDescriptor descriptor1,
        DrawingClipDescriptor descriptor2,
        DrawingClipDescriptor descriptor3)
    {
        this.descriptor0 = descriptor0;
        this.descriptor1 = descriptor1;
        this.descriptor2 = descriptor2;
        this.descriptor3 = descriptor3;
        this.overflowDescriptors = null;
        this.Count = 4;
    }

    private DrawingClipState(DrawingClipDescriptor[] overflowDescriptors)
    {
        this.descriptor0 = EmptyDescriptor;
        this.descriptor1 = EmptyDescriptor;
        this.descriptor2 = EmptyDescriptor;
        this.descriptor3 = EmptyDescriptor;
        this.overflowDescriptors = overflowDescriptors;
        this.Count = overflowDescriptors.Length;
    }

    /// <summary>
    /// Gets an empty clip state.
    /// </summary>
    public static DrawingClipState Empty => default;

    /// <summary>
    /// Gets the number of clip descriptors in stack order.
    /// </summary>
    public int Count { get; }

    /// <summary>
    /// Gets a value indicating whether this state carries any clips.
    /// </summary>
    public bool HasClips => this.Count > 0;

    /// <summary>
    /// Creates a clip state from path operands.
    /// </summary>
    /// <param name="paths">The clip paths.</param>
    /// <param name="operation">The operation used to combine the paths with the existing clip.</param>
    /// <param name="edgeMode">The clip edge mode.</param>
    /// <param name="antialiasThreshold">The coverage threshold used for hard clip edges.</param>
    /// <returns>The normalized clip state.</returns>
    public static DrawingClipState FromPaths(
        IReadOnlyList<IPath> paths,
        ClipOperation operation,
        DrawingClipEdgeMode edgeMode,
        float antialiasThreshold)
    {
        if (paths.Count == 0)
        {
            return Empty;
        }

        DrawingClipDescriptor descriptor = paths.Count == 1
            ? DrawingClipDescriptor.FromPath(paths[0], operation, edgeMode, antialiasThreshold)
            : DrawingClipDescriptor.CreatePath(paths, operation, edgeMode, antialiasThreshold);

        // Multiple paths supplied in one Clip call are one clip operand. They are not separate
        // stack entries because the operation applies to the combined incoming shape.
        return new DrawingClipState(descriptor);
    }

    /// <summary>
    /// Gets one descriptor by stack order.
    /// </summary>
    /// <param name="index">The descriptor index.</param>
    /// <returns>The descriptor at the requested index.</returns>
    public DrawingClipDescriptor GetDescriptor(int index)
    {
        if (this.overflowDescriptors is not null)
        {
            return this.overflowDescriptors[index];
        }

        return index switch
        {
            0 => this.descriptor0,
            1 => this.descriptor1,
            2 => this.descriptor2,
            _ => this.descriptor3,
        };
    }

    /// <summary>
    /// Appends one clip descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor to append.</param>
    /// <returns>The updated clip state.</returns>
    public DrawingClipState Append(DrawingClipDescriptor descriptor)
    {
        if (!this.HasClips)
        {
            return new DrawingClipState(descriptor);
        }

        if (this.Count == 1)
        {
            return new DrawingClipState(this.descriptor0, descriptor);
        }

        if (this.Count == 2)
        {
            return new DrawingClipState(this.descriptor0, this.descriptor1, descriptor);
        }

        if (this.Count == 3)
        {
            return new DrawingClipState(this.descriptor0, this.descriptor1, this.descriptor2, descriptor);
        }

        // Four inline descriptors cover the common UI path. Allocating here is reserved for
        // genuinely deep clip stacks rather than every push.
        DrawingClipDescriptor[] appended = new DrawingClipDescriptor[this.Count + 1];
        for (int i = 0; i < this.Count; i++)
        {
            appended[i] = this.GetDescriptor(i);
        }

        appended[^1] = descriptor;
        return new DrawingClipState(appended);
    }

    /// <summary>
    /// Appends all descriptors from another state.
    /// </summary>
    /// <param name="state">The clip state to append.</param>
    /// <returns>The updated clip state.</returns>
    public DrawingClipState Append(DrawingClipState state)
    {
        if (!this.HasClips)
        {
            return state;
        }

        if (!state.HasClips)
        {
            return this;
        }

        int count = this.Count + state.Count;
        if (count == 2)
        {
            return new DrawingClipState(this.GetDescriptor(0), state.GetDescriptor(0));
        }

        if (count == 3)
        {
            return new DrawingClipState(
                this.GetDescriptor(0),
                this.Count == 2 ? this.GetDescriptor(1) : state.GetDescriptor(0),
                state.GetDescriptor(state.Count - 1));
        }

        if (count == 4)
        {
            DrawingClipDescriptor d0 = this.GetDescriptor(0);
            DrawingClipDescriptor d1 = this.Count > 1 ? this.GetDescriptor(1) : state.GetDescriptor(0);
            DrawingClipDescriptor d2 = this.Count > 2 ? this.GetDescriptor(2) : state.GetDescriptor(2 - this.Count);
            DrawingClipDescriptor d3 = state.GetDescriptor(state.Count - 1);

            return new DrawingClipState(d0, d1, d2, d3);
        }

        // Keep ordering stable while spilling only after the inline descriptor slots are full.
        DrawingClipDescriptor[] appended = new DrawingClipDescriptor[count];
        for (int i = 0; i < this.Count; i++)
        {
            appended[i] = this.GetDescriptor(i);
        }

        for (int i = 0; i < state.Count; i++)
        {
            appended[this.Count + i] = state.GetDescriptor(i);
        }

        return new DrawingClipState(appended);
    }

    /// <summary>
    /// Translates this clip state.
    /// </summary>
    /// <param name="offset">The translation offset.</param>
    /// <returns>The translated clip state.</returns>
    public DrawingClipState Translate(Vector2 offset)
    {
        if (!this.HasClips || offset == Vector2.Zero)
        {
            return this;
        }

        if (this.Count == 1)
        {
            return new DrawingClipState(this.descriptor0.Translate(offset));
        }

        if (this.Count == 2)
        {
            return new DrawingClipState(
                this.descriptor0.Translate(offset),
                this.descriptor1.Translate(offset));
        }

        if (this.Count == 3)
        {
            return new DrawingClipState(
                this.descriptor0.Translate(offset),
                this.descriptor1.Translate(offset),
                this.descriptor2.Translate(offset));
        }

        if (this.Count == 4)
        {
            return new DrawingClipState(
                this.descriptor0.Translate(offset),
                this.descriptor1.Translate(offset),
                this.descriptor2.Translate(offset),
                this.descriptor3.Translate(offset));
        }

        DrawingClipDescriptor[] translated = new DrawingClipDescriptor[this.Count];
        for (int i = 0; i < translated.Length; i++)
        {
            translated[i] = this.GetDescriptor(i).Translate(offset);
        }

        return new DrawingClipState(translated);
    }

    /// <summary>
    /// Transforms this clip state.
    /// </summary>
    /// <param name="matrix">The transform matrix.</param>
    /// <returns>The transformed clip state.</returns>
    public DrawingClipState Transform(Matrix4x4 matrix)
    {
        if (!this.HasClips || matrix.IsIdentity)
        {
            return this;
        }

        if (this.Count == 1)
        {
            return new DrawingClipState(this.descriptor0.Transform(matrix));
        }

        if (this.Count == 2)
        {
            return new DrawingClipState(
                this.descriptor0.Transform(matrix),
                this.descriptor1.Transform(matrix));
        }

        if (this.Count == 3)
        {
            return new DrawingClipState(
                this.descriptor0.Transform(matrix),
                this.descriptor1.Transform(matrix),
                this.descriptor2.Transform(matrix));
        }

        if (this.Count == 4)
        {
            return new DrawingClipState(
                this.descriptor0.Transform(matrix),
                this.descriptor1.Transform(matrix),
                this.descriptor2.Transform(matrix),
                this.descriptor3.Transform(matrix));
        }

        DrawingClipDescriptor[] transformed = new DrawingClipDescriptor[this.Count];
        for (int i = 0; i < transformed.Length; i++)
        {
            transformed[i] = this.GetDescriptor(i).Transform(matrix);
        }

        return new DrawingClipState(transformed);
    }

    /// <summary>
    /// Gets conservative integer bounds for intersection descriptors in absolute target coordinates.
    /// </summary>
    /// <param name="destinationOffset">The absolute destination offset for the owning command.</param>
    /// <param name="bounds">The conservative bounds.</param>
    /// <returns><see langword="true"/> when at least one intersection descriptor has clip bounds.</returns>
    public bool TryGetConservativeBounds(Point destinationOffset, out Rectangle bounds)
    {
        bounds = default;
        bool hasBounds = false;

        for (int i = 0; i < this.Count; i++)
        {
            DrawingClipDescriptor descriptor = this.GetDescriptor(i);
            if (descriptor.Operation != ClipOperation.Intersection)
            {
                continue;
            }

            Rectangle descriptorBounds = descriptor.GetConservativeBounds(destinationOffset);
            bounds = hasBounds ? Rectangle.Intersect(bounds, descriptorBounds) : descriptorBounds;
            hasBounds = true;
        }

        return hasBounds;
    }

    /// <summary>
    /// Gets bounds for a single pixel-aligned clip that can be represented entirely by target bounds.
    /// </summary>
    /// <param name="destinationOffset">The absolute destination offset for the owning canvas state.</param>
    /// <param name="bounds">The exact integer clip bounds.</param>
    /// <returns><see langword="true"/> when this state contains only one pixel-aligned rectangle clip.</returns>
    public bool TryGetTargetBoundsClip(Point destinationOffset, out Rectangle bounds)
    {
        bounds = default;
        if (this.Count != 1)
        {
            return false;
        }

        DrawingClipDescriptor descriptor = this.descriptor0;
        if (descriptor.Operation != ClipOperation.Intersection)
        {
            return false;
        }

        if (descriptor.Kind == DrawingClipKind.IntegerRegion)
        {
            IReadOnlyList<Rectangle> rectangles = descriptor.IntegerRectangles;
            if (rectangles.Count != 1)
            {
                return false;
            }

            bounds = rectangles[0];
            bounds.Offset(destinationOffset);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        if (descriptor.Kind != DrawingClipKind.Rectangle)
        {
            return false;
        }

        RectangleF rectangle = descriptor.Rectangle;
        float left = rectangle.Left + destinationOffset.X;
        float top = rectangle.Top + destinationOffset.Y;
        float right = rectangle.Right + destinationOffset.X;
        float bottom = rectangle.Bottom + destinationOffset.Y;
        int integerLeft = (int)left;
        int integerTop = (int)top;
        int integerRight = (int)right;
        int integerBottom = (int)bottom;
        if (left != integerLeft || top != integerTop || right != integerRight || bottom != integerBottom)
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(integerLeft, integerTop, integerRight, integerBottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }
}
