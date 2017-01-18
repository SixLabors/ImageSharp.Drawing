using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Shaper2D
{
    internal static class ArrayExtensions
    {
        public static T[] Merge<T>(this T[] source1, T[] source2)
        {
            if (source2 == null)
            {
                return source1;
            }
            var target = new T[source1.Length + source2.Length];

            for (var i = 0; i < source1.Length; i++)
            {
                target[i] = source1[i];
            }
            for (var i = 0; i < source2.Length; i++)
            {
                target[i + source1.Length] = source2[i];
            }

            return target;
        }
    }
}
