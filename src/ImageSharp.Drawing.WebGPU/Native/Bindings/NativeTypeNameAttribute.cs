// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends.Native;

/// <summary>
/// Records the original C type name associated with a generated managed declaration.
/// </summary>
[AttributeUsage(
    AttributeTargets.Struct |
    AttributeTargets.Enum |
    AttributeTargets.Property |
    AttributeTargets.Field |
    AttributeTargets.Parameter |
    AttributeTargets.ReturnValue,
    AllowMultiple = false,
    Inherited = true)]
[Conditional("DEBUG")]
internal sealed class NativeTypeNameAttribute : Attribute
{
    private readonly string name;

    /// <summary>
    /// Initializes a new instance of the <see cref="NativeTypeNameAttribute"/> class.
    /// </summary>
    /// <param name="name">The native C type name.</param>
    public NativeTypeNameAttribute(string name) => this.name = name;

    /// <summary>
    /// Gets the native C type name.
    /// </summary>
    public string Name => this.name;
}
