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
        public static readonly float DefaultEpsilon = 1e-5f;

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

        public static implicit operator float(FuzzyFloat x) => x.value;

        public static implicit operator FuzzyFloat(float x) => new FuzzyFloat(x);

        public static implicit operator FuzzyFloat(int x) => new FuzzyFloat(x);

        public static implicit operator FuzzyFloat(double x) => new FuzzyFloat((float)x);

        public bool Equals(float x) => x >= this.min && x <= this.max;

        public override string ToString() => $"{this.value}±{this.eps}";

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(this.value), this.value);
            info.AddValue(nameof(this.eps), this.eps);
        }

        public void Deserialize(IXunitSerializationInfo info)
        {
            this.value = info.GetValue<float>(nameof(this.value));
            this.eps = info.GetValue<float>(nameof(this.eps));
            this.min = this.value - this.eps;
            this.max = this.value + this.eps;
        }

        public static FuzzyFloat operator +(FuzzyFloat a, float b) => new FuzzyFloat(a.value + b, a.eps);

        public static FuzzyFloat operator -(FuzzyFloat a, float b) => new FuzzyFloat(a.value - b, a.eps);
    }
}
