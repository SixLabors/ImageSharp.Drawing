// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Color mixing and compositing math for layer blending.
//
// Imported by fine.wgsl, which calls blend_mix_compose when popping a clip or
// layer (CMD_END_CLIP) to combine the layer contents with its backdrop. The
// math mirrors the CPU drawing backend's blending so both backends produce
// the same output for a given GraphicsOptions.
//
// Ported from Vello's shader/shared/blend.wgsl (linebender/vello).

// Color mixing modes.
//
// These are the high byte of the packed blend-mode word ((mix << 8) | compose)
// produced by WebGPUSceneEncoder.PackBlendMode, so every value documents the
// wire format shared with the C# encoder even where no WGSL code names it.
// The values mirror the CSS mix-blend-mode list from Vello; the C# encoder
// currently emits only NORMAL, MULTIPLY, SCREEN, OVERLAY, DARKEN, LIGHTEN,
// HARD_LIGHT, ADD, and SUBTRACT.

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
// 15, which blend_mix_compose masks off so clip layers take the plain
// src-over fast path. No WGSL code names this constant; it documents the
// wire format shared with the encoder.
const MIX_CLIP = 128u;

// Screen blend: inverted multiply of the inverted channels.
fn screen(cb: vec3<f32>, cs: vec3<f32>) -> vec3<f32> {
    return cb + cs - (cb * cs);
}

// Per-channel color-dodge blend. The zero and one cases are handled
// explicitly to avoid the division blowing up.
fn color_dodge(cb: f32, cs: f32) -> f32 {
    if cb == 0.0 {
        return 0.0;
    } else if cs == 1.0 {
        return 1.0;
    } else {
        return min(1.0, cb / (1.0 - cs));
    }
}

// Per-channel color-burn blend, the dual of color_dodge.
fn color_burn(cb: f32, cs: f32) -> f32 {
    if cb == 1.0 {
        return 1.0;
    } else if cs == 0.0 {
        return 0.0;
    } else {
        return 1.0 - min(1.0, (1.0 - cb) / cs);
    }
}

// Hard-light blend: multiply for dark source channels, screen for light ones.
// Overlay is implemented as hard_light with the operands swapped.
fn hard_light(cb: vec3<f32>, cs: vec3<f32>) -> vec3<f32> {
    return select(
        screen(cb, 2.0 * cs - 1.0),
        cb * 2.0 * cs,
        cs <= vec3(0.5)
    );
}

// Soft-light blend per the W3C compositing spec, using the piecewise
// polynomial approximation of the darkening curve for cb <= 0.25.
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

// Saturation of a color: the spread between its largest and smallest channel.
fn sat(c: vec3<f32>) -> f32 {
    return max(c.x, max(c.y, c.z)) - min(c.x, min(c.y, c.z));
}

// Luminosity using the NTSC weights specified by the W3C compositing spec
// for the non-separable blend modes.
fn lum(c: vec3<f32>) -> f32 {
    let f = vec3(0.3, 0.59, 0.11);
    return dot(c, f);
}

// Luminance using the SVG/Rec. 709 coefficients. Used by fine.wgsl to build
// luminance masks for hard-mask clips rather than for blending.
fn svg_lum(c: vec3<f32>) -> f32 {
    let f = vec3(0.2125, 0.7154, 0.0721);
    return dot(c, f);
}

// Clamps an out-of-gamut color back into [0, 1] by scaling its channels
// toward the luminosity, preserving perceived lightness (spec ClipColor).
fn clip_color(c_in: vec3<f32>) -> vec3<f32> {
    var c = c_in;
    let l = lum(c);
    let n = min(c.x, min(c.y, c.z));
    let x = max(c.x, max(c.y, c.z));
    if n < 0.0 {
        c = l + (((c - l) * l) / (l - n));
    }
    if x > 1.0 {
        c = l + (((c - l) * (1.0 - l)) / (x - l));
    }
    return c;
}

