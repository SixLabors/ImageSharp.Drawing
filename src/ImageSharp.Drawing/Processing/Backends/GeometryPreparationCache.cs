// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Flush-scoped cache for sharing prepared paths across commands that have identical
/// geometry-affecting inputs.
/// </summary>
/// <remarks>
/// This cache sits above both CPU and WebGPU backends. It deduplicates the expensive
/// transform, stroke-expansion, and clip work that happens during command preparation
/// while keeping destination- and brush-specific state on each command.
/// </remarks>
internal sealed class GeometryPreparationCache
{
    private readonly ConcurrentDictionary<GeometryPreparationKey, Lazy<IPath>> cache = [];

    /// <summary>
    /// Gets a shared prepared path instance for the given command.
    /// </summary>
    /// <param name="command">The command being prepared.</param>
    /// <returns>A prepared path shared by equivalent commands in this flush.</returns>
    public IPath GetOrCreate(in CompositionCommand command)
    {
        GeometryPreparationKey key = command.CreateGeometryPreparationKey();
        CompositionCommand commandCopy = command;
        Lazy<IPath> lazy = this.cache.GetOrAdd(
            key,
            _ => new Lazy<IPath>(
                () => commandCopy.BuildPreparedPath(),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    /// <summary>
    /// Immutable cache key describing the geometry-affecting inputs for one command.
    /// </summary>
    internal readonly struct GeometryPreparationKey : IEquatable<GeometryPreparationKey>
    {
        private readonly IPath sourcePath;
        private readonly Matrix4x4 transform;
        private readonly PenGeometryKey penGeometry;
        private readonly IReadOnlyList<IPath>? clipPaths;
        private readonly BooleanOperation booleanOperation;
        private readonly IntersectionRule intersectionRule;
        private readonly bool enforceFillOrientation;
        private readonly int hashCode;

        /// <summary>
        /// Initializes a new instance of the <see cref="GeometryPreparationKey"/> struct.
        /// </summary>
        /// <param name="sourcePath">The original command path reference.</param>
        /// <param name="transform">The queued transform.</param>
        /// <param name="pen">The optional pen used for stroke expansion.</param>
        /// <param name="clipPaths">The clip path sequence applied during preparation.</param>
        /// <param name="shapeOptions">Options that influence clipping behavior.</param>
        /// <param name="enforceFillOrientation">Whether preparation should normalize closed contour winding.</param>
        public GeometryPreparationKey(
            IPath sourcePath,
            Matrix4x4 transform,
            Pen? pen,
            IReadOnlyList<IPath>? clipPaths,
            ShapeOptions shapeOptions,
            bool enforceFillOrientation)
        {
            this.sourcePath = sourcePath;
            this.transform = transform;
            this.penGeometry = new PenGeometryKey(pen);
            this.clipPaths = clipPaths;
            this.booleanOperation = shapeOptions.BooleanOperation;
            this.intersectionRule = shapeOptions.IntersectionRule;
            this.enforceFillOrientation = enforceFillOrientation;

            HashCode hash = default;
            hash.Add(RuntimeHelpers.GetHashCode(sourcePath));
            hash.Add(transform);
            hash.Add(this.penGeometry);
            hash.Add(this.booleanOperation);
            hash.Add(this.intersectionRule);
            hash.Add(this.enforceFillOrientation);

            if (this.clipPaths is not null)
            {
                hash.Add(this.clipPaths.Count);
                for (int i = 0; i < this.clipPaths.Count; i++)
                {
                    hash.Add(RuntimeHelpers.GetHashCode(this.clipPaths[i]));
                }
            }

            this.hashCode = hash.ToHashCode();
        }

        /// <inheritdoc/>
        public bool Equals(GeometryPreparationKey other)
        {
            if (!ReferenceEquals(this.sourcePath, other.sourcePath) ||
                !this.transform.Equals(other.transform) ||
                !this.penGeometry.Equals(other.penGeometry) ||
                this.booleanOperation != other.booleanOperation ||
                this.intersectionRule != other.intersectionRule ||
                this.enforceFillOrientation != other.enforceFillOrientation)
            {
                return false;
            }

            return ClipPathsEqual(this.clipPaths, other.clipPaths);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is GeometryPreparationKey other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => this.hashCode;

        private static bool ClipPathsEqual(IReadOnlyList<IPath>? left, IReadOnlyList<IPath>? right)
        {
            if (left is null || right is null)
            {
                return left is null && right is null;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!ReferenceEquals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Immutable snapshot of pen state that affects generated outline geometry.
    /// </summary>
    private readonly struct PenGeometryKey : IEquatable<PenGeometryKey>
    {
        private readonly Type? penType;
        private readonly float strokeWidth;
        private readonly ReadOnlyMemory<float> strokePattern;
        private readonly double miterLimit;
        private readonly double innerMiterLimit;
        private readonly double arcDetailScale;
        private readonly LineJoin lineJoin;
        private readonly LineCap lineCap;
        private readonly InnerJoin innerJoin;
        private readonly bool normalizeOutput;
        private readonly int hashCode;

        public PenGeometryKey(Pen? pen)
        {
            if (pen is null)
            {
                this.penType = null;
                this.strokeWidth = 0;
                this.strokePattern = default;
                this.miterLimit = 0;
                this.innerMiterLimit = 0;
                this.arcDetailScale = 0;
                this.lineJoin = default;
                this.lineCap = default;
                this.innerJoin = default;
                this.normalizeOutput = false;
                this.hashCode = 0;
                return;
            }

            this.penType = pen.GetType();
            this.strokeWidth = pen.StrokeWidth;
            this.strokePattern = pen.StrokePattern;

            StrokeOptions strokeOptions = pen.StrokeOptions;
            this.miterLimit = strokeOptions.MiterLimit;
            this.innerMiterLimit = strokeOptions.InnerMiterLimit;
            this.arcDetailScale = strokeOptions.ArcDetailScale;
            this.lineJoin = strokeOptions.LineJoin;
            this.lineCap = strokeOptions.LineCap;
            this.innerJoin = strokeOptions.InnerJoin;
            this.normalizeOutput = strokeOptions.NormalizeOutput;

            HashCode hash = default;
            hash.Add(this.penType);
            hash.Add(this.strokeWidth);
            hash.Add(this.miterLimit);
            hash.Add(this.innerMiterLimit);
            hash.Add(this.arcDetailScale);
            hash.Add(this.lineJoin);
            hash.Add(this.lineCap);
            hash.Add(this.innerJoin);
            hash.Add(this.normalizeOutput);

            if (!this.strokePattern.IsEmpty)
            {
                hash.Add(this.strokePattern.Length);
                ReadOnlySpan<float> strokePatternSpan = this.strokePattern.Span;
                for (int i = 0; i < strokePatternSpan.Length; i++)
                {
                    hash.Add(strokePatternSpan[i]);
                }
            }

            this.hashCode = hash.ToHashCode();
        }

        /// <inheritdoc/>
        public bool Equals(PenGeometryKey other)
        {
            if (this.penType is null || other.penType is null)
            {
                return this.penType is null && other.penType is null;
            }

            return this.penType == other.penType
                && this.strokeWidth == other.strokeWidth
                && this.miterLimit == other.miterLimit
                && this.innerMiterLimit == other.innerMiterLimit
                && this.arcDetailScale == other.arcDetailScale
                && this.lineJoin == other.lineJoin
                && this.lineCap == other.lineCap
                && this.innerJoin == other.innerJoin
                && this.normalizeOutput == other.normalizeOutput
                && this.strokePattern.Span.SequenceEqual(other.strokePattern.Span);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
            => obj is PenGeometryKey other && this.Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => this.hashCode;
    }
}
