// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Text;
using SixLabors.ImageSharp.Drawing.Processing.Backends.Native;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Convolution;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Contains one complete framework-wrapped WGSL module and its user-source location mapping.
/// </summary>
/// <remarks>
/// A <see cref="WebGPUShaderProgram"/> supplies a WGSL module fragment, not a parsed syntax tree.
/// This type combines that fragment with the bindings, load helpers, and pipeline entry points owned
/// by ImageSharp. The resulting module always has this order:
/// <list type="number">
/// <item><description>leading user-authored WGSL module directives;</description></item>
/// <item><description>framework structures, bindings, and texture-access helpers;</description></item>
/// <item><description>the remaining user-authored declarations, including <c>layer_effect</c>;</description></item>
/// <item><description>framework vertex and fragment entry points.</description></item>
/// </list>
/// Only the leading directive prefix changes position. All other user source is appended verbatim,
/// and the vacated directive characters are represented by whitespace so compiler diagnostics can be
/// mapped back to the authored line and column positions.
/// </remarks>
internal sealed class WebGPUShaderModuleSource
{
    // Incrementing this value invalidates pipeline keys whenever generated framework semantics
    // change without requiring callers to alter their user WGSL.
    private const int FrameworkContractVersion = 3;

    // WGSL section 3.2 defines LF, VT, FF, CR, NEL, LS, and PS as line breaks. Every value is one
    // UTF-16 code unit, so SearchValues can use the runtime's optimized span search without decoding
    // every source code point. CRLF folding remains explicit because it is one WGSL line break.
    private static readonly SearchValues<char> LineBreakCharacters = SearchValues.Create("\n\v\f\r\u0085\u2028\u2029");
    private readonly byte[] utf8Source;

    private WebGPUShaderModuleSource(string source, byte[] utf8Source, int userLineStart, int userLineCount)
    {
        this.Source = source;
        this.utf8Source = utf8Source;
        this.UserLineStart = userLineStart;
        this.UserLineCount = userLineCount;
        this.PrecomputedHashCode = HashCode.Combine(
            FrameworkContractVersion,
            StringComparer.Ordinal.GetHashCode(source),
            WGPUTextureFormat.RGBA16Float);
    }

    /// <summary>
    /// Gets the complete WGSL module text.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// Gets the null-terminated UTF-8 module text accepted by the native API.
    /// </summary>
    public ReadOnlySpan<byte> Utf8Source => this.utf8Source;

    /// <summary>
    /// Gets the first one-based wrapper line occupied by user source.
    /// </summary>
    public int UserLineStart { get; }

    /// <summary>
    /// Gets the number of lines occupied by user source.
    /// </summary>
    public int UserLineCount { get; }

    /// <summary>
    /// Gets the exact pipeline-key hash computed once when the module is generated.
    /// </summary>
    public int PrecomputedHashCode { get; }