// Shifts a color to the target luminosity, then re-clips into gamut.
fn set_lum(c: vec3<f32>, l: f32) -> vec3<f32> {
    return clip_color(c + (l - lum(c)));
}

// Rescales three channel values, already ordered min/mid/max, so that the
// spread becomes s while the mid channel keeps its relative position.
fn set_sat_inner(
    cmin: ptr<function, f32>,
    cmid: ptr<function, f32>,
    cmax: ptr<function, f32>,
    s: f32
) {
    if *cmax > *cmin {
        *cmid = ((*cmid - *cmin) * s) / (*cmax - *cmin);
        *cmax = s;
    } else {
        *cmid = 0.0;
        *cmax = 0.0;
    }
    *cmin = 0.0;
}

// Sets the saturation of a color to s (spec SetSat). The branch ladder
// orders the channels so set_sat_inner receives them as min/mid/max.
fn set_sat(c: vec3<f32>, s: f32) -> vec3<f32> {
    var r = c.r;
    var g = c.g;
    var b = c.b;
    if r <= g {
        if g <= b {
            set_sat_inner(&r, &g, &b, s);
        } else {
            if r <= b {
                set_sat_inner(&r, &b, &g, s);
            } else {
                set_sat_inner(&b, &r, &g, s);
            }
        }
    } else {
        if r <= b {
            set_sat_inner(&g, &r, &b, s);
        } else {
            if g <= b {
                set_sat_inner(&g, &b, &r, s);
            } else {
                set_sat_inner(&b, &g, &r, s);
            }
        }
    }
    return vec3(r, g, b);
}

