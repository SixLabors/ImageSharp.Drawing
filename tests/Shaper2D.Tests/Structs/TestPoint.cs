using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace Shaper2D.Tests
{
    using System.Numerics;

    public class TestPoint : IXunitSerializable
    {
        protected TestPoint() { }

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
            info.AddValue("X", X);
            info.AddValue("Y", Y);
        }

        public override string ToString()
        {
            return $"({X}, {Y})";
        }

        public static implicit operator Vector2(TestPoint p)
        {
            return new Vector2(p.X, p.Y);
        }
        public static implicit operator Point(TestPoint p)
        {
            return new Point(p.X, p.Y);
        }
        public static implicit operator TestPoint(Point p)
        {
            return new TestPoint(p.X, p.Y);
        }
    }
}
