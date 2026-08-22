// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Color mixing and compositing math for draw and layer blending.
//
// Imported by fine.wgsl for per-draw composition (compose_draw), recolor
// inner blends, and layer pops (CMD_END_CLIP). ImageSharp's CPU renderer is
// the parity authority for every formula, branch, and operand order below:
//
//   - AssociatedAlphaPorterDuffFunctions: composition operators and the
//     associated overlap terms.
//   - AssociatedAlphaPorterDuffFunctions.Generated: the mode-to-operator
//     mapping and the opacity application.
//   - PorterDuffFunctions: the straight-RGB blend curves (ColorDodge through
//     Luminosity) together with SetSaturation, SetLuminosity, and Luminosity.
//   - Numerics.UnPremultiply: zero-alpha semantics for straight recovery.
//
// Colors are associated (premultiplied) on input and output throughout.
// Straight RGB is recovered only inside the blend modes whose CPU equations
// are defined on straight colors, at the same point, and re-associated in
// the same order, as the CPU implementation.

// Color mixing modes.
//
// These are the high byte of the packed blend-mode word ((mix << 8) | compose)
// produced by WebGPUSceneEncoder.PackBlendMode, so every value documents the
// wire format shared with the C# encoder even where no WGSL code names it.
// The values mirror the CSS mix-blend-mode list from Vello; the C# encoder
// emits every ImageSharp PixelColorBlendingMode value.

const MIX_NORMAL = 0u;
const MIX_MULTIPLY = 1u;
const MIX_SCREEN = 2u;
const MIX_OVERLAY = 3u;
const MIX_DARKEN = 4u;
const MIX_LIGHTEN = 5u;
const MIX_ADD = 16u;
const MIX_SUBTRACT = 17u;
const MIX_COLOR_DODGE = 6u;
const MIX_COLOR_BURN = 7u;
const MIX_HARD_LIGHT = 8u;
const MIX_SOFT_LIGHT = 9u;
const MIX_DIFFERENCE = 10u;
const MIX_EXCLUSION = 11u;
const MIX_HUE = 12u;
const MIX_SATURATION = 13u;
const MIX_COLOR = 14u;
const MIX_LUMINOSITY = 15u;
// Wire value written by the C# encoder (WebGPUSceneEncoder.ClipBlendMode) to
// mark a pure clip layer. As the mix half of the packed word it occupies bit
// 15, which compose_source masks off so clip layers take the plain
// normal source-over fast path. No WGSL code names this constant; it
// documents the wire format shared with the encoder.
const MIX_CLIP = 128u;

// Converts an associated color to straight RGB. Mirrors
// Numerics.UnPremultiply: zero alpha has no mathematical inverse, so the
// stored RGB passes through unchanged instead of collapsing to zero.
fn unpremultiply(color: vec4<f32>) -> vec3<f32> {
    if color.a == 0.0 {
        return color.rgb;
    }

    return color.rgb / color.a;
}

// Per-channel color-dodge blend (PorterDuffFunctions.ColorDodgeValue). The
// singular zero-backdrop and one-source cases are resolved before the
// division, in the CPU's precedence order, so transparent or saturated
// channels never divide by zero.
fn color_dodge(cb: f32, cs: f32) -> f32 {
    if cb == 0.0 {
        return 0.0;
    } else if cs == 1.0 {
        return 1.0;
    } else {
        return min(1.0, cb / (1.0 - cs));
    }
}

// Per-channel color-burn blend (PorterDuffFunctions.ColorBurnValue), the dual
// of color_dodge with the same explicit singular handling.
fn color_burn(cb: f32, cs: f32) -> f32 {
    if cb == 1.0 {
        return 1.0;
    } else if cs == 0.0 {
        return 0.0;
    } else {
        return 1.0 - min(1.0, (1.0 - cb) / cs);
    }
}

// Soft-light blend (PorterDuffFunctions.SoftLightValue): multiply-like
// darkening for source <= 0.5, otherwise a lightening curve whose cubic
// segment below backdrop 0.25 joins the square-root segment smoothly while
// avoiding its steep slope near zero.
fn soft_light(cb: vec3<f32>, cs: vec3<f32>) -> vec3<f32> {
    let d = select(
        sqrt(cb),
        ((16.0 * cb - 12.0) * cb + 4.0) * cb,
        cb <= vec3(0.25)
    );
    return select(
        cb + (2.0 * cs - 1.0) * (d - cb),
        cb - (1.0 - 2.0 * cs) * cb * (1.0 - cb),
        cs <= vec3(0.5)
    );
}

