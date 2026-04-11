// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Extension methods for arrays.
/// </summary>
internal static class ArrayExtensions
{
    /// <summary>
    /// Merges two arrays into one.
    /// </summary>
    /// <typeparam name="T">the type of the array</typeparam>
    /// <param name="source1">The first source array.</param>
    /// <param name="source2">The second source array.</param>
    /// <returns>
    /// A new array containing the elements of both source arrays.
    /// </returns>
    public static T[] Merge<T>(this T[] source1, T[] source2)
    {
        if (source2 is null || source2.Length == 0)
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
