// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Builds the retained <see cref="PointF"/> array used by flattened segment caches without an intermediate collection copy.
/// </summary>
/// <remarks>
/// Segment flatteners ultimately need to return a tightly-sized array that can be cached by the segment instance.
/// This builder owns that array while points are appended.
/// </remarks>
internal struct FlattenedPointBuilder
{
    private PointF[] points;
    private int count;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlattenedPointBuilder"/> struct.
    /// </summary>
    /// <param name="capacity">The estimated number of points that will be appended.</param>
    public FlattenedPointBuilder(int capacity)
    {
        this.points = new PointF[Math.Max(capacity, 4)];
        this.count = 0;
    }

    /// <summary>
    /// Appends one point to the retained point array.
    /// </summary>
    /// <param name="point">The point to append.</param>
    public void Add(PointF point)
    {
        this.EnsureCapacity(this.count + 1);
        this.points[this.count++] = point;
    }

    /// <summary>
    /// Reserves a writable append window for callers that populate multiple points directly.
    /// </summary>
    /// <param name="length">The number of points to reserve.</param>
    /// <returns>A span covering the reserved append window.</returns>
    public Span<PointF> GetAppendSpan(int length)
    {
        this.EnsureCapacity(this.count + length);
        return this.points.AsSpan(this.count, length);
    }

    /// <summary>
    /// Commits points previously written through <see cref="GetAppendSpan"/>.
    /// </summary>
    /// <param name="length">The number of points written to the reserved append window.</param>
    public void Advance(int length) => this.count += length;

    /// <summary>
    /// Returns the owned point array.
    /// </summary>
    /// <returns>The tightly-sized retained point array.</returns>
    public PointF[] Detach()
    {
        if (this.count != this.points.Length)
        {
            Array.Resize(ref this.points, this.count);
        }

        return this.points;
    }

    /// <summary>
    /// Ensures the owned array can store the requested total point count.
    /// </summary>
    /// <param name="capacity">The total number of points that must fit.</param>
    private void EnsureCapacity(int capacity)
    {
        if (capacity <= this.points.Length)
        {
            return;
        }

        Array.Resize(ref this.points, Math.Max(capacity, this.points.Length * 2));
    }
}
