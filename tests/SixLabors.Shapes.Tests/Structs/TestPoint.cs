// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.Primitives;
using Xunit.Abstractions;

namespace SixLabors.Shapes.Tests
{
    [Serializable]
    public class TestPoint : IXunitSerializable
    {
        public TestPoint()
        {
        }

        public TestPoint(float x, float y)
        {
            this.X = x;
            this.Y = y;
        }

        public float X { get; private set; }

        public float Y { get; private set; }

        public void Deserialize(IXunitSerializationInfo info)
        {
            this.X = (float)info.GetValue<float>("X");
            this.Y = (float)info.GetValue<float>("Y");
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue("X", this.X);
            info.AddValue("Y", this.Y);
        }

        public override string ToString()
        {
            return $"({this.X}, {this.Y})";
        }

        public static implicit operator PointF(TestPoint p)
        {
            return new Vector2(p.X, p.Y);
        }

        public static implicit operator TestPoint(PointF p)
        {
            return new TestPoint(p.X, p.Y);
        }
    }
}
