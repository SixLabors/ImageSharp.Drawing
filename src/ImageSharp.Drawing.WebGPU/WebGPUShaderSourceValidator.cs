// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Performs allocation-free lexical validation of user WGSL against names and declarations owned by the layer-effect framework.
/// </summary>
/// <remarks>
/// This type deliberately does not parse WGSL grammar or types; the native WebGPU compiler remains
/// authoritative for those rules. The lexical pass rejects constructs that could shadow generated
/// resources, helpers, or entry points before untrusted source is combined with the framework module.
/// It also identifies the initial module-directive prefix because those directives must precede the
/// framework declarations in the final WGSL module. That scan reports source boundaries only; it does
/// not validate directive arguments or reorder directives found after the first non-directive token.
/// Successful validation allocates no managed memory. Validation failures allocate only their exception.
/// </remarks>
internal static class WebGPUShaderSourceValidator
{
    /// <summary>
    /// Finds the end of the leading WGSL module-directive prefix.
    /// </summary>
    /// <param name="source">The validated WGSL module fragment.</param>
    /// <returns>The exclusive end offset of the final leading directive, or zero when no directive is present.</returns>
    /// <remarks>
    /// User source is accepted as a WGSL module fragment rather than as a parsed syntax tree. The
    /// framework must therefore locate any initial <c>enable</c>, <c>requires</c>, and
    /// <c>diagnostic</c> directives before it inserts its own global declarations. This method
    /// recognizes only a contiguous leading sequence separated by WGSL whitespace or comments.
    /// A malformed directive, or a directive appearing after another global declaration, remains in
    /// the user body for the native WGSL compiler to diagnose.
    ///
    /// The returned offset lets module generation append slices of the original string. No substring
    /// or rewritten source copy is allocated, and the original line endings remain available for
    /// diagnostic source mapping.
    /// </remarks>
    public static int GetModuleDirectiveEnd(string source)
    {
        // `position` identifies the next character to inspect. It may advance beyond trivia while
        // probing for another directive, but that trivia is not committed unless a complete
        // following directive is found.
        int position = 0;

        // Track the last fully terminated directive separately from the current scan position. If
        // the next apparent directive is malformed, the native compiler must see it in the user
        // body rather than having an incomplete declaration moved ahead of the framework prelude.
        int directiveEnd = 0;

        while (position < source.Length)
        {
            // WGSL permits whitespace, line comments, and nested block comments between module
            // directives. Scan this trivia tentatively: when another directive follows, the
            // returned prefix retains the trivia between them; otherwise directiveEnd remains
            // unchanged and trailing trivia stays at its authored source location.
            while (position < source.Length)
            {
                if (char.IsWhiteSpace(source[position]))
                {
                    position++;
                    continue;
                }

                if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '/')
                {
                    // Stop before the newline and let the whitespace branch consume it so CR, LF,
                    // and CRLF input all follow the same path.
                    position += 2;
                    while (position < source.Length && source[position] is not ('\r' or '\n'))
                    {
                        position++;
                    }

                    continue;
                }

                if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '*')
                {
                    // WGSL block comments may nest. Stopping at the first closing delimiter would
                    // expose outer-comment text and could misclassify it as a module directive.
                    int blockCommentDepth = 1;
                    position += 2;

                    while (position < source.Length && blockCommentDepth > 0)
                    {
                        if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '*')
                        {
                            blockCommentDepth++;
                            position += 2;
                        }
                        else if (position + 1 < source.Length && source[position] == '*' && source[position + 1] == '/')
                        {
                            blockCommentDepth--;
                            position += 2;
                        }
                        else
                        {
                            position++;
                        }
                    }

                    continue;
                }

