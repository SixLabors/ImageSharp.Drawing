// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using Xunit.Abstractions;

namespace SixLabors.ImageSharp.Drawing.Tests.TestUtilities;

public class TestVector4 : IXunitSerializable
{
    public TestVector4()
    {
    }

    public TestVector4(float x, float y, float z, float w)
    {
        this.X = x;
        this.Y = y;
        this.Z = z;
        this.W = w;
    }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public float W { get; set; }

    public static implicit operator Vector4(TestVector4 d) => d?.AsVector() ?? default;

    public Vector4 AsVector() => new(this.X, this.Y, this.Z, this.W);

    public void Deserialize(IXunitSerializationInfo info)
    {
        this.X = info.GetValue<float>("x");
        this.Y = info.GetValue<float>("y");
        this.Z = info.GetValue<float>("z");
        this.W = info.GetValue<float>("w");
    }

    public void Serialize(IXunitSerializationInfo info)
    {
        info.AddValue("x", this.X);
        info.AddValue("y", this.Y);
        info.AddValue("z", this.Z);
        info.AddValue("w", this.W);
    }

    public override string ToString() => $"{this.AsVector()}";
}
