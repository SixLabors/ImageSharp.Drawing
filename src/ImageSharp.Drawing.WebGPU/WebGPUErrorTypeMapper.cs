// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

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
    public static WebGPUErrorType ToPublic(ErrorType errorType)
        => errorType switch
        {
            ErrorType.NoError => WebGPUErrorType.NoError,
            ErrorType.Validation => WebGPUErrorType.Validation,
            ErrorType.OutOfMemory => WebGPUErrorType.OutOfMemory,
            ErrorType.Internal => WebGPUErrorType.Internal,
            _ => WebGPUErrorType.Unknown
        };
}
