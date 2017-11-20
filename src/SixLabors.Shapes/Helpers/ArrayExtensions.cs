// Copyright (c) Six Labors and contributors.
// Licensed under the Apache License, Version 2.0.

namespace SixLabors.Shapes
{
    /// <summary>
    /// Extensions on arrays.
    /// </summary>
    internal static class ArrayExtensions
    {
        /// <summary>
        /// Merges the specified source2.
        /// </summary>
        /// <typeparam name="T">the type of the array</typeparam>
        /// <param name="source1">The source1.</param>
        /// <param name="source2">The source2.</param>
        /// <returns>the Merged arrays</returns>
        public static T[] Merge<T>(this T[] source1, T[] source2)
        {
            if (source2 == null)
            {
                return source1;
            }

            T[] target = new T[source1.Length + source2.Length];

            for (int i = 0; i < source1.Length; i++)
            {
                target[i] = source1[i];
            }

            for (int i = 0; i < source2.Length; i++)
            {
                target[i + source1.Length] = source2[i];
            }

            return target;
        }
    }
}
