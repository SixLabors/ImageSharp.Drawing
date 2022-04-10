// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using SixLabors.ImageSharp.Diagnostics;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    public static class MemoryAllocatorValidator
    {
        private static List<string> traces = new List<string>();

        public static void MonitorStackTraces() => MemoryDiagnostics.UndisposedAllocation += MemoryDiagnostics_UndisposedAllocation;

        public static List<string> GetStackTraces()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            return traces;
        }

        private static void MemoryDiagnostics_UndisposedAllocation(string allocationStackTrace)
        {
            lock (traces)
            {
                traces.Add(allocationStackTrace);
            }
        }

        public static TestMemoryAllocatorDisposable MonitorAllocations()
        {
            MemoryDiagnostics.Current = new();
            return new TestMemoryAllocatorDisposable();
        }

        public static void StopMonitoringAllocations() => MemoryDiagnostics.Current = null;

        public static void ValidateAllocation(int max = 0)
        {
            var count = MemoryDiagnostics.TotalUndisposedAllocationCount;

            var pass = count <= max;
            Assert.True(pass, $"Expected a max of {max} undisposed buffers but found {count}");

            if (count > 0)
            {
                Debug.WriteLine("We should have Zero undisposed memory allocations.");
            }
        }

        public struct TestMemoryAllocatorDisposable : IDisposable
        {
            public void Dispose()
                => StopMonitoringAllocations();

            public void Validate(int maxAllocations = 0)
                => ValidateAllocation(maxAllocations);
        }
    }
}