// Saturation of a straight color (PorterDuffFunctions.Saturation): the spread
// between its largest and smallest channel.
fn sat(c: vec3<f32>) -> f32 {
    return max(c.x, max(c.y, c.z)) - min(c.x, min(c.y, c.z));
}

// Luminosity of a straight color (PorterDuffFunctions.Luminosity). The
// explicit scalar operation order is kept because a dot() product may be
// contracted differently by a shader compiler.
fn lum(c: vec3<f32>) -> f32 {
    return (0.3 * c.x) + (0.59 * c.y) + (0.11 * c.z);
}

// Luminance using the SVG/Rec. 709 coefficients. Used by fine.wgsl to build
// luminance masks for hard-mask clips rather than for blending.
fn svg_lum(c: vec3<f32>) -> f32 {
    let f = vec3(0.2125, 0.7154, 0.0721);
    return dot(c, f);
}

// Replaces the saturation of a color (PorterDuffFunctions.SetSaturation).
// Translating the minimum to zero and scaling the range is equivalent to the
// specification's ordered-channel construction; the CPU's scalar-ratio order
// is kept so both backends round identically.
fn set_sat(c: vec3<f32>, s: f32) -> vec3<f32> {
    let minimum = min(c.x, min(c.y, c.z));
    let range = max(c.x, max(c.y, c.z)) - minimum;
    if range == 0.0 {
        return vec3(0.0);
    }

    return (c - vec3(minimum)) * (s / range);
}

// Replaces the luminosity of a color and clips the result to the
// representable gamut (PorterDuffFunctions.SetLuminosity). Out-of-gamut
// channels are pulled toward the requested luminosity by one shared ratio so
// hue is preserved. The clip anchors on the requested luminosity l, not a
// recomputed one, exactly as the CPU does.
fn set_lum(c_in: vec3<f32>, l: f32) -> vec3<f32> {
    var c = c_in + vec3(l - lum(c_in));

    let minimum = min(c.x, min(c.y, c.z));
    if minimum < 0.0 {
        c = vec3(l) + ((c - vec3(l)) * (l / (l - minimum)));
    }

    let maximum = max(c.x, max(c.y, c.z));
    if maximum > 1.0 {
        c = vec3(l) + ((c - vec3(l)) * ((1.0 - l) / (maximum - l)));
    }

    return c;
}

// Straight-RGB blend curves for the modes whose CPU equations are defined on
// straight colors (PorterDuffFunctions.ColorDodge through Luminosity). cb and
// cs are the straight backdrop and source recovered by the caller, and the
// straight result is re-associated by the caller. The separable legacy modes
// never reach this function; their overlap terms stay associated in
// blend_overlap.
fn blend_mix(cb: vec3<f32>, cs: vec3<f32>, mode: u32) -> vec3<f32> {
    switch mode {
        case MIX_COLOR_DODGE: {
            return vec3(color_dodge(cb.x, cs.x), color_dodge(cb.y, cs.y), color_dodge(cb.z, cs.z));
        }
        case MIX_COLOR_BURN: {
            return vec3(color_burn(cb.x, cs.x), color_burn(cb.y, cs.y), color_burn(cb.z, cs.z));
        }
        case MIX_SOFT_LIGHT: {
            return soft_light(cb, cs);
        }
        case MIX_DIFFERENCE: {
            return abs(cb - cs);
        }
        case MIX_EXCLUSION: {
            return cb + cs - 2.0 * cb * cs;
        }
        case MIX_HUE: {
            return set_lum(set_sat(cs, sat(cb)), lum(cb));
        }
        case MIX_SATURATION: {
            return set_lum(set_sat(cb, sat(cs)), lum(cb));
        }
        case MIX_COLOR: {
            return set_lum(cs, lum(cb));
        }
        case MIX_LUMINOSITY: {
            return set_lum(cb, lum(cs));
        }
        default: {
            return cs;
        }
    }
}

// Composition modes.
//
// Porter-Duff style alpha composition operators. These are the low byte of
// the packed blend-mode word and match the values produced by the C# encoder
// (WebGPUSceneEncoder.MapAlphaCompositionMode), so unused-looking entries
// still document the shared wire format.

const COMPOSE_CLEAR = 0u;
const COMPOSE_COPY = 1u;
const COMPOSE_DEST = 2u;
const COMPOSE_SRC_OVER = 3u;
const COMPOSE_DEST_OVER = 4u;
const COMPOSE_SRC_IN = 5u;
const COMPOSE_DEST_IN = 6u;
const COMPOSE_SRC_OUT = 7u;
const COMPOSE_DEST_OUT = 8u;
const COMPOSE_SRC_ATOP = 9u;
const COMPOSE_DEST_ATOP = 10u;
const COMPOSE_XOR = 11u;
const COMPOSE_PLUS = 12u;
const COMPOSE_PLUS_LIGHTER = 13u;

