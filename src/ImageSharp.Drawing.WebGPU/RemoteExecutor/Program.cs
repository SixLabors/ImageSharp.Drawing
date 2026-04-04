// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Entry point for child processes spawned by <see cref="RemoteExecutor"/>.
/// Dispatches to the requested probe method by name.
/// Adapted from Microsoft.DotNet.RemoteExecutor (MIT license).
/// </summary>
internal static class Program
{
    /// <summary>
    /// Dispatches the requested probe method inside the child process.
    /// </summary>
    private static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: {0} methodName", typeof(Program).Assembly.GetName().Name);
            return -1;
        }

        string methodName = args[0];

        return methodName switch
        {
            nameof(WebGPURuntime.ProbeComputePipelineSupport) => WebGPURuntime.ProbeComputePipelineSupport(),
            _ => -1
        };
    }
}
