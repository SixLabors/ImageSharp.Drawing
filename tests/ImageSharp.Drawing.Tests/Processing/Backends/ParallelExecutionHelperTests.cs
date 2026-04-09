// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing.Backends;

public class ParallelExecutionHelperTests
{
    [Theory]
    [InlineData(-1, 8, 8)]
    [InlineData(4, 8, 4)]
    [InlineData(1, 8, 1)]
    public void GetPartitionCountSingleLimit(int maxDegreeOfParallelism, int workItemCount, int expected)
        => Assert.Equal(expected, ParallelExecutionHelper.GetPartitionCount(maxDegreeOfParallelism, workItemCount));

    [Theory]
    [InlineData(-1, 8, 3, 3)]
    [InlineData(8, 8, 3, 3)]
    [InlineData(2, 8, 3, 2)]
    public void GetPartitionCountDualLimit(int maxDegreeOfParallelism, int workItemCount, int secondaryLimit, int expected)
        => Assert.Equal(expected, ParallelExecutionHelper.GetPartitionCount(maxDegreeOfParallelism, workItemCount, secondaryLimit));

    [Theory]
    [InlineData(-1, 8, -1)]
    [InlineData(4, 8, 8)]
    [InlineData(2, 2, 2)]
    public void CreateParallelOptionsPreservesUnboundedSetting(int maxDegreeOfParallelism, int partitionCount, int expected)
        => Assert.Equal(expected, ParallelExecutionHelper.CreateParallelOptions(maxDegreeOfParallelism, partitionCount).MaxDegreeOfParallelism);
}
