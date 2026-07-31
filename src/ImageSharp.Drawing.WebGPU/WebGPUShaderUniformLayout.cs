// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Describes the names, types, and fixed array lengths of values supplied to a WebGPU layer-effect shader.
/// </summary>
public sealed class WebGPUShaderUniformLayout
{
    /// <summary>
    /// The maximum packed byte length accepted for a shader uniform layout.
    /// </summary>
    public const int MaximumByteLength = 64 * 1024;

    // WGSL reserves these identifiers for the language and future language evolution. The
    // framework prefix is checked separately so generated names cannot collide with user names.
    private static readonly HashSet<string> DisallowedNames = new(StringComparer.Ordinal)
    {
        "NULL", "Self", "abstract", "active", "alias", "alignas", "alignof", "as", "asm", "asm_fragment", "async", "attribute", "auto", "await",
        "become", "break", "case", "cast", "catch", "class", "co_await", "co_return", "co_yield", "coherent", "column_major", "common", "compile",
        "compile_fragment", "concept", "const", "const_assert", "const_cast", "consteval", "constexpr", "constinit", "continue", "continuing", "crate",
        "debugger", "decltype", "default", "delete", "demote", "demote_to_helper", "diagnostic", "discard", "do", "dynamic_cast", "else", "enable",
        "enum", "explicit", "export", "extends", "extern", "external", "fallthrough", "false", "filter", "final", "finally", "fn", "for", "friend",
        "from", "fxgroup", "get", "goto", "groupshared", "highp", "if", "impl", "implements", "import", "inline", "instanceof", "interface", "layout",
        "let", "loop", "lowp", "macro", "macro_rules", "match", "mediump", "meta", "mod", "module", "move", "mut", "mutable", "namespace", "new",
        "nil", "noexcept", "noinline", "nointerpolation", "non_coherent", "noncoherent", "noperspective", "null", "nullptr", "of", "operator", "override",
        "package", "packoffset", "partition", "pass", "patch", "pixelfragment", "precise", "precision", "premerge", "priv", "protected", "pub", "public",
        "readonly", "ref", "regardless", "register", "reinterpret_cast", "require", "requires", "resource", "restrict", "return", "self", "set", "shared",
        "sizeof", "smooth", "snorm", "static", "static_assert", "static_cast", "std", "struct", "subroutine", "super", "switch", "target", "template", "this",
        "thread_local", "throw", "trait", "true", "try", "type", "typedef", "typeid", "typename", "typeof", "union", "unless", "unorm", "unsafe",
        "unsized", "use", "using", "var", "varying", "virtual", "volatile", "wgsl", "where", "while", "with", "writeonly", "yield"
    };

    private readonly WebGPUShaderUniform[] uniforms;
    private readonly WebGPUShaderUniformMember[] members;
    private readonly Dictionary<string, int> memberIndices;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebGPUShaderUniformLayout"/> class.
    /// </summary>
    /// <param name="uniforms">The ordered uniform declarations.</param>
    public WebGPUShaderUniformLayout(ReadOnlySpan<WebGPUShaderUniform> uniforms)
    {
        this.uniforms = uniforms.ToArray();
        this.members = new WebGPUShaderUniformMember[uniforms.Length];
        this.memberIndices = new Dictionary<string, int>(uniforms.Length, StringComparer.Ordinal);

        int offset = 0;
        int structureAlignment = 4;

        // Calculate the host-shareable uniform layout once. The builder and generated WGSL both
        // consume these members, preventing the CPU byte offsets from drifting from shader types.
        for (int i = 0; i < uniforms.Length; i++)
        {
            WebGPUShaderUniform uniform = uniforms[i];
            ValidateName(uniform.Name, nameof(uniforms), "uniform");
            if (uniform.ElementCount <= 0)
            {
                throw new ArgumentException("Uniform element counts must be greater than zero.", nameof(uniforms));
            }

            if (!this.memberIndices.TryAdd(uniform.Name, i))
            {
                throw new ArgumentException($"The uniform name '{uniform.Name}' is declared more than once.", nameof(uniforms));
            }

            GetTypeLayout(uniform.Type, out int alignment, out int size, out string wgslType);
            int memberAlignment = alignment;
            int stride = AlignUp(size, alignment);
            if (uniform.ElementCount > 1)
            {
                // Uniform-address-space arrays have a minimum 16-byte alignment and stride on
                // every WebGPU implementation. Using that portable layout avoids an optional
                // WGSL feature changing whether the same public program can run on a device.
                memberAlignment = Math.Max(16, alignment);
                stride = AlignUp(size, memberAlignment);
            }

            offset = AlignUp(offset, memberAlignment);
            this.members[i] = new WebGPUShaderUniformMember(uniform, offset, stride, wgslType);
            int memberSize = uniform.ElementCount == 1 ? size : checked(stride * uniform.ElementCount);
            offset = checked(offset + memberSize);
            structureAlignment = Math.Max(structureAlignment, memberAlignment);
        }

        // Uniform structs also have a minimum 16-byte alignment. Capping the final binding at
        // WebGPU's guaranteed limit makes a valid layout portable instead of device-dependent.
        this.ByteLength = Math.Max(16, AlignUp(offset, Math.Max(16, structureAlignment)));

        // WebGPU guarantees at least 64 KiB for a uniform buffer binding. Enforcing the public
        // limit here keeps every successfully constructed program portable across conforming devices.
        if (this.ByteLength > MaximumByteLength)
        {
            throw new ArgumentException($"The uniform layout requires {this.ByteLength} bytes; WebGPU layer-effect uniforms are limited to {MaximumByteLength} bytes.", nameof(uniforms));
        }

        this.WgslStructureDeclaration = CreateWgslStructureDeclaration(this.members);
    }

