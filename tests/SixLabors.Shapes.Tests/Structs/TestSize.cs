using SixLabors.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit.Abstractions;

namespace SixLabors.Shapes.Tests
{
    public class TestSize : IXunitSerializable
    {
        public TestSize() { }

        public TestSize(float width, float height)
        {
            this.Width = width;
            this.Height = height;
        }

        public float Width { get; private set; }
        public float Height { get; private set; }

        public void Deserialize(IXunitSerializationInfo info)
        {
            this.Width = (float)info.GetValue<float>("width");
            this.Height = (float)info.GetValue<float>("height");
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue("width", Width);
            info.AddValue("height", Height);
        }

        public static implicit operator SizeF(TestSize p)
        {
            return new SizeF(p.Width, p.Height);
        }
        public static implicit operator TestSize(SizeF p)
        {
            return new TestSize(p.Width, p.Height);
        }
        public override string ToString()
        {
            return $"{Width}x{Height}";
        }
    }
}
