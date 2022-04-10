// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.Reflection;
using Xunit.Sdk;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class ValidateDisposedMemoryAllocationsAttribute : BeforeAfterTestAttribute
    {
        private readonly int max = 0;

        public ValidateDisposedMemoryAllocationsAttribute()
            : this(0)
        {
        }

        public ValidateDisposedMemoryAllocationsAttribute(int max)
        {
            this.max = max;
            if (max > 0)
            {
                Debug.WriteLine("Needs fixing, we shoudl have Zero undisposed memory allocations.");
            }
        }

        public override void Before(MethodInfo methodUnderTest)
            => MemoryAllocatorValidator.MonitorAllocations();

        public override void After(MethodInfo methodUnderTest)
        {
            MemoryAllocatorValidator.ValidateAllocation(this.max);
            MemoryAllocatorValidator.StopMonitoringAllocations();
        }
    }
}