    /// <summary>
    /// Gets the ordered uniform declarations.
    /// </summary>
    public ReadOnlySpan<WebGPUShaderUniform> Uniforms => this.uniforms;

    /// <summary>
    /// Gets the calculated members in declaration order.
    /// </summary>
    internal ReadOnlySpan<WebGPUShaderUniformMember> Members => this.members;

    /// <summary>
    /// Gets the packed byte length required for one set of values.
    /// </summary>
    internal int ByteLength { get; }

    /// <summary>
    /// Gets the WGSL structure declaration generated from this layout.
    /// </summary>
    internal string WgslStructureDeclaration { get; }

    /// <summary>
    /// Creates a mutable builder initialized with zero values for every declared uniform.
    /// </summary>
    /// <returns>A builder accepting the names and types declared by this layout.</returns>
    internal WebGPUShaderUniformBuilder CreateUniforms() => new(this);

    /// <summary>
    /// Gets the packed member matching the supplied name and type.
    /// </summary>
    /// <param name="name">The declared member name.</param>
    /// <param name="type">The required member type.</param>
    /// <param name="isArray">Whether the caller requires an array member.</param>
    /// <returns>The matching packed member.</returns>
    internal WebGPUShaderUniformMember GetMember(string name, WebGPUShaderUniformType type, bool isArray)
    {
        Guard.NotNull(name, nameof(name));
        if (!this.memberIndices.TryGetValue(name, out int index))
        {
            throw new ArgumentException($"The uniform layout does not contain a member named '{name}'.", nameof(name));
        }

        WebGPUShaderUniformMember member = this.members[index];
        if (member.Uniform.Type != type || (member.Uniform.ElementCount > 1) != isArray)
        {
            string expected = isArray ? $"an array of {type}" : type.ToString();
            throw new ArgumentException($"The uniform '{name}' is not declared as {expected}.", nameof(name));
        }

        return member;
    }

    /// <summary>
    /// Validates that a public name is a WGSL identifier which cannot collide with framework or reserved names.
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <param name="parameterName">The public parameter reported by validation failures.</param>
    /// <param name="declarationKind">The declaration kind described by validation failures.</param>
    internal static void ValidateName(string name, string parameterName, string declarationKind)
    {
        Guard.NotNull(name, parameterName);
        if (!IsValidPublicIdentifier(name))
        {
            throw new ArgumentException($"'{name}' cannot be used as a WGSL {declarationKind} name.", parameterName);
        }
    }

    /// <summary>
    /// Gets the occupied byte length of one uniform member, including fixed-array stride.
    /// </summary>
    /// <param name="member">The calculated member.</param>
    /// <returns>The member byte length.</returns>
    internal static int GetMemberByteLength(WebGPUShaderUniformMember member)
    {
        if (member.Uniform.ElementCount > 1)
        {
            return checked(member.Stride * member.Uniform.ElementCount);
        }

        return member.Uniform.Type switch
        {
            WebGPUShaderUniformType.Float32 => sizeof(float),
            WebGPUShaderUniformType.Int32 => sizeof(int),
            WebGPUShaderUniformType.UInt32 => sizeof(uint),
            WebGPUShaderUniformType.Vector2 => 8,
            WebGPUShaderUniformType.Vector3 => 12,
            WebGPUShaderUniformType.Vector4 => 16,
            WebGPUShaderUniformType.Matrix4x4 => 64,
            _ => throw new ArgumentOutOfRangeException(nameof(member))
        };
    }

