// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SixLabors.Fonts;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests
{
    /// <summary>
    /// A test image file.
    /// </summary>
    public static class TestFontUtilities
    {
        /// <summary>
        /// Gets a font with the given name and size.
        /// </summary>
        /// <param name="name">The name of the font.</param>
        /// <param name="size">The font size.</param>
        /// <returns>The <see cref="Font"/></returns>
        public static Font GetFont(string name, float size)
            => GetFont(new FontCollection(), name, size);

        /// <summary>
        /// Gets a font with the given name and size.
        /// </summary>
        /// <param name="collection">The collection to add the font to</param>
        /// <param name="name">The name of the font.</param>
        /// <param name="size">The font size.</param>
        /// <returns>The <see cref="Font"/></returns>
        public static Font GetFont(FontCollection collection, string name, float size)
            => collection.Add(GetPath(name)).CreateFont(size);

        /// <summary>
        /// The formats directory.
        /// </summary>
        private static readonly string FontsDirectory = GetFontsDirectory();

        /// <summary>
        /// Gets the full qualified path to the file.
        /// </summary>
        /// <param name="file">The file path.</param>
        /// <returns>
        /// The <see cref="string"/>.
        /// </returns>
        public static string GetPath(string file) => IOPath.Combine(FontsDirectory, file);

        /// <summary>
        /// Gets the correct path to the formats directory.
        /// </summary>
        /// <returns>
        /// The <see cref="string"/>.
        /// </returns>
        private static string GetFontsDirectory()
        {
            var directories = new List<string>
            {
                 "TestFonts/", // Here for code coverage tests.
                 "tests/ImageSharp.Drawing.Tests/TestFonts/", // from travis/build script
                 "../../../../../ImageSharp.Drawing.Tests/TestFonts/", // from Sandbox46
                 "../../../../TestFonts/"
            };

            directories = directories.SelectMany(x => new[]
                                     {
                                         IOPath.GetFullPath(x)
                                     }).ToList();

            AddFormatsDirectoryFromTestAssemblyPath(directories);

            string directory = directories.Find(Directory.Exists);

            if (directory != null)
            {
                return directory;
            }

            throw new System.Exception($"Unable to find Fonts directory at any of these locations [{string.Join(", ", directories)}]");
        }

        /// <summary>
        /// The path returned by Path.GetFullPath(x) can be relative to dotnet framework directory
        /// in certain scenarios like dotTrace test profiling.
        /// This method calculates and adds the format directory based on the ImageSharp.Tests assembly location.
        /// </summary>
        /// <param name="directories">The directories list</param>
        private static void AddFormatsDirectoryFromTestAssemblyPath(List<string> directories)
        {
            string assemblyLocation = typeof(TestFile).GetTypeInfo().Assembly.Location;
            assemblyLocation = IOPath.GetDirectoryName(assemblyLocation);

            if (assemblyLocation != null)
            {
                string dirFromAssemblyLocation = IOPath.Combine(assemblyLocation, "../../../TestFonts/");
                dirFromAssemblyLocation = IOPath.GetFullPath(dirFromAssemblyLocation);
                directories.Add(dirFromAssemblyLocation);
            }
        }
    }
}
