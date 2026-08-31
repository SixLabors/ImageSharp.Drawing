// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.FontGenerator;

/// <summary>
/// Resolves the directories the generator writes to. The generator takes no output path: the font data
/// is source, so it is written where the library compiles it from, and the sheets are written to the
/// repository artifacts directory. Both are found by walking up from the running assembly to the
/// solution file.
/// </summary>
internal static class RepositoryPaths
{
    private const string SolutionFileName = "ImageSharp.Drawing.slnx";
    private const string LibraryFontsRelativePath = @"src\ImageSharp.Drawing\Barcodes\Fonts";
    private const string ArtifactsRelativePath = @"artifacts\fonts";

    private static readonly Lazy<string> SolutionDirectoryFullPathLazy = new(GetSolutionDirectoryFullPathImpl);

    /// <summary>
    /// Gets the directory the library compiles the generated font data from.
    /// </summary>
    public static string LibraryFonts => GetFullPath(LibraryFontsRelativePath);

    /// <summary>
    /// Gets the directory the fonts and the proof, grid and comparison sheets are written to.
    /// </summary>
    public static string Artifacts => GetFullPath(ArtifactsRelativePath);

    private static string SolutionDirectoryFullPath => SolutionDirectoryFullPathLazy.Value;

    private static string GetSolutionDirectoryFullPathImpl()
    {
        string assemblyLocation = AppContext.BaseDirectory;
        DirectoryInfo? directory = new FileInfo(assemblyLocation).Directory
            ?? throw new IOException($"Unable to find the SixLabors solution directory from {assemblyLocation}!");

        while (!directory.EnumerateFiles(SolutionFileName).Any())
        {
            directory = directory.Parent;
            if (directory is null)
            {
                throw new IOException($"Unable to find the SixLabors solution directory from {assemblyLocation}!");
            }
        }

        return directory.FullName;
    }

    private static string GetFullPath(string relativePath)
        => System.IO.Path.Combine(SolutionDirectoryFullPath, relativePath)
            .Replace('\\', System.IO.Path.DirectorySeparatorChar);
}
