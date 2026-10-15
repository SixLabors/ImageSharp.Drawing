// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// One component of the data of a GS1 Application Identifier: its character set, its length, whether it
/// may be left out, and the checks its content takes.
/// </summary>
internal readonly struct Gs1Component
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Gs1Component"/> struct.
    /// </summary>
    /// <param name="characterSet">The character set: 'N' digits, 'X' character set 82, 'Y' character set 39 or 'Z' character set 64.</param>
    /// <param name="minimum">The shortest length.</param>
    /// <param name="maximum">The longest length.</param>
    /// <param name="optional">Whether the component may be left out.</param>
    /// <param name="linters">The checks the content takes, in order.</param>
    public Gs1Component(char characterSet, int minimum, int maximum, bool optional, Gs1Linter[] linters)
    {
        this.CharacterSet = characterSet;
        this.Minimum = minimum;
        this.Maximum = maximum;
        this.Optional = optional;
        this.Linters = linters;
    }

    /// <summary>
    /// Gets the character set: 'N' digits, 'X' character set 82, 'Y' character set 39 or 'Z' character set 64.
    /// </summary>
    public char CharacterSet { get; }

    /// <summary>
    /// Gets the shortest length.
    /// </summary>
    public int Minimum { get; }

    /// <summary>
    /// Gets the longest length.
    /// </summary>
    public int Maximum { get; }

    /// <summary>
    /// Gets a value indicating whether the component may be left out.
    /// </summary>
    public bool Optional { get; }

    /// <summary>
    /// Gets the checks the content takes, in order.
    /// </summary>
    public Gs1Linter[] Linters { get; }
}
