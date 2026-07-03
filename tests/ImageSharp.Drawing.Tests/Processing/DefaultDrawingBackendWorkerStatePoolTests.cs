// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SixLabors.ImageSharp.Drawing.Tests.Processing;

/// <summary>
/// Verifies the lifetime behavior of the CPU backend's pooled worker states: rendering
/// retains a bounded set of allocator buffers across flushes, and the Gen2 idle trim
/// returns every buffer to the allocator once rendering stops.
/// </summary>
public class DefaultDrawingBackendWorkerStatePoolTests
{
    /// <summary>
    /// The worker-state pool is keyed per pixel type. Using a pixel format no other test
    /// draws with isolates this test's pool and its rent flag from concurrently executing
    /// tests, making the idle-trim observation deterministic.
    /// </summary>
    [Fact]
    public void WorkerStatePool_ReturnsAllBuffersToAllocator_WhenIdleAcrossGen2Collections()
    {
        TestMemoryAllocator allocator = new();
        Configuration configuration = Configuration.Default.Clone();
        configuration.MemoryAllocator = allocator;

        using (Image<Rgb48> image = new(configuration, 128, 128))
        {
            image.Mutate(context => context.Paint(
                canvas => canvas.Fill(Brushes.Solid(Color.Red), new RectanglePolygon(new RectangleF(8, 8, 64, 64)))));
        }

        // The first collection consumes the rent flag set by the flush above; the second
        // observes an idle Gen2 and trims the pool, disposing the retained worker states.
        // Extra iterations keep the test robust if the finalizer thread lags.
        for (int i = 0; i < 5 && CountOutstanding(allocator) != 0; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.Equal(0, CountOutstanding(allocator));
    }

    /// <summary>
    /// Counts allocations that have not been returned, matching allocation and return
    /// entries by buffer identity so double returns cannot mask a leak.
    /// </summary>
    /// <param name="allocator">The tracking allocator used by the test.</param>
    /// <returns>The number of live allocations.</returns>
    private static int CountOutstanding(TestMemoryAllocator allocator)
    {
        Dictionary<int, int> outstanding = [];
        foreach (TestMemoryAllocator.AllocationRequest request in allocator.AllocationLog)
        {
            outstanding[request.HashCodeOfBuffer] = outstanding.TryGetValue(request.HashCodeOfBuffer, out int count) ? count + 1 : 1;
        }

        int live = allocator.AllocationLog.Count;
        foreach (TestMemoryAllocator.ReturnRequest request in allocator.ReturnLog)
        {
            if (outstanding.TryGetValue(request.HashCodeOfBuffer, out int count) && count > 0)
            {
                outstanding[request.HashCodeOfBuffer] = count - 1;
                live--;
            }
        }

        return live;
    }
}
