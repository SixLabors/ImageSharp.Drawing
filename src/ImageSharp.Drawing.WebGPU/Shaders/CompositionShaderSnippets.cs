// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Shared WGSL function snippets used by multiple compute shaders that perform pixel blending
/// and alpha composition (e.g., <see cref="CompositeComputeShader"/> and <see cref="ComposeLayerComputeShader"/>).
/// </summary>
internal static class CompositionShaderSnippets
{
    /// <summary>
    /// WGSL functions for unpremultiplying alpha, blending colors by mode,
    /// and compositing pixels using Porter-Duff alpha composition.
    /// </summary>
    internal const string BlendAndCompose =
        """
        fn unpremultiply(rgb: vec3<f32>, alpha: f32) -> vec3<f32> {
            if (alpha <= 0.0) {
                return vec3<f32>(0.0);
            }

            return rgb / alpha;
        }

        fn blend_color(backdrop: vec3<f32>, source: vec3<f32>, mode: u32) -> vec3<f32> {
            switch mode {
                case 1u: {
                    return backdrop * source;
                }
                case 2u: {
                    return backdrop + source;
                }
                case 3u: {
                    return backdrop - source;
                }
                case 4u: {
                    return 1.0 - ((1.0 - backdrop) * (1.0 - source));
                }
                case 5u: {
                    return min(backdrop, source);
                }
                case 6u: {
                    return max(backdrop, source);
                }
                case 7u: {
                    return select(
                        2.0 * backdrop * source,
                        1.0 - (2.0 * (1.0 - backdrop) * (1.0 - source)),
                        backdrop >= vec3<f32>(0.5));
                }
                case 8u: {
                    return select(
                        2.0 * backdrop * source,
                        1.0 - (2.0 * (1.0 - backdrop) * (1.0 - source)),
                        source >= vec3<f32>(0.5));
                }
                default: {
                    return source;
                }
            }
        }

        fn compose_pixel(destination_premul: vec4<f32>, source: vec4<f32>, color_mode: u32, alpha_mode: u32) -> vec4<f32> {
            let destination_alpha = destination_premul.a;
            let destination_rgb_straight = unpremultiply(destination_premul.rgb, destination_alpha);
            let source_alpha = source.a;
            let source_rgb = source.rgb;
            let source_premul = source_rgb * source_alpha;
            let forward_blend = blend_color(destination_rgb_straight, source_rgb, color_mode);
            let reverse_blend = blend_color(source_rgb, destination_rgb_straight, color_mode);
            let shared_alpha = source_alpha * destination_alpha;

            switch alpha_mode {
                case 1u: {
                    return vec4<f32>(source_premul, source_alpha);
                }
                case 2u: {
                    let premul = (destination_rgb_straight * (destination_alpha - shared_alpha)) + (forward_blend * shared_alpha);
                    return vec4<f32>(premul, destination_alpha);
                }
                case 3u: {
                    let alpha = source_alpha * destination_alpha;
                    return vec4<f32>(source_premul * destination_alpha, alpha);
                }
                case 4u: {
                    let alpha = source_alpha * (1.0 - destination_alpha);
                    return vec4<f32>(source_premul * (1.0 - destination_alpha), alpha);
                }
                case 5u: {
                    return destination_premul;
                }
                case 6u: {
                    let premul = (source_rgb * (source_alpha - shared_alpha)) + (reverse_blend * shared_alpha);
                    return vec4<f32>(premul, source_alpha);
                }
                case 7u: {
                    let alpha = destination_alpha + source_alpha - shared_alpha;
                    let premul =
                        (source_rgb * (source_alpha - shared_alpha)) +
                        (destination_rgb_straight * (destination_alpha - shared_alpha)) +
                        (reverse_blend * shared_alpha);
                    return vec4<f32>(premul, alpha);
                }
                case 8u: {
                    let alpha = destination_alpha * source_alpha;
                    return vec4<f32>(destination_premul.rgb * source_alpha, alpha);
                }
                case 9u: {
                    let alpha = destination_alpha * (1.0 - source_alpha);
                    return vec4<f32>(destination_premul.rgb * (1.0 - source_alpha), alpha);
                }
                case 10u: {
                    return vec4<f32>(0.0, 0.0, 0.0, 0.0);
                }
                case 11u: {
                    let source_term = source_premul * (1.0 - destination_alpha);
                    let destination_term = destination_premul.rgb * (1.0 - source_alpha);
                    let alpha = source_alpha * (1.0 - destination_alpha) + destination_alpha * (1.0 - source_alpha);
                    return vec4<f32>(source_term + destination_term, alpha);
                }
                default: {
                    let alpha = source_alpha + destination_alpha - shared_alpha;
                    let premul =
                        (destination_rgb_straight * (destination_alpha - shared_alpha)) +
                        (source_rgb * (source_alpha - shared_alpha)) +
                        (forward_blend * shared_alpha);
                    return vec4<f32>(premul, alpha);
                }
            }
        }
        """;
}