    /// <summary>
    /// Generates the complete module for one source representation.
    /// </summary>
    /// <param name="program">The public user program.</param>
    /// <param name="sourceDescriptor">The source texture representation.</param>
    /// <param name="xBorderMode">The horizontal border mode used by filtered samples.</param>
    /// <param name="yBorderMode">The vertical border mode used by filtered samples.</param>
    /// <returns>The generated module source.</returns>
    /// <remarks>
    /// WGSL module directives must appear before global declarations. Since ImageSharp contributes
    /// global declarations of its own, the leading directive prefix is emitted before the framework
    /// prelude. The user fragment is otherwise preserved and the native WebGPU compiler remains
    /// responsible for WGSL grammar, types, and directive semantics.
    /// </remarks>
    public static WebGPUShaderModuleSource Create(
        WebGPUShaderProgram program,
        WebGPUTargetDescriptor sourceDescriptor,
        BorderWrappingMode? xBorderMode,
        BorderWrappingMode? yBorderMode)
    {
        StringBuilder builder = new();
        ReadOnlySpan<char> userSource = program.Source.AsSpan();

        // This is a lexical boundary, not a parsed or validated directive list. Only a contiguous
        // directive prefix can be relocated without changing the relative order of user declarations.
        // Malformed or non-leading directives stay in the body for the native compiler to reject.
        int moduleDirectiveEnd = WebGPUShaderSourceValidator.GetModuleDirectiveEnd(program.Source);

        // Emit the directive prefix exactly once, before ImageSharp's first global declaration.
        // The original characters are masked at their authored location below; retaining both
        // copies would produce duplicate directives and invalid WGSL.
        _ = builder.Append(userSource[..moduleDirectiveEnd]);

        // Framework declarations are generated rather than user supplied so resource bindings,
        // texture representation conversion, and diagnostic source mapping remain authoritative.
        _ = builder.AppendLine("struct ImageSharpFramework {");
        _ = builder.AppendLine("    imagesharp_source_origin: vec2<i32>,");
        _ = builder.AppendLine("    imagesharp_valid_min: vec2<i32>,");
        _ = builder.AppendLine("    imagesharp_valid_max: vec2<i32>,");
        _ = builder.AppendLine("    imagesharp_input_size: vec2<i32>,");
        _ = builder.AppendLine("};");
        _ = builder.Append(program.UniformLayout.WgslStructureDeclaration);
        _ = builder.AppendLine("@group(0) @binding(0) var imagesharp_source: texture_2d<f32>;");
        _ = builder.AppendLine("@group(0) @binding(1) var<uniform> imagesharp_framework: ImageSharpFramework;");
        _ = builder.AppendLine("@group(0) @binding(2) var<uniform> imagesharp_uniforms: ImageSharpUniforms;");
        _ = builder.AppendLine("@group(0) @binding(3) var imagesharp_filtering_sampler: sampler;");
        _ = builder.AppendLine();
        _ = builder.AppendLine("fn imagesharp_layer_load_scaled(position: vec2<i32>) -> vec4<f32> {");
        _ = builder.AppendLine("    if (any(position < imagesharp_framework.imagesharp_valid_min) || any(position >= imagesharp_framework.imagesharp_valid_max)) {");
        _ = builder.AppendLine("        return vec4<f32>(0.0);");
        _ = builder.AppendLine("    }");
        _ = builder.AppendLine();
        _ = builder.AppendLine("    let imagesharp_native = textureLoad(imagesharp_source, imagesharp_framework.imagesharp_source_origin + position, 0);");

        if (sourceDescriptor.NumericEncoding == WebGPUTargetNumericEncoding.SignedUnit)
        {
            // Signed-unit targets store logical [0, 1] values in physical [-1, 1]. Convert before
            // alpha association so every public load helper exposes the same logical component range.
            _ = builder.AppendLine("    let imagesharp_scaled = (imagesharp_native + vec4<f32>(1.0)) * 0.5;");
        }
        else
        {
            _ = builder.AppendLine("    let imagesharp_scaled = imagesharp_native;");
        }

        _ = builder.AppendLine("    return imagesharp_scaled;");
        _ = builder.AppendLine("}");
        _ = builder.AppendLine();
        _ = builder.AppendLine("fn layer_load(position: vec2<i32>) -> vec4<f32> {");
        _ = builder.AppendLine("    let imagesharp_scaled = imagesharp_layer_load_scaled(position);");

        if (sourceDescriptor.AlphaRepresentation == PixelAlphaRepresentation.Associated)
        {
            _ = builder.AppendLine("    return imagesharp_scaled;");
        }
        else
        {
            // The renderer's common effect space is associated alpha. Multiplying at this boundary
            // prevents transparent RGB from bleeding through filtering and multi-pass effects.
            _ = builder.AppendLine("    return vec4<f32>(imagesharp_scaled.rgb * imagesharp_scaled.a, imagesharp_scaled.a);");
        }

        _ = builder.AppendLine("}");
        _ = builder.AppendLine();
        _ = builder.AppendLine("fn layer_load_unassociated(position: vec2<i32>) -> vec4<f32> {");
        _ = builder.AppendLine("    let imagesharp_scaled = imagesharp_layer_load_scaled(position);");

        if (sourceDescriptor.AlphaRepresentation == PixelAlphaRepresentation.Associated)
        {
            // Division is defined only for covered pixels. Transparent associated pixels have no
            // recoverable straight RGB, so the logical transparent value is returned instead.
            _ = builder.AppendLine("    if (imagesharp_scaled.a > 0.0) {");
            _ = builder.AppendLine("        return vec4<f32>(imagesharp_scaled.rgb / imagesharp_scaled.a, imagesharp_scaled.a);");
            _ = builder.AppendLine("    }");
            _ = builder.AppendLine();
            _ = builder.AppendLine("    return vec4<f32>(0.0);");
        }
        else
        {
            _ = builder.AppendLine("    return imagesharp_scaled;");
        }

        _ = builder.AppendLine("}");
        _ = builder.AppendLine();

        bool wrapsSamples = xBorderMode.HasValue || yBorderMode.HasValue;
        if (wrapsSamples)
        {
            // Border mapping is generated into the module rather than selected by a shader uniform.
            // Each pass therefore pays only for its declared rule inside the pixel-sampling loop.
            AppendBorderCoordinateFunction(builder, 'x', xBorderMode);
            AppendBorderCoordinateFunction(builder, 'y', yBorderMode);
            _ = builder.AppendLine("fn imagesharp_wrap_position(position: vec2<i32>) -> vec2<i32> {");
            _ = builder.AppendLine("    return vec2<i32>(imagesharp_wrap_x(position.x), imagesharp_wrap_y(position.y));");
            _ = builder.AppendLine("}");
            _ = builder.AppendLine();
        }

        _ = builder.AppendLine("fn imagesharp_layer_sample_bilinear(position: vec2<f32>) -> vec4<f32> {");

        // Fragment positions address pixel centers at half-integer coordinates. Subtracting one
        // half converts them to the integer texel lattice used by the four explicit loads.
        _ = builder.AppendLine("    let imagesharp_texel_position = position - vec2<f32>(0.5);");
        _ = builder.AppendLine("    let imagesharp_minimum = vec2<i32>(floor(imagesharp_texel_position));");
        _ = builder.AppendLine("    let imagesharp_fraction = fract(imagesharp_texel_position);");

        if (wrapsSamples)
        {
            // Map each texel independently before interpolation. Mapping the floating-point sample
            // position instead would produce incorrect weights at reflected and wrapped boundaries.
            _ = builder.AppendLine("    let imagesharp_top_left = layer_load(imagesharp_wrap_position(imagesharp_minimum));");
            _ = builder.AppendLine("    let imagesharp_top_right = layer_load(imagesharp_wrap_position(imagesharp_minimum + vec2<i32>(1, 0)));");
            _ = builder.AppendLine("    let imagesharp_bottom_left = layer_load(imagesharp_wrap_position(imagesharp_minimum + vec2<i32>(0, 1)));");
            _ = builder.AppendLine("    let imagesharp_bottom_right = layer_load(imagesharp_wrap_position(imagesharp_minimum + vec2<i32>(1, 1)));");
        }
        else
        {
            _ = builder.AppendLine("    let imagesharp_top_left = layer_load(imagesharp_minimum);");
            _ = builder.AppendLine("    let imagesharp_top_right = layer_load(imagesharp_minimum + vec2<i32>(1, 0));");
            _ = builder.AppendLine("    let imagesharp_bottom_left = layer_load(imagesharp_minimum + vec2<i32>(0, 1));");
            _ = builder.AppendLine("    let imagesharp_bottom_right = layer_load(imagesharp_minimum + vec2<i32>(1, 1));");
        }

        _ = builder.AppendLine("    let imagesharp_top = mix(imagesharp_top_left, imagesharp_top_right, imagesharp_fraction.x);");
        _ = builder.AppendLine("    let imagesharp_bottom = mix(imagesharp_bottom_left, imagesharp_bottom_right, imagesharp_fraction.x);");
        _ = builder.AppendLine("    return mix(imagesharp_top, imagesharp_bottom, imagesharp_fraction.y);");
        _ = builder.AppendLine("}");
        _ = builder.AppendLine();
        _ = builder.AppendLine("fn layer_sample(position: vec2<f32>) -> vec4<f32> {");

        if (sourceDescriptor.AlphaRepresentation == PixelAlphaRepresentation.Associated)
        {
            // Hardware filtering is safe only when the complete bilinear footprint lies inside
            // valid associated source data. Boundary footprints use explicit transparent loads.
            _ = builder.AppendLine("    let imagesharp_texel_position = position - vec2<f32>(0.5);");
            _ = builder.AppendLine("    let imagesharp_minimum = vec2<i32>(floor(imagesharp_texel_position));");
            _ = builder.AppendLine("    let imagesharp_maximum = imagesharp_minimum + vec2<i32>(1);");
            _ = builder.AppendLine();
            _ = builder.AppendLine("    if (all(imagesharp_minimum >= imagesharp_framework.imagesharp_valid_min) && all(imagesharp_maximum < imagesharp_framework.imagesharp_valid_max)) {");
            _ = builder.AppendLine("        let imagesharp_source_position = vec2<f32>(imagesharp_framework.imagesharp_source_origin) + position;");
            _ = builder.AppendLine("        let imagesharp_coordinate = imagesharp_source_position / vec2<f32>(textureDimensions(imagesharp_source));");
            _ = builder.AppendLine("        let imagesharp_native = textureSampleLevel(imagesharp_source, imagesharp_filtering_sampler, imagesharp_coordinate, 0.0);");

            if (sourceDescriptor.NumericEncoding == WebGPUTargetNumericEncoding.SignedUnit)
            {
                _ = builder.AppendLine("        return (imagesharp_native + vec4<f32>(1.0)) * 0.5;");
            }
            else
            {
                _ = builder.AppendLine("        return imagesharp_native;");
            }

            _ = builder.AppendLine("    }");
            _ = builder.AppendLine();
        }

        // Hardware filtering would interpolate straight RGB before alpha association. Manually
        // blending layer_load results instead preserves associated-alpha interpolation and also
        // retains transparent reads when the bilinear footprint crosses the valid source bounds.
        _ = builder.AppendLine("    return imagesharp_layer_sample_bilinear(position);");
        _ = builder.AppendLine("}");
        _ = builder.AppendLine();

        int userLineStart = CountLines(builder);

        // The leading directives were emitted before the framework prelude and must not appear a
        // second time here. Preserve every original line ending and replace only non-newline
        // characters with spaces. The user body consequently begins on the same relative line, and
        // tokens following a directive retain their authored columns in native compiler diagnostics.
        // Appending directly avoids allocating a temporary character array or rewritten string.
        for (int i = 0; i < moduleDirectiveEnd; i++)
        {
            char value = userSource[i];
            _ = builder.Append(value is '\r' or '\n' ? value : ' ');
        }

        // Record the exact wrapper range occupied by user source before appending framework entry
        // points. Masking preserves the original line count, so the immutable source can be counted
        // directly without constructing an intermediate source-body string.
        _ = builder.Append(userSource[moduleDirectiveEnd..]);
        _ = builder.AppendLine();
        int userLineCount = CountLines(program.Source);

        _ = builder.AppendLine("@vertex");
        _ = builder.AppendLine("fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4<f32> {");
        _ = builder.AppendLine("    let positions = array<vec2<f32>, 3>(vec2<f32>(-1.0, -1.0), vec2<f32>(3.0, -1.0), vec2<f32>(-1.0, 3.0));");
        _ = builder.AppendLine("    return vec4<f32>(positions[vertex_index], 0.0, 1.0);");
        _ = builder.AppendLine("}");
        _ = builder.AppendLine();
        _ = builder.AppendLine("@fragment");
        _ = builder.AppendLine("fn fs_main(@builtin(position) position: vec4<f32>) -> @location(0) vec4<f32> {");
        _ = builder.AppendLine("    return layer_effect(position.xy);");
        _ = builder.AppendLine("}");

        string source = builder.ToString();

        // Native WebGPU consumes a null-terminated UTF-8 string view. Cache this encoding with the
        // immutable module so pipeline creation performs no repeated string conversion.
        byte[] utf8Source = new byte[Encoding.UTF8.GetByteCount(source) + 1];
        _ = Encoding.UTF8.GetBytes(source, utf8Source);
        return new WebGPUShaderModuleSource(source, utf8Source, userLineStart, userLineCount);
    }