// Composition operators ported one-for-one from
// AssociatedAlphaPorterDuffFunctions. All vectors are associated, and each
// operand order matches the CPU's scalar Vector4 implementation so both
// backends round identically. The overlap argument is the associated
// color-blend term for the region where source and destination coverage
// intersect; alpha never takes the overlap value.

// AssociatedAlphaPorterDuffFunctions.OverNormal: Ps + Pb(1 - As). Color and
// alpha share the same coefficient, so one whole-vector expression suffices.
fn compose_over_normal(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
    return source + (destination * (1.0 - source.a));
}

// AssociatedAlphaPorterDuffFunctions.Over: destination-only, source-only, and
// blended overlap contributions, with plain source-over alpha.
fn compose_over(destination: vec4<f32>, source: vec4<f32>, overlap: vec3<f32>) -> vec4<f32> {
    let color = ((destination * (1.0 - source.a)) + (source * (1.0 - destination.a))).rgb + overlap;
    let alpha = source.a + (destination.a * (1.0 - source.a));
    return vec4(color, alpha);
}

// AssociatedAlphaPorterDuffFunctions.AtopNormal: the source replaces the
// destination's covered contribution while total coverage stays put.
fn compose_atop_normal(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
    return (source * destination.a) + (destination * (1.0 - source.a));
}

// AssociatedAlphaPorterDuffFunctions.Atop: fused destination retention plus
// the blended overlap; the destination alpha is preserved exactly. The CPU
// uses a fused multiply-add here, so fma() is the closest expression.
fn compose_atop(destination: vec4<f32>, source: vec4<f32>, overlap: vec3<f32>) -> vec4<f32> {
    return vec4(fma(destination.rgb, vec3(1.0 - source.a), overlap), destination.a);
}

// AssociatedAlphaPorterDuffFunctions.In: retain the source inside the
// destination coverage.
fn compose_in(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
    return source * destination.a;
}

// AssociatedAlphaPorterDuffFunctions.Out: retain the source outside the
// destination coverage.
fn compose_out(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
    return source * (1.0 - destination.a);
}

// AssociatedAlphaPorterDuffFunctions.Xor: keep only the non-overlapping parts
// of the two vectors.
fn compose_xor(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
    return (source * (1.0 - destination.a)) + (destination * (1.0 - source.a));
}

// AssociatedAlphaPorterDuffFunctions.PlusNormal: clamped additive
// composition without a color-blending function.
fn compose_plus_normal(destination: vec4<f32>, source: vec4<f32>) -> vec4<f32> {
    let alpha = min(1.0, source.a + destination.a);
    return vec4(min(vec3(1.0), (destination + source).rgb), alpha);
}

// AssociatedAlphaPorterDuffFunctions.Plus: the blended source contributes
// straight source color outside the destination and the overlap term inside,
// with both color and alpha clamped to the representable range.
fn compose_plus(destination: vec4<f32>, source: vec4<f32>, overlap: vec3<f32>) -> vec4<f32> {
    let alpha = min(1.0, source.a + destination.a);
    let color = (destination + (source * (1.0 - destination.a))).rgb + overlap;
    return vec4(min(vec3(1.0), color), alpha);
}

// One associated Overlay/HardLight overlap channel group
// (AssociatedAlphaPorterDuffFunctions.OverlayValue). Comparing 2Pb with Ab is
// equivalent to comparing the straight backdrop channel with one half, so no
// unpremultiply is needed. Doubling is exact in binary floating point, which
// keeps this bit-identical to the CPU's scalar operation order.
fn overlay_value(backdrop: vec3<f32>, backdrop_alpha: f32, source: vec3<f32>, source_alpha: f32) -> vec3<f32> {
    let doubled = backdrop + backdrop;
    let multiply_side = doubled * source;
    let screen_side = vec3(backdrop_alpha * source_alpha) - (2.0 * (vec3(backdrop_alpha) - backdrop) * (vec3(source_alpha) - source));
    return select(screen_side, multiply_side, doubled <= vec3(backdrop_alpha));
}

