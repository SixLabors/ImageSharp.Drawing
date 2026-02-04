// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Utilities;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Utils;

public class ThreadLocalBlenderBuffersTests
{
    private readonly TestMemoryAllocator memoryAllocator = new();

    [Fact]
    public void CreatesPerThreadUniqueInstances()
    {
        using ThreadLocalBlenderBuffers<Rgb24> buffers = new(this.memoryAllocator, 100);

        SemaphoreSlim allSetSemaphore = new(2);

        Thread thread1 = new(() =>
        {
            Span<float> ams = buffers.AmountSpan;
            Span<Rgb24> overlays = buffers.OverlaySpan;

            ams[0] = 10;
            overlays[0] = new Rgb24(10, 10, 10);

            allSetSemaphore.Release(1);
            allSetSemaphore.Wait();

            Assert.Equal(10, buffers.AmountSpan[0]);
            Assert.Equal(10, buffers.OverlaySpan[0].R);
        });

        Thread thread2 = new(() =>
        {
            Span<float> ams = buffers.AmountSpan;
            Span<Rgb24> overlays = buffers.OverlaySpan;

            ams[0] = 20;
            overlays[0] = new Rgb24(20, 20, 20);

            allSetSemaphore.Release(1);
            allSetSemaphore.Wait();

            Assert.Equal(20, buffers.AmountSpan[0]);
            Assert.Equal(20, buffers.OverlaySpan[0].R);
        });

        thread1.Start();
        thread2.Start();
        thread1.Join();
        thread2.Join();
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 3)]
    [InlineData(true, 1)]
    [InlineData(true, 3)]
    public void Dispose_ReturnsAllBuffers(bool amountBufferOnly, int threadCount)
    {
        ThreadLocalBlenderBuffers<Rgb24> buffers = new(this.memoryAllocator, 100, amountBufferOnly);

        void RunThread()
        {
            buffers.AmountSpan[0] = 42;
        }

        Thread[] threads = new Thread[threadCount];
        for (int i = 0; i < threadCount; i++)
        {
            threads[i] = new Thread(RunThread);
            threads[i].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        buffers.Dispose();

        int expectedReturnCount = amountBufferOnly ? threadCount : 2 * threadCount;
        Assert.Equal(expectedReturnCount, this.memoryAllocator.ReturnLog.Count);
    }
}
