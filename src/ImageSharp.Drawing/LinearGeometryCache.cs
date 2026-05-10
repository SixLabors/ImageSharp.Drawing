// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Single-entry memoization slot for a scale-baked <see cref="LinearGeometry"/> derived from an <see cref="IPath"/>.
/// Entries are keyed on the X/Y scale so text and panning workloads — which re-render the same shapes at a fixed
/// zoom level while their rotation/translation/perspective drift — hit a stable cached bake.
/// </summary>
/// <remarks>
/// Safe for concurrent readers and writers. Publication uses <see cref="Volatile.Write{T}(ref T, T)"/> so a reader
/// either observes <see langword="null"/> or a fully-constructed entry.
/// </remarks>
internal struct LinearGeometryCache
{
    private Entry? entry;

    public bool TryGet(Vector2 scale, [NotNullWhen(true)] out LinearGeometry? value)
    {
        Entry? hit = Volatile.Read(ref this.entry);
        if (hit is not null && hit.Scale == scale)
        {
            value = hit.Value;
            return true;
        }

        value = null;
        return false;
    }

    public LinearGeometry Store(Vector2 scale, LinearGeometry value)
    {
        Volatile.Write(ref this.entry, new Entry(scale, value));
        return value;
    }

    private sealed class Entry(Vector2 scale, LinearGeometry value)
    {
        public Vector2 Scale { get; } = scale;

        public LinearGeometry Value { get; } = value;
    }
}
