// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Preferred coverage preparation mode for a drawing operation.
/// </summary>
internal enum CoveragePreparationMode
{
    /// <summary>
    /// Backend chooses its default coverage preparation path.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Backend should use fallback coverage preparation.
    /// </summary>
    Fallback = 1
}
