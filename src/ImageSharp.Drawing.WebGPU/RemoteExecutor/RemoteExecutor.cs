// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;
using System.Runtime.InteropServices;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Minimal remote executor that invokes a named method in a child process.
/// The child process entry point (<see cref="Program.Main"/>) dispatches to
/// the requested method by name; no reflection is used.
/// Adapted from Microsoft.DotNet.RemoteExecutor (MIT license).
/// </summary>
internal static class RemoteExecutor
{
    private static readonly string? AssemblyPath;
    private static readonly string? HostRunner;
    private static readonly string? RuntimeConfigPath;
    private static readonly string? DepsJsonPath;

    static RemoteExecutor()
    {
        if (!IsSupported)
        {
            return;
        }

        string? processFileName = Process.GetCurrentProcess().MainModule?.FileName;
        if (processFileName is null)
        {
            return;
        }

        string baseDir = AppContext.BaseDirectory;
        string assemblyName = typeof(RemoteExecutor).Assembly.GetName().Name!;
        AssemblyPath = IOPath.Combine(baseDir, assemblyName + ".dll");
        if (!File.Exists(AssemblyPath))
        {
            return;
        }

        HostRunner = processFileName;
        string hostName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";

        if (!IOPath.GetFileName(HostRunner).Equals(hostName, StringComparison.OrdinalIgnoreCase))
        {
            // Walk up from the runtime directory to find the dotnet host executable.
            // The runtime directory is typically:
            //   <dotnet_root>/shared/Microsoft.NETCore.App/<version>/
            // so dotnet.exe is 3-4 levels up depending on trailing separator.
            string? directory = RuntimeEnvironment.GetRuntimeDirectory();
            for (int i = 0; i < 4 && directory is not null; i++)
            {
                directory = IOPath.GetDirectoryName(directory);
                if (directory is not null)
                {
                    string dotnetExe = IOPath.Combine(directory, hostName);
                    if (File.Exists(dotnetExe))
                    {
                        HostRunner = dotnetExe;
                        break;
                    }
                }
            }
        }

        string runtimeConfigCandidate = IOPath.Combine(baseDir, assemblyName + ".runtimeconfig.json");
        string depsJsonCandidate = IOPath.Combine(baseDir, assemblyName + ".deps.json");

        RuntimeConfigPath = File.Exists(runtimeConfigCandidate) ? runtimeConfigCandidate : null;
        DepsJsonPath = File.Exists(depsJsonCandidate) ? depsJsonCandidate : null;
    }

    /// <summary>
    /// Gets a value indicating whether this remote executor is supported on the current platform.
    /// </summary>
    internal static bool IsSupported { get; } =
        !RuntimeInformation.IsOSPlatform(OSPlatform.Create("IOS")) &&
        !RuntimeInformation.IsOSPlatform(OSPlatform.Create("ANDROID")) &&
        !RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER")) &&
        !RuntimeInformation.IsOSPlatform(OSPlatform.Create("WASI")) &&
        Environment.GetEnvironmentVariable("DOTNET_REMOTEEXECUTOR_SUPPORTED") != "0";

    /// <summary>
    /// Invokes the specified static method in a child process and returns its exit code.
    /// The method name is dispatched by <see cref="Program.Main"/> via a switch statement,
    /// so no reflection is needed in the child process.
    /// </summary>
    /// <param name="method">A static method returning <see cref="int"/> (the exit code).</param>
    /// <param name="timeoutMilliseconds">Maximum time to wait for the child process.</param>
    /// <returns>The exit code from the child process, or -1 on failure.</returns>
    internal static int Invoke(Func<int> method, int timeoutMilliseconds = 30_000)
    {
        if (!IsSupported || AssemblyPath is null || HostRunner is null)
        {
            return -1;
        }

        string methodName = method.Method.Name;

        string args = "exec";
        if (RuntimeConfigPath is not null)
        {
            args += $" --runtimeconfig \"{RuntimeConfigPath}\"";
        }

        if (DepsJsonPath is not null)
        {
            args += $" --depsfile \"{DepsJsonPath}\"";
        }

        args += $" \"{AssemblyPath}\" \"{methodName}\"";

        ProcessStartInfo psi = new()
        {
            FileName = HostRunner,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Remove profiler environment variables from child process.
        psi.Environment.Remove("Cor_Profiler");
        psi.Environment.Remove("Cor_Enable_Profiling");
        psi.Environment.Remove("CoreClr_Profiler");
        psi.Environment.Remove("CoreClr_Enable_Profiling");

        try
        {
            using Process? process = Process.Start(psi);
            if (process is null)
            {
                return -1;
            }

            if (!process.WaitForExit(timeoutMilliseconds))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Ignore cleanup errors.
                }

                return -1;
            }

            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
