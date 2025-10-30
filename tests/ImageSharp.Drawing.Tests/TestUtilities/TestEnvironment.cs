// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Reflection;
using System.Runtime.InteropServices;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests;

public static partial class TestEnvironment
{
    private const string ImageSharpSolutionFileName = "ImageSharp.Drawing.sln";

    private const string InputImagesRelativePath = @"tests\Images\Input";

    private const string ActualOutputDirectoryRelativePath = @"tests\Images\ActualOutput";

    private const string ReferenceOutputDirectoryRelativePath = @"tests\Images\ReferenceOutput";

    private const string ToolsDirectoryRelativePath = @"tests\Images\External\tools";

    private static readonly Lazy<string> SolutionDirectoryFullPathLazy = new(GetSolutionDirectoryFullPathImpl);

    private static readonly Lazy<string> NetCoreVersionLazy = new(GetNetCoreVersion);

    internal static bool IsFramework => RuntimeInformation.FrameworkDescription.StartsWith(".NET Framework");

    /// <summary>
    /// Gets the .NET Core version, if running on .NET Core, otherwise returns an empty string.
    /// </summary>
    internal static string NetCoreVersion => NetCoreVersionLazy.Value;

    /// <summary>
    /// Gets a value indicating whether test execution runs on CI.
    /// </summary>
#if ENV_CI
    internal static bool RunsOnCI => true;
#else
    internal static bool RunsOnCI => false;
#endif

    /// <summary>
    /// Gets a value indicating whether test execution is running with code coverage testing enabled.
    /// </summary>
#if ENV_CODECOV
    internal static bool RunsWithCodeCoverage => true;
#else
    internal static bool RunsWithCodeCoverage => false;
#endif

    internal static string SolutionDirectoryFullPath => SolutionDirectoryFullPathLazy.Value;

    private static string GetSolutionDirectoryFullPathImpl()
    {
        string assemblyLocation = typeof(TestEnvironment).GetTypeInfo().Assembly.Location;

        FileInfo assemblyFile = new(assemblyLocation);

        DirectoryInfo directory = assemblyFile.Directory;

        while (!directory.EnumerateFiles(ImageSharpSolutionFileName).Any())
        {
            try
            {
                directory = directory.Parent;
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Unable to find ImageSharp solution directory from {assemblyLocation} because of {ex.GetType().Name}!",
                    ex);
            }

            if (directory == null)
            {
                throw new Exception($"Unable to find ImageSharp solution directory from {assemblyLocation}!");
            }
        }

        return directory.FullName;
    }

    public static string GetFullPath(string relativePath) =>
        IOPath.Combine(SolutionDirectoryFullPath, relativePath)
        .Replace('\\', IOPath.DirectorySeparatorChar);

    /// <summary>
    /// Gets the correct full path to the Input Images directory.
    /// </summary>
    internal static string InputImagesDirectoryFullPath => GetFullPath(InputImagesRelativePath);

    /// <summary>
    /// Gets the correct full path to the Actual Output directory. (To be written to by the test cases.)
    /// </summary>
    internal static string ActualOutputDirectoryFullPath => GetFullPath(ActualOutputDirectoryRelativePath);

    /// <summary>
    /// Gets the correct full path to the Expected Output directory. (To compare the test results to.)
    /// </summary>
    internal static string ReferenceOutputDirectoryFullPath => GetFullPath(ReferenceOutputDirectoryRelativePath);

    internal static string ToolsDirectoryFullPath => GetFullPath(ToolsDirectoryRelativePath);

    internal static string GetReferenceOutputFileName(string actualOutputFileName) =>
        actualOutputFileName.Replace("ActualOutput", @"ReferenceOutput").Replace('\\', IOPath.DirectorySeparatorChar);

    internal static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    internal static bool IsMono => Type.GetType("Mono.Runtime") != null; // https://stackoverflow.com/a/721194

    internal static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    internal static bool Is64BitProcess => IntPtr.Size == 8;

    /// <summary>
    /// Creates the image output directory.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="pathParts">The path parts.</param>
    /// <returns>
    /// The <see cref="string"/>.
    /// </returns>
    internal static string CreateOutputDirectory(string path, params string[] pathParts)
    {
        path = IOPath.Combine(ActualOutputDirectoryFullPath, path);

        if (pathParts != null && pathParts.Length > 0)
        {
            path = IOPath.Combine(path, IOPath.Combine(pathParts));
        }

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }

    /// <summary>
    /// Solution borrowed from:
    /// https://github.com/dotnet/BenchmarkDotNet/issues/448#issuecomment-308424100
    /// </summary>
    private static string GetNetCoreVersion()
    {
        Assembly assembly = typeof(System.Runtime.GCSettings).GetTypeInfo().Assembly;
        string[] assemblyPath = assembly.Location.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        int netCoreAppIndex = Array.IndexOf(assemblyPath, "Microsoft.NETCore.App");
        if (netCoreAppIndex > 0 && netCoreAppIndex < assemblyPath.Length - 2)
        {
            return assemblyPath[netCoreAppIndex + 1];
        }

        return string.Empty;
    }
}
