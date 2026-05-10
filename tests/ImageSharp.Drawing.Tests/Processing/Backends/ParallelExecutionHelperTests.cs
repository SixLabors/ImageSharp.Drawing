// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class ParallelExecutionHelperTests
{
    [Theory]
    [InlineData(4, 8, 4)]
    [InlineData(1, 8, 1)]
    public void GetPartitionCountSingleLimit(int maxDegreeOfParallelism, int workItemCount, int expected)
        => Assert.Equal(expected, ParallelExecutionHelper.GetPartitionCount(maxDegreeOfParallelism, workItemCount));

    [Fact]
    public void GetPartitionCountSingleLimit_UnboundedSettingIsCappedToProcessorCount()
        => Assert.Equal(
            Math.Min(Environment.ProcessorCount, 8),
            ParallelExecutionHelper.GetPartitionCount(-1, 8));

    [Theory]
    [InlineData(8, 8, 3, 3)]
    [InlineData(2, 8, 3, 2)]
    public void GetPartitionCountDualLimit(int maxDegreeOfParallelism, int workItemCount, int secondaryLimit, int expected)
        => Assert.Equal(expected, ParallelExecutionHelper.GetPartitionCount(maxDegreeOfParallelism, workItemCount, secondaryLimit));

    [Fact]
    public void GetPartitionCountDualLimit_UnboundedSettingIsCappedToProcessorCount()
        => Assert.Equal(
            Math.Min(Environment.ProcessorCount, 3),
            ParallelExecutionHelper.GetPartitionCount(-1, 8, 3));

    [Theory]
    [InlineData(-1, 8, -1)]
    [InlineData(4, 8, 4)]
    [InlineData(4, 2, 2)]
    [InlineData(2, 2, 2)]
    public void CreateParallelOptions_MatchesParallelOptionsContract(int maxDegreeOfParallelism, int partitionCount, int expected)
        => Assert.Equal(expected, ParallelExecutionHelper.CreateParallelOptions(maxDegreeOfParallelism, partitionCount).MaxDegreeOfParallelism);
}
