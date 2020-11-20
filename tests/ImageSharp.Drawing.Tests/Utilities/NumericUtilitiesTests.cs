// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Linq;
using SixLabors.ImageSharp.Drawing.Utilities;
using Xunit;

namespace SixLabors.ImageSharp.Drawing.Tests.Drawing.Utils
{
    public class NumericUtilitiesTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(13)]
        [InlineData(130)]
        public void AddToAllElements(int length)
        {
            float[] values = Enumerable.Range(0, length).Select(v => (float)v).ToArray();

            const float val = 13.4321f;
            float[] expected = values.Select(x => x + val).ToArray();
            values.AsSpan().AddToAllElements(val);
            
            Assert.Equal(expected, values);
        } 
    }
}