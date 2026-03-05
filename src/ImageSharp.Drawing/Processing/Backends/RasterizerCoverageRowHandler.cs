// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Delegate invoked for each emitted non-zero coverage span.
/// </summary>
/// <param name="y">The destination y coordinate.</param>
/// <param name="startX">The first x coordinate represented by <paramref name="coverage"/>.</param>
/// <param name="coverage">Non-zero coverage values starting at <paramref name="startX"/>.</param>
internal delegate void RasterizerCoverageRowHandler(int y, int startX, Span<float> coverage);
