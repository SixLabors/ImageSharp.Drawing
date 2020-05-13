// Copyright (c) Six Labors and contributors.
// Licensed under the GNU Affero General Public License, Version 3.

using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing
{
    /// <summary>
    /// Utility methods for numeric primitives.
    /// </summary>
    internal static class NumberUtilities
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ClampFloat(float value, float min, float max)
        {
            if (value >= max)
            {
                return max;
            }

            if (value <= min)
            {
                return min;
            }

            return value;
        }
    }
}
