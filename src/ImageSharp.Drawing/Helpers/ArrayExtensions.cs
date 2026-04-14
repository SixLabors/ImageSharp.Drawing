// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Helpers;

/// <summary>
/// Extension methods for arrays.
/// </summary>
internal static class ArrayExtensions
{
    /// <summary>
    /// Concatenates two arrays into one.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source1">The first source array.</param>
    /// <param name="source2">The second source array.</param>
    /// <returns>
    /// A new array containing the elements of both source arrays, or <paramref name="source1"/>
    /// when <paramref name="source2"/> is empty.
    /// </returns>
    public static T[] Concat<T>(this T[] source1, T[] source2)
    {
        if (source2 is null || source2.Length == 0)
        {
            return source1;
        }

        T[] target = new T[source1.Length + source2.Length];
        source1.AsSpan().CopyTo(target);
        source2.AsSpan().CopyTo(target.AsSpan(source1.Length));

        return target;
    }
}
