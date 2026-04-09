// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Centralizes the conversion from configuration parallelism settings to partition counts and
/// <see cref="ParallelOptions"/> instances used by retained-scene CPU execution paths.
/// </summary>
internal static class ParallelExecutionHelper
{
    /// <summary>
    /// Computes the number of partitions to schedule for work constrained by a single work-item limit.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> leaves the degree of
    /// parallelism unbounded, so the work-item count is used as the effective partition cap.
    /// </param>
    /// <param name="workItemCount">The total number of work items available for partitioning.</param>
    /// <returns>The number of partitions to schedule.</returns>
    public static int GetPartitionCount(int maxDegreeOfParallelism, int workItemCount)
        => maxDegreeOfParallelism == -1 ? workItemCount : Math.Min(maxDegreeOfParallelism, workItemCount);

    /// <summary>
    /// Computes the number of partitions to schedule for work constrained by two independent limits.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> leaves the degree of
    /// parallelism unbounded, so only the supplied work limits constrain the partition count.
    /// </param>
    /// <param name="workItemCount">The total number of work items available for partitioning.</param>
    /// <param name="secondaryLimit">An additional caller-specific upper bound on useful partitions.</param>
    /// <returns>The number of partitions to schedule.</returns>
    public static int GetPartitionCount(int maxDegreeOfParallelism, int workItemCount, int secondaryLimit)
        => maxDegreeOfParallelism == -1
            ? Math.Min(workItemCount, secondaryLimit)
            : Math.Min(maxDegreeOfParallelism, Math.Min(workItemCount, secondaryLimit));

    /// <summary>
    /// Creates the <see cref="ParallelOptions"/> for a partitioned operation while preserving the
    /// special meaning of <c>-1</c> as unbounded parallelism.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> is propagated directly
    /// to <see cref="ParallelOptions.MaxDegreeOfParallelism"/>.
    /// </param>
    /// <param name="partitionCount">The computed number of useful partitions for the operation.</param>
    /// <returns>The <see cref="ParallelOptions"/> instance for the operation.</returns>
    public static ParallelOptions CreateParallelOptions(int maxDegreeOfParallelism, int partitionCount)
        => new() { MaxDegreeOfParallelism = maxDegreeOfParallelism == -1 ? -1 : partitionCount };
}