// Blends two RGB colors together using the given MIX_* mode. The colors are
// assumed to be in sRGB color space, and this function does not take alpha
// into account; unknown modes (including MIX_NORMAL) return the source.
fn blend_mix(cb: vec3<f32>, cs: vec3<f32>, mode: u32) -> vec3<f32> {
    var b = vec3(0.0);
    switch mode {
        case MIX_MULTIPLY: {
            b = cb * cs;
        }
        case MIX_SCREEN: {
            b = screen(cb, cs);
        }
        case MIX_OVERLAY: {
            b = hard_light(cs, cb);
        }
        case MIX_DARKEN: {
            b = min(cb, cs);
        }
        case MIX_LIGHTEN: {
            b = max(cb, cs);
        }
        case MIX_ADD: {
            b = min(vec3(1.0), cb + cs);
        }
        case MIX_SUBTRACT: {
            b = max(vec3(0.0), cb - cs);
        }
        case MIX_COLOR_DODGE: {
            b = vec3(color_dodge(cb.x, cs.x), color_dodge(cb.y, cs.y), color_dodge(cb.z, cs.z));
        }
        case MIX_COLOR_BURN: {
            b = vec3(color_burn(cb.x, cs.x), color_burn(cb.y, cs.y), color_burn(cb.z, cs.z));
        }
        case MIX_HARD_LIGHT: {
            b = hard_light(cb, cs);
        }
        case MIX_SOFT_LIGHT: {
            b = soft_light(cb, cs);
        }
        case MIX_DIFFERENCE: {
            b = abs(cb - cs);
        }
        case MIX_EXCLUSION: {
            b = cb + cs - 2.0 * cb * cs;
        }
        case MIX_HUE: {
            b = set_lum(set_sat(cs, sat(cb)), lum(cb));
        }
        case MIX_SATURATION: {
            b = set_lum(set_sat(cb, sat(cs)), lum(cb));
        }
        case MIX_COLOR: {
            b = set_lum(cs, lum(cb));
        }
        case MIX_LUMINOSITY: {
            b = set_lum(cb, lum(cs));
        }
        default: {
            b = cs;
        }
    }
    return b;
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

// Apply general compositing operation.
// Inputs are separated colors and alpha, output is premultiplied.
// fa/fb are the Porter-Duff source and backdrop fractions for the mode;
// COMPOSE_CLEAR falls through the default with fa = fb = 0.
fn blend_compose(
    cb: vec3<f32>,
    cs: vec3<f32>,
    ab: f32,
    as_: f32,
    compose_mode: u32,
) -> vec4<f32> {
    var fa = 0.0;
    var fb = 0.0;
    switch compose_mode {
        case COMPOSE_COPY: {
            fa = 1.0;
            fb = 0.0;
        }
        case COMPOSE_DEST: {
            fa = 0.0;
            fb = 1.0;
        }
        case COMPOSE_SRC_OVER: {
            fa = 1.0;
            fb = 1.0 - as_;
        }
        case COMPOSE_DEST_OVER: {
            fa = 1.0 - ab;
            fb = 1.0;
        }
        case COMPOSE_SRC_IN: {
            fa = ab;
            fb = 0.0;
        }
        case COMPOSE_DEST_IN: {
            fa = 0.0;
            fb = as_;
        }
        case COMPOSE_SRC_OUT: {
            fa = 1.0 - ab;
            fb = 0.0;
        }
        case COMPOSE_DEST_OUT: {
            fa = 0.0;
            fb = 1.0 - as_;
        }
        case COMPOSE_SRC_ATOP: {
            fa = ab;
            fb = 1.0 - as_;
        }
        case COMPOSE_DEST_ATOP: {
            fa = 1.0 - ab;
            fb = as_;
        }
        case COMPOSE_XOR: {
            fa = 1.0 - ab;
            fb = 1.0 - as_;
        }
        case COMPOSE_PLUS: {
            fa = 1.0;
            fb = 1.0;
        }
        case COMPOSE_PLUS_LIGHTER: {
            return min(vec4(1.0), vec4(as_ * cs + ab * cb, as_ + ab));
        }
        default: {}
    }
    let as_fa = as_ * fa;
    let ab_fb = ab * fb;
    let co = as_fa * cs + ab_fb * cb;
    // Modes like COMPOSE_PLUS can generate alpha > 1.0, so clamp.
    return vec4(co, min(as_fa + ab_fb, 1.0));
}

// Converts a premultiplied color to separated RGB by dividing out alpha.
fn unpremultiply(color: vec4<f32>) -> vec3<f32> {
    let EPSILON = 1e-15;
    // Max with a small epsilon to avoid NaNs.
    let inv_alpha = 1.0 / max(color.a, EPSILON);
    return color.rgb * inv_alpha;
}

// Apply color mixing and composition. Both input and output colors are
// premultiplied RGB. `mode` is the packed word (mix << 8) | compose written
// by the C# encoder's PackBlendMode.
fn blend_mix_compose(backdrop: vec4<f32>, src: vec4<f32>, mode: u32) -> vec4<f32> {
    let BLEND_DEFAULT = ((MIX_NORMAL << 8u) | COMPOSE_SRC_OVER);
    // The 0x7fff mask strips bit 15, the MIX_CLIP marker, so both the plain
    // normal+src_over blend and the pure clip case take this fast path.
    if (mode & 0x7fffu) == BLEND_DEFAULT {
        return backdrop * (1.0 - src.a) + src;
    }
    // Un-premultiply colors for blending.
    var cs = unpremultiply(src);
    let cb = unpremultiply(backdrop);
    let mix_mode = mode >> 8u;
    let mixed = blend_mix(cb, cs, mix_mode);
    cs = mix(cs, mixed, backdrop.a);
    let compose_mode = mode & 0xffu;
    if compose_mode == COMPOSE_SRC_OVER {
        let co = mix(backdrop.rgb, cs, src.a);
        return vec4(co, src.a + backdrop.a * (1.0 - src.a));
    } else {
        return blend_compose(cb, cs, backdrop.a, src.a, compose_mode);
    }
}