    /// <summary>
    /// Appends one axis of the pass-specific border-coordinate transform.
    /// </summary>
    /// <param name="builder">The generated WGSL module.</param>
    /// <param name="axis">The framework coordinate axis.</param>
    /// <param name="borderMode">The ImageSharp convolution border mode, or transparent sampling when absent.</param>
    private static void AppendBorderCoordinateFunction(
        StringBuilder builder,
        char axis,
        BorderWrappingMode? borderMode)
    {
        _ = builder.Append("fn imagesharp_wrap_").Append(axis).AppendLine("(value: i32) -> i32 {");

        if (!borderMode.HasValue)
        {
            // An unconfigured axis retains layer_sample's public transparent-outside contract.
            _ = builder.AppendLine("    return value;");
            _ = builder.AppendLine("}");
            _ = builder.AppendLine();
            return;
        }

        _ = builder.Append("    let imagesharp_minimum = imagesharp_framework.imagesharp_valid_min.").Append(axis).AppendLine(";");
        _ = builder.Append("    let imagesharp_maximum = imagesharp_framework.imagesharp_valid_max.").Append(axis).AppendLine(" - 1;");

        switch (borderMode.Value)
        {
            case BorderWrappingMode.Repeat:
                // ImageSharp's Repeat convolution mode extends the nearest border sample.
                _ = builder.AppendLine("    return clamp(value, imagesharp_minimum, imagesharp_maximum);");
                break;

            case BorderWrappingMode.Wrap:
                _ = builder.AppendLine("    let imagesharp_extent = imagesharp_maximum - imagesharp_minimum + 1;");
                _ = builder.AppendLine("    let imagesharp_remainder = (value - imagesharp_minimum) % imagesharp_extent;");
                _ = builder.AppendLine("    let imagesharp_offset = (imagesharp_remainder + imagesharp_extent) % imagesharp_extent;");
                _ = builder.AppendLine("    return imagesharp_minimum + imagesharp_offset;");
                break;

            case BorderWrappingMode.Mirror:
                _ = builder.AppendLine("    let imagesharp_extent = imagesharp_maximum - imagesharp_minimum + 1;");
                _ = builder.AppendLine("    let imagesharp_period = imagesharp_extent * 2;");
                _ = builder.AppendLine("    let imagesharp_remainder = (value - imagesharp_minimum) % imagesharp_period;");
                _ = builder.AppendLine("    let imagesharp_offset = (imagesharp_remainder + imagesharp_period) % imagesharp_period;");
                _ = builder.AppendLine("    let imagesharp_reflected = select(imagesharp_period - 1 - imagesharp_offset, imagesharp_offset, imagesharp_offset < imagesharp_extent);");
                _ = builder.AppendLine("    return imagesharp_minimum + imagesharp_reflected;");
                break;

            case BorderWrappingMode.Bounce:
                _ = builder.AppendLine("    let imagesharp_extent = imagesharp_maximum - imagesharp_minimum + 1;");
                _ = builder.AppendLine("    if (imagesharp_extent == 1) {");
                _ = builder.AppendLine("        return imagesharp_minimum;");
                _ = builder.AppendLine("    }");
                _ = builder.AppendLine();
                _ = builder.AppendLine("    let imagesharp_period = (imagesharp_extent * 2) - 2;");
                _ = builder.AppendLine("    let imagesharp_remainder = (value - imagesharp_minimum) % imagesharp_period;");
                _ = builder.AppendLine("    let imagesharp_offset = (imagesharp_remainder + imagesharp_period) % imagesharp_period;");
                _ = builder.AppendLine("    let imagesharp_reflected = select(imagesharp_period - imagesharp_offset, imagesharp_offset, imagesharp_offset < imagesharp_extent);");
                _ = builder.AppendLine("    return imagesharp_minimum + imagesharp_reflected;");
                break;
        }

        _ = builder.AppendLine("}");
        _ = builder.AppendLine();
    }