                break;
            }

            // Inspect the original source through a span so probing for each directive keyword
            // creates no substring.
            ReadOnlySpan<char> remaining = source.AsSpan(position);
            int keywordLength;

            // Recognize only module directives that WGSL requires ahead of global declarations.
            // This deliberately stops at the first other token: relocating a later directive would
            // also reorder the user's declarations and conceal the native compiler error. The
            // identifier-boundary check prevents names such as `enabled_feature` from being treated
            // as the `enable` directive.
            if (StartsWithDirectiveKeyword(remaining, "enable"))
            {
                keywordLength = "enable".Length;
            }
            else if (StartsWithDirectiveKeyword(remaining, "requires"))
            {
                keywordLength = "requires".Length;
            }
            else if (StartsWithDirectiveKeyword(remaining, "diagnostic"))
            {
                keywordLength = "diagnostic".Length;
            }
            else
            {
                break;
            }

            position += keywordLength;

            // This counter is initialized only when a block comment begins and is consumed before
            // scanning resumes, so comment nesting never leaks into the next directive.
            int blockDepth;

            // WGSL module directives end at the first semicolon outside a comment. WGSL has no
            // string literal token, so comments are the only lexical construct that can hide it.
            while (position < source.Length)
            {
                if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '/')
                {
                    // Semicolons inside a line comment cannot terminate the directive.
                    position += 2;
                    while (position < source.Length && source[position] is not ('\r' or '\n'))
                    {
                        position++;
                    }

                    continue;
                }

                if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '*')
                {
                    // Skip the complete nested comment before resuming the terminator search.
                    blockDepth = 1;
                    position += 2;

                    while (position < source.Length && blockDepth > 0)
                    {
                        if (position + 1 < source.Length && source[position] == '/' && source[position + 1] == '*')
                        {
                            blockDepth++;
                            position += 2;
                        }
                        else if (position + 1 < source.Length && source[position] == '*' && source[position + 1] == '/')
                        {
                            blockDepth--;
                            position += 2;
                        }
                        else
                        {
                            position++;
                        }
                    }

                    continue;
                }

                if (source[position++] == ';')
                {
                    // Commit only after finding a real terminator. The exclusive offset includes
                    // the semicolon and any leading or inter-directive trivia before it.
                    directiveEnd = position;
                    break;
                }
            }

            if (directiveEnd != position)
            {
                // The native WGSL compiler reports the malformed unterminated directive. Treating
                // it as source body here avoids moving unrelated declarations ahead of the prelude.
                // Equality holds only when this scan committed a semicolon at the current position.
                break;
            }
        }

        // Returning an offset lets module generation append slices of the original source and
        // preserve diagnostic coordinates without allocating rewritten strings in this validator.
        return directiveEnd;
    }

    /// <summary>
    /// Validates source constructs that would collide with the generated layer-effect module.
    /// </summary>
    /// <param name="source">The user WGSL module fragment.</param>
    /// <param name="parameterName">The public parameter name reported by validation failures.</param>
    public static void Validate(string source, string parameterName)
    {
        // The limit is defined in encoded bytes because that is what the native API consumes.
        // Encoding.GetByteCount scans the existing string without creating an encoded copy.
        if (Encoding.UTF8.GetByteCount(source) > WebGPUShaderProgram.MaximumSourceByteLength)
        {
            throw new ArgumentException(
                $"WGSL source cannot exceed {WebGPUShaderProgram.MaximumSourceByteLength} UTF-8 bytes.",
                parameterName);
        }

        // This is a single-pass lexical boundary check rather than a WGSL parser. The flags retain
        // only the context required to identify declarations and framework-owned references:
        //
        // - attribute: the preceding significant character was '@', so the next identifier names
        //   a pipeline attribute owned either by the user or by the framework.
        // - declarationNameExpected/functionNameExpected: the preceding token was a declaration
        //   keyword whose next identifier introduces a symbol.
        // - variableNameExpected/variableTemplateDepth: `var` may place an address-space template
        //   between its keyword and declared identifier, for example `var<uniform> value`.
        // - uniformMemberAccessExpected: the public uniform object may only be followed by `.` so
        //   callers cannot pass, index, take the address of, or redeclare the complete binding.
        // - reservedNameAwaitingColon: function parameters and structure fields have no declaration
        //   keyword, so a following colon identifies a declaration that must also be rejected.
        //
        // Comments are skipped before token interpretation. WGSL permits nested block comments, so
        // blockCommentDepth prevents comment text from being mistaken for executable declarations.
        bool attribute = false;
        bool declarationNameExpected = false;
        bool functionNameExpected = false;
        bool uniformMemberAccessExpected = false;
        bool variableNameExpected = false;
        bool reservedNameAwaitingColon = false;
        int blockCommentDepth = 0;
        int layerEffectDeclarationCount = 0;
        int variableTemplateDepth = 0;

        for (int i = 0; i < source.Length;)
        {
            char value = source[i];
            if (value == '\0')
            {
                // Native string views use a null terminator. An embedded null could make the native
                // compiler validate a different prefix from the source inspected here.
                throw new ArgumentException("WGSL source cannot contain a null character.", parameterName);
            }

            if (blockCommentDepth > 0)
            {
                if (i + 1 < source.Length && value == '/' && source[i + 1] == '*')
                {
                    blockCommentDepth++;
                    i += 2;
                }
                else if (i + 1 < source.Length && value == '*' && source[i + 1] == '/')
                {
                    blockCommentDepth--;
                    i += 2;
                }
                else
                {
                    i++;
                }

                continue;
            }

            if (i + 1 < source.Length && value == '/' && source[i + 1] == '*')
            {
                blockCommentDepth = 1;
                i += 2;
                continue;
            }

            if (i + 1 < source.Length && value == '/' && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] is not ('\r' or '\n'))
                {
                    i++;
                }

                continue;
            }

            if (value == '@')
            {
                if (uniformMemberAccessExpected)
                {
                    throw new ArgumentException("imagesharp_uniforms must be accessed through a directly named field.", parameterName);
                }

                // Remember the attribute marker across whitespace; the next identifier determines
                // whether the user is attempting to claim a framework-owned pipeline construct.
                reservedNameAwaitingColon = false;
                attribute = true;
                i++;
                continue;
            }

            if (IsIdentifierStart(value))
            {
                if (uniformMemberAccessExpected)
                {
                    throw new ArgumentException("imagesharp_uniforms must be accessed through a directly named field.", parameterName);
                }

                int start = i++;
                while (i < source.Length && IsIdentifierPart(source[i]))
                {
                    i++;
                }

                ReadOnlySpan<char> name = source.AsSpan(start, i - start);
                reservedNameAwaitingColon = false;

                if (attribute)
                {
                    if (name.StartsWith("imagesharp_", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("WGSL identifiers beginning with 'imagesharp_' are reserved by ImageSharp.", parameterName);
                    }

                    if (name.SequenceEqual("group") || name.SequenceEqual("binding"))
                    {
                        throw new ArgumentException("Layer-effect WGSL cannot declare bind groups or bindings.", parameterName);
                    }

                    if (name.SequenceEqual("vertex") || name.SequenceEqual("fragment") || name.SequenceEqual("compute"))
                    {
                        throw new ArgumentException("Layer-effect WGSL cannot declare shader entry points.", parameterName);
                    }

                    attribute = false;
                    continue;
                }

                bool isDeclarationName = false;
                bool isLayerEffectFunction = false;
                if (functionNameExpected)
                {
                    functionNameExpected = false;
                    isDeclarationName = true;

                    if (name.SequenceEqual("layer_effect"))
                    {
                        isLayerEffectFunction = true;
                        layerEffectDeclarationCount++;
                        if (layerEffectDeclarationCount > 1)
                        {
                            throw new ArgumentException("Layer-effect WGSL must declare exactly one layer_effect function.", parameterName);
                        }
                    }
                }
                else if (declarationNameExpected)
                {
                    declarationNameExpected = false;
                    isDeclarationName = true;
                }
                else if (variableNameExpected && variableTemplateDepth == 0)
                {
                    variableNameExpected = false;
                    isDeclarationName = true;
                }
                else if (!variableNameExpected)
                {
                    // These WGSL declarations place their name immediately after the keyword.
                    // Variable declarations are handled separately because var may include an
                    // address-space template before its name.
                    if (name.SequenceEqual("fn"))
                    {
                        functionNameExpected = true;
                    }
                    else if (name.SequenceEqual("alias") ||
                             name.SequenceEqual("const") ||
                             name.SequenceEqual("let") ||
                             name.SequenceEqual("override") ||
                             name.SequenceEqual("struct"))
                    {
                        declarationNameExpected = true;
                    }
                    else if (name.SequenceEqual("var"))
                    {
                        variableNameExpected = true;
                    }
                }

                if (IsFrameworkPrivateName(name))
                {
                    throw new ArgumentException($"The WGSL identifier '{name}' is owned by ImageSharp.", parameterName);
                }

                if (isDeclarationName && IsFrameworkOwnedName(name) && !isLayerEffectFunction)
                {
                    throw new ArgumentException($"The WGSL declaration name '{name}' is owned by ImageSharp.", parameterName);
                }

                bool hasReservedPrefix = name.StartsWith("imagesharp_", StringComparison.Ordinal);
                bool isPublicUniformBinding = name.SequenceEqual("imagesharp_uniforms");
                if (hasReservedPrefix && (!isPublicUniformBinding || isDeclarationName))
                {
                    throw new ArgumentException("WGSL identifiers beginning with 'imagesharp_' are reserved by ImageSharp; only imagesharp_uniforms may be read by user source.", parameterName);
                }

                // Function parameters and structure members have no declaration keyword. Retain
                // the public uniform binding name across whitespace and comments so a following
                // colon cannot redeclare it while ordinary member reads remain valid.
                reservedNameAwaitingColon = !isDeclarationName && isPublicUniformBinding;
                uniformMemberAccessExpected = !isDeclarationName && isPublicUniformBinding;
                continue;
            }

            if (!char.IsWhiteSpace(value))
            {
                if (uniformMemberAccessExpected)
                {
                    if (value != '.')
                    {
                        throw new ArgumentException("imagesharp_uniforms must be accessed through a directly named field.", parameterName);
                    }

                    uniformMemberAccessExpected = false;
                }

                attribute = false;

                if (variableNameExpected)
                {
                    if (value == '<')
                    {
                        variableTemplateDepth++;
                    }
                    else if (value == '>' && variableTemplateDepth > 0)
                    {
                        variableTemplateDepth--;
                    }
                }

                if (value == ':' && reservedNameAwaitingColon)
                {
                    throw new ArgumentException("WGSL declarations beginning with 'imagesharp_' are reserved by ImageSharp.", parameterName);
                }

                reservedNameAwaitingColon = false;
            }

            i++;
        }

        if (uniformMemberAccessExpected)
        {
            throw new ArgumentException("imagesharp_uniforms must be accessed through a directly named field.", parameterName);
        }

        if (layerEffectDeclarationCount == 0)
        {
            throw new ArgumentException("Layer-effect WGSL must declare exactly one layer_effect function.", parameterName);
        }
    }

    /// <summary>
    /// Tests whether a character may begin a WGSL identifier.
    /// </summary>
    /// <param name="value">The character to test.</param>
    /// <returns><see langword="true"/> when the character is valid.</returns>
    private static bool IsIdentifierStart(char value)
        => value is '_' or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z');

    /// <summary>
    /// Tests whether a character may follow the first character of a WGSL identifier.
    /// </summary>
    /// <param name="value">The character to test.</param>
    /// <returns><see langword="true"/> when the character is valid.</returns>
    private static bool IsIdentifierPart(char value)
        => IsIdentifierStart(value) || (value >= '0' && value <= '9');

    /// <summary>
    /// Tests whether source begins with one complete WGSL directive keyword.
    /// </summary>
    /// <param name="source">The source beginning at the next non-trivia token.</param>
    /// <param name="keyword">The directive keyword to test.</param>
    /// <returns><see langword="true"/> when the keyword is not an identifier prefix.</returns>
    private static bool StartsWithDirectiveKeyword(ReadOnlySpan<char> source, ReadOnlySpan<char> keyword)
        => source.StartsWith(keyword, StringComparison.Ordinal)
            && (source.Length == keyword.Length || !IsIdentifierPart(source[keyword.Length]));

    /// <summary>
    /// Tests whether a declaration name is supplied by the generated layer-effect module.
    /// </summary>
    /// <param name="name">The WGSL identifier to test.</param>
    /// <returns><see langword="true"/> when ImageSharp owns the declaration.</returns>
    private static bool IsFrameworkOwnedName(ReadOnlySpan<char> name)
        => name.SequenceEqual("layer_effect")
            || name.SequenceEqual("layer_load")
            || name.SequenceEqual("layer_load_unassociated")
            || name.SequenceEqual("layer_sample")
            || name.SequenceEqual("vs_main")
            || name.SequenceEqual("fs_main")
            || name.SequenceEqual("ImageSharpFramework")
            || name.SequenceEqual("ImageSharpUniforms");

    /// <summary>
    /// Tests whether an identifier belongs to the generated module but is not a public shader helper.
    /// </summary>
    /// <param name="name">The WGSL identifier to test.</param>
    /// <returns><see langword="true"/> when user source cannot reference the identifier.</returns>
    private static bool IsFrameworkPrivateName(ReadOnlySpan<char> name)
        => name.SequenceEqual("vs_main")
            || name.SequenceEqual("fs_main")
            || name.SequenceEqual("ImageSharpFramework")
            || name.SequenceEqual("ImageSharpUniforms");
}