// The associated overlap term produced by the color-blending half of
// AssociatedAlphaPorterDuffFunctions. The separable legacy modes stay in
// associated arithmetic exactly as the CPU does. The straight-RGB modes
// recover straight colors (preserving RGB at zero alpha), blend, then
// re-associate by multiplying by backdrop alpha and then source alpha, which
// is the CPU's exact multiplication order.
fn blend_overlap(backdrop: vec4<f32>, source: vec4<f32>, mix_mode: u32) -> vec3<f32> {
    switch mix_mode {
        case MIX_MULTIPLY: {
            return backdrop.rgb * source.rgb;
        }
        case MIX_ADD: {
            return min(vec3(backdrop.a * source.a), (backdrop.rgb * source.a) + (source.rgb * backdrop.a));
        }
        case MIX_SUBTRACT: {
            return max(vec3(0.0), (backdrop.rgb * source.a) - (source.rgb * backdrop.a));
        }
        case MIX_SCREEN: {
            return (backdrop.rgb * source.a) + (source.rgb * backdrop.a) - (backdrop.rgb * source.rgb);
        }
        case MIX_DARKEN: {
            return min(backdrop.rgb * source.a, source.rgb * backdrop.a);
        }
        case MIX_LIGHTEN: {
            return max(backdrop.rgb * source.a, source.rgb * backdrop.a);
        }
        case MIX_OVERLAY: {
            return overlay_value(backdrop.rgb, backdrop.a, source.rgb, source.a);
        }
        case MIX_HARD_LIGHT: {
            return overlay_value(source.rgb, source.a, backdrop.rgb, backdrop.a);
        }
        case MIX_COLOR_DODGE, MIX_COLOR_BURN, MIX_SOFT_LIGHT, MIX_DIFFERENCE, MIX_EXCLUSION, MIX_HUE, MIX_SATURATION, MIX_COLOR, MIX_LUMINOSITY: {
            let mixed = blend_mix(unpremultiply(backdrop), unpremultiply(source), mix_mode);
            return (mixed * backdrop.a) * source.a;
        }
        default: {
            return vec3(0.0);
        }
    }
}

// Composites an associated source over an associated backdrop using the
// packed blend word ((mix << 8) | compose) and the source opacity. This is
// the GPU equivalent of the generated AssociatedAlphaPorterDuffFunctions
// operators: opacity scales the associated source as a whole (RGB and alpha
// together) so it cannot change the represented straight color, Dest and
// Clear ignore the source, and each compose mode maps to the same operator
// family as the CPU's GetPixelBlender switch, with source-over serving as
// the default exactly as it does on the CPU.
fn compose_source(backdrop: vec4<f32>, source: vec4<f32>, opacity: f32, mode: u32) -> vec4<f32> {
    // Bit 15 of the packed word carries the encoder's MIX_CLIP marker;
    // stripping it lets pure clip layers take the normal source-over path.
    let mix_mode = (mode >> 8u) & 0x7fu;
    let compose_mode = mode & 0xffu;
    let scaled = source * opacity;

    // Normal source-over dominates real scenes, so resolve it before the
    // switch. The formula is compose_over_normal exactly.
    if compose_mode == COMPOSE_SRC_OVER && mix_mode == MIX_NORMAL {
        return scaled + (backdrop * (1.0 - scaled.a));
    }

    let normal = mix_mode == MIX_NORMAL;
    switch compose_mode {
        case COMPOSE_CLEAR: {
            return vec4(0.0);
        }
        case COMPOSE_COPY: {
            return scaled;
        }
        case COMPOSE_DEST: {
            return backdrop;
        }
        case COMPOSE_DEST_OVER: {
            if normal {
                return compose_over_normal(scaled, backdrop);
            }

            return compose_over(scaled, backdrop, blend_overlap(scaled, backdrop, mix_mode));
        }
        case COMPOSE_SRC_IN: {
            return compose_in(backdrop, scaled);
        }
        case COMPOSE_DEST_IN: {
            return compose_in(scaled, backdrop);
        }
        case COMPOSE_SRC_OUT: {
            return compose_out(backdrop, scaled);
        }
        case COMPOSE_DEST_OUT: {
            return compose_out(scaled, backdrop);
        }
        case COMPOSE_SRC_ATOP: {
            if normal {
                return compose_atop_normal(backdrop, scaled);
            }

            return compose_atop(backdrop, scaled, blend_overlap(backdrop, scaled, mix_mode));
        }
        case COMPOSE_DEST_ATOP: {
            if normal {
                return compose_atop_normal(scaled, backdrop);
            }

            return compose_atop(scaled, backdrop, blend_overlap(scaled, backdrop, mix_mode));
        }
        case COMPOSE_XOR: {
            return compose_xor(backdrop, scaled);
        }
        case COMPOSE_PLUS: {
            if normal {
                return compose_plus_normal(backdrop, scaled);
            }

            return compose_plus(backdrop, scaled, blend_overlap(backdrop, scaled, mix_mode));
        }
        default: {
            if normal {
                return compose_over_normal(backdrop, scaled);
            }

            return compose_over(backdrop, scaled, blend_overlap(backdrop, scaled, mix_mode));
        }
    }
}
