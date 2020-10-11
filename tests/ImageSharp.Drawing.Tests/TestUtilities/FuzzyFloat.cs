// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using Xunit.Abstractions;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities
{
    /// <summary>
    /// Represents an inaccurate <see cref="float"/> value.
    /// </summary>
    public struct FuzzyFloat : IEquatable<float>, IXunitSerializable
    {
        public const float DefaultEpsilon = 1e-5f;
        
        private float value;
        private float min;
        private float max;
        private float eps;

        public FuzzyFloat(float value) 
            : this(value, DefaultEpsilon)
        {
        }

        public FuzzyFloat(float value, float eps)
        {
            this.value = value;
            this.eps = eps;
            this.min = value - eps;
            this.max = value + eps;
        }

        public static implicit operator float(FuzzyFloat x)  => x.value;
        
        public static implicit operator FuzzyFloat(float x) => new FuzzyFloat(x);
        
        public static implicit operator FuzzyFloat(int x) => new FuzzyFloat(x);
        
        public static implicit operator FuzzyFloat(double x) => new FuzzyFloat((float)x);
        public bool Equals(float x) => x >= this.min && x <= this.max;

        public override string ToString() => $"{value}±{eps}";
        
        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(value), this.value);
            info.AddValue(nameof(eps), this.eps);
        }
        
        public void Deserialize(IXunitSerializationInfo info)
        {
            this.value = info.GetValue<float>(nameof(value));
            this.eps = info.GetValue<float>(nameof(eps));
            this.min = value - eps;
            this.max = value + eps;
        }
    }
}