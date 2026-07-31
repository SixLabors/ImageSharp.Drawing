// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Maps native WebGPU error classifications to the public error model.
/// </summary>
internal static class WebGPUErrorTypeMapper
{
    /// <summary>
    /// Maps a native error classification to its public equivalent.
    /// </summary>
    /// <param name="errorType">The native error classification.</param>
    /// <returns>The public error classification.</returns>
    public static WebGPUErrorType ToPublic(WGPUErrorType errorType)
        => errorType switch
        {
            WGPUErrorType.NoError => WebGPUErrorType.NoError,
            WGPUErrorType.Validation => WebGPUErrorType.Validation,
            WGPUErrorType.OutOfMemory => WebGPUErrorType.OutOfMemory,
            WGPUErrorType.Internal => WebGPUErrorType.Internal,
            _ => WebGPUErrorType.Unknown
        };
}
