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
    /// The configured maximum degree of parallelism. A value of <c>-1</c> leaves the runtime
    /// parallelism cap unbounded, but partition planning remains capped to
    /// <see cref="Environment.ProcessorCount"/> to avoid excessive fan-out.
    /// </param>
    /// <param name="workItemCount">The total number of work items available for partitioning.</param>
    /// <returns>The number of partitions to schedule.</returns>
    public static int GetPartitionCount(int maxDegreeOfParallelism, int workItemCount)
        => Math.Min(GetPartitionLimit(maxDegreeOfParallelism), workItemCount);

    /// <summary>
    /// Computes the number of partitions to schedule for work constrained by two independent limits.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> leaves the runtime
    /// parallelism cap unbounded, but partition planning remains capped to
    /// <see cref="Environment.ProcessorCount"/> to avoid excessive fan-out.
    /// </param>
    /// <param name="workItemCount">The total number of work items available for partitioning.</param>
    /// <param name="secondaryLimit">An additional caller-specific upper bound on useful partitions.</param>
    /// <returns>The number of partitions to schedule.</returns>
    public static int GetPartitionCount(int maxDegreeOfParallelism, int workItemCount, int secondaryLimit)
        => Math.Min(GetPartitionLimit(maxDegreeOfParallelism), Math.Min(workItemCount, secondaryLimit));

    /// <summary>
    /// Creates the <see cref="ParallelOptions"/> for a partitioned operation while preserving the
    /// special meaning of <c>-1</c> in <see cref="ParallelOptions.MaxDegreeOfParallelism"/>.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> is propagated directly
    /// to <see cref="ParallelOptions.MaxDegreeOfParallelism"/>; positive values are capped to the
    /// smaller of the configured limit and the useful partition count.
    /// </param>
    /// <param name="partitionCount">The computed number of useful partitions for the operation.</param>
    /// <returns>The <see cref="ParallelOptions"/> instance for the operation.</returns>
    public static ParallelOptions CreateParallelOptions(int maxDegreeOfParallelism, int partitionCount)
        => new() { MaxDegreeOfParallelism = Math.Min(maxDegreeOfParallelism, partitionCount) };

    /// <summary>
    /// Computes the internal partition-planning cap for the configured parallelism setting.
    /// </summary>
    /// <param name="maxDegreeOfParallelism">
    /// The configured maximum degree of parallelism. A value of <c>-1</c> keeps the runtime
    /// parallelism setting unbounded, but partition planning is capped to
    /// <see cref="Environment.ProcessorCount"/>.
    /// </param>
    /// <returns>The maximum number of partitions to plan for.</returns>
    private static int GetPartitionLimit(int maxDegreeOfParallelism)
        => maxDegreeOfParallelism == -1 ? Environment.ProcessorCount : maxDegreeOfParallelism;
}