    /// <summary>
    /// Counts one-based lines in a partially generated module without allocating an intermediate string.
    /// </summary>
    /// <param name="builder">The generated source accumulated so far.</param>
    /// <returns>The one-based line count.</returns>
    private static int CountLines(StringBuilder builder)
    {
        int count = 1;
        bool previousWasCarriageReturn = false;

        // StringBuilder chunks avoid both the comparatively expensive indexer and a temporary
        // flattened string. Carrying the CR state across chunks ensures a split CRLF is one line end.
        foreach (ReadOnlyMemory<char> chunk in builder.GetChunks())
        {
            count += CountLineEndings(chunk.Span, ref previousWasCarriageReturn);
        }

        return count;
    }

    /// <summary>
    /// Counts one-based lines in user WGSL for diagnostic source mapping.
    /// </summary>
    /// <param name="value">The source text to inspect.</param>
    /// <returns>The one-based line count.</returns>
    private static int CountLines(string value)
    {
        bool previousWasCarriageReturn = false;
        return 1 + CountLineEndings(value, ref previousWasCarriageReturn);
    }

    /// <summary>
    /// Counts Unicode newline indicators without allocating, treating CRLF as one line ending.
    /// </summary>
    /// <param name="value">The source segment to inspect.</param>
    /// <param name="previousWasCarriageReturn">
    /// Indicates whether the preceding segment ended with CR so a leading LF completes the same line ending.
    /// </param>
    /// <returns>The number of complete line endings represented by the segment.</returns>
    private static int CountLineEndings(ReadOnlySpan<char> value, ref bool previousWasCarriageReturn)
    {
        int count = 0;

        while (!value.IsEmpty)
        {
            int lineBreakIndex = value.IndexOfAny(LineBreakCharacters);
            if (lineBreakIndex < 0)
            {
                // Ordinary text separates a trailing CR from any LF in the next builder chunk.
                previousWasCarriageReturn = false;
                break;
            }

            if (lineBreakIndex > 0)
            {
                // A preceding non-line-break character means this cannot complete a cross-chunk CRLF.
                previousWasCarriageReturn = false;
            }

            char lineBreak = value[lineBreakIndex];
            if (lineBreak != '\n' || !previousWasCarriageReturn)
            {
                count++;
            }

            previousWasCarriageReturn = lineBreak == '\r';
            value = value[(lineBreakIndex + 1)..];
        }

        return count;
    }
}