    /// <summary>
    /// Tests whether a name is available to a public shader declaration.
    /// </summary>
    /// <param name="name">The name to test.</param>
    /// <returns><see langword="true"/> when the name is a non-reserved WGSL identifier.</returns>
    internal static bool IsValidPublicIdentifier(string name)
    {
        if (name.Length == 0 || !IsIdentifierStart(name[0]))
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            if (!IsIdentifierPart(name[i]))
            {
                return false;
            }
        }

        return !name.StartsWith("imagesharp_", StringComparison.Ordinal) && !DisallowedNames.Contains(name);
    }

    /// <summary>
    /// Tests whether a character may begin a WGSL identifier.
    /// </summary>
    /// <param name="value">The character to test.</param>
    /// <returns><see langword="true"/> when the character is valid.</returns>
    private static bool IsIdentifierStart(char value)
        => value == '_' || (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z');

    /// <summary>
    /// Tests whether a character may follow the first character of a WGSL identifier.
    /// </summary>
    /// <param name="value">The character to test.</param>
    /// <returns><see langword="true"/> when the character is valid.</returns>
    private static bool IsIdentifierPart(char value)
        => IsIdentifierStart(value) || (value >= '0' && value <= '9');

    /// <summary>
    /// Rounds a byte offset up to the requested power-of-two alignment.
    /// </summary>
    /// <param name="value">The byte offset to align.</param>
    /// <param name="alignment">The required power-of-two alignment.</param>
    /// <returns>The aligned byte offset.</returns>
    private static int AlignUp(int value, int alignment)
        => checked((value + alignment - 1) & -alignment);

    /// <summary>
    /// Maps one public uniform type to its WGSL host-shareable alignment, size, and spelling.
    /// </summary>
    /// <param name="type">The public uniform type.</param>
    /// <param name="alignment">Receives its required byte alignment.</param>
    /// <param name="size">Receives its unpadded byte size.</param>
    /// <param name="wgslType">Receives its WGSL type spelling.</param>
    private static void GetTypeLayout(WebGPUShaderUniformType type, out int alignment, out int size, out string wgslType)
    {
        switch (type)
        {
            case WebGPUShaderUniformType.Float32:
                alignment = 4;
                size = 4;
                wgslType = "f32";
                break;
            case WebGPUShaderUniformType.Int32:
                alignment = 4;
                size = 4;
                wgslType = "i32";
                break;
            case WebGPUShaderUniformType.UInt32:
                alignment = 4;
                size = 4;
                wgslType = "u32";
                break;
            case WebGPUShaderUniformType.Vector2:
                alignment = 8;
                size = 8;
                wgslType = "vec2<f32>";
                break;
            case WebGPUShaderUniformType.Vector3:
                alignment = 16;
                size = 12;
                wgslType = "vec3<f32>";
                break;
            case WebGPUShaderUniformType.Vector4:
                alignment = 16;
                size = 16;
                wgslType = "vec4<f32>";
                break;
            case WebGPUShaderUniformType.Matrix4x4:
                alignment = 16;
                size = 64;
                wgslType = "mat4x4<f32>";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    /// <summary>
    /// Generates the WGSL structure that exactly mirrors the calculated host byte layout.
    /// </summary>
    /// <param name="members">The calculated members in declaration order.</param>
    /// <returns>The complete WGSL structure declaration.</returns>
    private static string CreateWgslStructureDeclaration(ReadOnlySpan<WebGPUShaderUniformMember> members)
    {
        StringBuilder builder = new();
        builder.AppendLine("struct ImageSharpUniforms {");
        if (members.IsEmpty)
        {
            // WGSL structures cannot be empty. This private member keeps a stable binding layout
            // for programs that do not expose user values.
            builder.AppendLine("    imagesharp_padding: vec4<f32>,");
        }
        else
        {
            for (int i = 0; i < members.Length; i++)
            {
                WebGPUShaderUniformMember member = members[i];
                builder.Append("    ").Append(member.Uniform.Name).Append(": ");
                if (member.Uniform.ElementCount == 1)
                {
                    builder.Append(member.WgslType);
                }
                else
                {
                    builder.Append("array<").Append(member.WgslType).Append(", ").Append(member.Uniform.ElementCount).Append('>');
                }

                builder.AppendLine(",");
            }
        }

        builder.AppendLine("};");
        return builder.ToString();
    }
}
