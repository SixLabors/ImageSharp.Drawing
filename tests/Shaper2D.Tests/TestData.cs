using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shaper2D.Tests
{
    public static class TestData
    {
        static Random rand = new Random();

        public static Size RandomSize()
        {
             return new Size((float)(rand.NextDouble() * rand.Next()), (float)(rand.NextDouble() * rand.Next()));
        }
    }
}
