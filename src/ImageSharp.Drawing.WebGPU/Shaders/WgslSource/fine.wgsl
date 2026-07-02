// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Fine rasterizer: the final stage of the pipeline. Each workgroup shades one
// 16x16 tile by interpreting the tile's command list (PTCL) written by
// coarse.wgsl. Coverage is computed analytically from the tile's line segments
// plus backdrop winding, then brushes (solid color, recolor, linear/radial/
// elliptic/sweep/path gradients, images) are evaluated and composited,
// including clip mask begin/end handling.
//
// Inputs: config uniform, segment buffer, ptcl and info streams, gradient ramp
// texture, image atlas, and a backdrop texture holding the existing target
// contents. Output: the rgba8unorm storage texture, written with straight
// (unpremultiplied) alpha. blend_spill provides scratch for clip stacks deeper
// than BLEND_STACK_SPLIT.
//
// Ported from Vello's fine.wgsl (vello_shaders/shader/fine.wgsl). Local
// divergences from upstream: per-fill aliased coverage thresholds, extra PTCL
// commands (CMD_RECOLOR, CMD_ELLIPTIC_GRAD, CMD_PATH_GRAD), clip difference and
// hard mask bits, per-command raster interest rectangles, backdrop texture
// seeding, tile-row chunking via config.chunk_tile_y_start, and gradient extend
// behavior deliberately matched to the CPU brushes.

#import segment
#import config
#import drawtag

// Scene configuration: target dimensions, tile counts, and the chunk window.
@group(0) @binding(0)
var<uniform> config: Config;

// Tile-relative line segments referenced by CMD_FILL commands.
@group(0) @binding(1)
var<storage> segments: array<Segment>;

#import blend
#import ptcl

// Width in texels of each gradient ramp row.
const GRADIENT_WIDTH = 512;

// Sentinel end-clip blend value: the layer is applied to its backdrop as a
// luminance mask rather than composited with a regular blend mode.
const LUMINANCE_MASK_LAYER = 0x10000u;

// Per-tile command lists written by coarse.wgsl.
@group(0) @binding(2)
var<storage> ptcl: array<u32>;

// Draw info stream: per-draw flags followed by brush payload words.
@group(0) @binding(3)
var<storage> info: array<u32>;

// Scratch for clip/blend stack entries deeper than BLEND_STACK_SPLIT.
@group(0) @binding(4)
var<storage, read_write> blend_spill: array<u32>;

// Final render target; written with straight (unpremultiplied) alpha.
@group(0) @binding(5)
var output: texture_storage_2d<rgba8unorm, write>;

// Gradient ramp texture: one GRADIENT_WIDTH-texel row per gradient.
@group(0) @binding(6)
var gradients: texture_2d<f32>;

// Atlas holding image brush sources.
@group(0) @binding(7)
var image_atlas: texture_2d<f32>;

// Existing target contents used to seed each pixel (straight alpha).
@group(0) @binding(8)
var backdrop_texture: texture_2d<f32>;

// Decodes a CMD_FILL payload: packed segment-count/fill-rule word, segment
// base index, backdrop winding, per-fill aliased coverage threshold, and the
// raster interest rectangle. As for all read_* decoders, cmd_ix addresses the
// command tag and the payload words follow it.
fn read_fill(cmd_ix: u32) -> CmdFill {
    let size_and_rule = ptcl[cmd_ix + 1u];
    let seg_data = ptcl[cmd_ix + 2u];
    let backdrop = i32(ptcl[cmd_ix + 3u]);
    let coverage_threshold = bitcast<f32>(ptcl[cmd_ix + 4u]);
    let interest = vec4<f32>(
        bitcast<f32>(ptcl[cmd_ix + 5u]),
        bitcast<f32>(ptcl[cmd_ix + 6u]),
        bitcast<f32>(ptcl[cmd_ix + 7u]),
        bitcast<f32>(ptcl[cmd_ix + 8u]));
    return CmdFill(size_and_rule, seg_data, backdrop, coverage_threshold, interest);
}

// Decodes a CMD_COLOR payload: packed rgba8 color and draw flags.
fn read_color(cmd_ix: u32) -> CmdColor {
    let rgba_color = ptcl[cmd_ix + 1u];
    let draw_flags = ptcl[cmd_ix + 2u];
    return CmdColor(rgba_color, draw_flags);
}

// Decodes a CMD_RECOLOR payload: source key color, replacement target color,
// match threshold (compared against squared RGBA distance), and draw flags.
fn read_recolor(cmd_ix: u32) -> CmdRecolor {
    let source_color = ptcl[cmd_ix + 1u];
    let target_color = ptcl[cmd_ix + 2u];
    let threshold = bitcast<f32>(ptcl[cmd_ix + 3u]);
    let draw_flags = ptcl[cmd_ix + 4u];
    return CmdRecolor(source_color, target_color, threshold, draw_flags);
}

// Decodes a CMD_LIN_GRAD payload. The first word packs the gradient ramp index
// (high bits) with the extend mode (low two bits); the implicit line equation
// coefficients are read from the info stream.
fn read_lin_grad(cmd_ix: u32) -> CmdLinGrad {
    let index_mode = ptcl[cmd_ix + 1u];
    let index = index_mode >> 2u;
    let extend_mode = index_mode & 0x3u;
    let info_offset = ptcl[cmd_ix + 2u];
    let line_x = bitcast<f32>(info[info_offset]);
    let line_y = bitcast<f32>(info[info_offset + 1u]);
    let line_c = bitcast<f32>(info[info_offset + 2u]);
    return CmdLinGrad(index, extend_mode, line_x, line_y, line_c);
}

// Decodes a CMD_RAD_GRAD payload. The info stream supplies a 2x2 matrix plus
// translation mapping pixels into gradient space, the focal parameters, and a
// word packing the gradient kind (low three bits) with the flags above them.
fn read_rad_grad(cmd_ix: u32) -> CmdRadGrad {
    let index_mode = ptcl[cmd_ix + 1u];
    let index = index_mode >> 2u;
    let extend_mode = index_mode & 0x3u;
    let info_offset = ptcl[cmd_ix + 2u];
    let m0 = bitcast<f32>(info[info_offset]);
    let m1 = bitcast<f32>(info[info_offset + 1u]);
    let m2 = bitcast<f32>(info[info_offset + 2u]);
    let m3 = bitcast<f32>(info[info_offset + 3u]);
    let matrx = vec4(m0, m1, m2, m3);
    let xlat = vec2(bitcast<f32>(info[info_offset + 4u]), bitcast<f32>(info[info_offset + 5u]));
    let focal_x = bitcast<f32>(info[info_offset + 6u]);
    let radius = bitcast<f32>(info[info_offset + 7u]);
    let flags_kind = info[info_offset + 8u];
    let flags = flags_kind >> 3u;
    let kind = flags_kind & 0x7u;
    return CmdRadGrad(index, extend_mode, matrx, xlat, focal_x, radius, kind, flags);
}

// Decodes a CMD_ELLIPTIC_GRAD payload: ramp index and extend mode, plus a 2x2
// matrix and translation (from the info stream) mapping pixels into a space
// where distance from the origin is the gradient parameter.
fn read_elliptic_grad(cmd_ix: u32) -> CmdEllipticGrad {
    let index_mode = ptcl[cmd_ix + 1u];
    let index = index_mode >> 2u;
    let extend_mode = index_mode & 0x3u;
    let info_offset = ptcl[cmd_ix + 2u];
    let m0 = bitcast<f32>(info[info_offset]);
    let m1 = bitcast<f32>(info[info_offset + 1u]);
    let m2 = bitcast<f32>(info[info_offset + 2u]);
    let m3 = bitcast<f32>(info[info_offset + 3u]);
    let matrx = vec4(m0, m1, m2, m3);
    let xlat = vec2(bitcast<f32>(info[info_offset + 4u]), bitcast<f32>(info[info_offset + 5u]));
    return CmdEllipticGrad(index, extend_mode, matrx, xlat);
}

// Decodes a CMD_SWEEP_GRAD payload: ramp index and extend mode, matrix and
// translation from the info stream, and the t0/t1 angular range expressed in
// normalized turns.
fn read_sweep_grad(cmd_ix: u32) -> CmdSweepGrad {
    let index_mode = ptcl[cmd_ix + 1u];
    let index = index_mode >> 2u;
    let extend_mode = index_mode & 0x3u;
    let info_offset = ptcl[cmd_ix + 2u];
    let m0 = bitcast<f32>(info[info_offset]);
    let m1 = bitcast<f32>(info[info_offset + 1u]);
    let m2 = bitcast<f32>(info[info_offset + 2u]);
    let m3 = bitcast<f32>(info[info_offset + 3u]);
    let matrx = vec4(m0, m1, m2, m3);
    let xlat = vec2(bitcast<f32>(info[info_offset + 4u]), bitcast<f32>(info[info_offset + 5u]));
    let t0 = bitcast<f32>(info[info_offset + 6u]);
    let t1 = bitcast<f32>(info[info_offset + 7u]);
    return CmdSweepGrad(index, extend_mode, matrx, xlat, t0, t1);
}

// Decodes a CMD_PATH_GRAD payload: info-stream offset of the gradient data,
// edge count, gradient flags, and draw flags.
fn read_path_grad(cmd_ix: u32) -> CmdPathGrad {
    let data_offset = ptcl[cmd_ix + 1u];
    let edge_count = ptcl[cmd_ix + 2u];
    let flags = ptcl[cmd_ix + 3u];
    let draw_flags = ptcl[cmd_ix + 4u];
    return CmdPathGrad(data_offset, edge_count, flags, draw_flags);
}

// Decodes a CMD_IMAGE payload from the info stream: inverse transform (matrix
// plus translation), atlas placement and extents, and a packed word carrying
// alpha, pixel format, alpha type, and per-axis extend modes.
fn read_image(cmd_ix: u32) -> CmdImage {
    let info_offset = ptcl[cmd_ix + 1u];
    let m0 = bitcast<f32>(info[info_offset]);
    let m1 = bitcast<f32>(info[info_offset + 1u]);
    let m2 = bitcast<f32>(info[info_offset + 2u]);
    let m3 = bitcast<f32>(info[info_offset + 3u]);
    let matrx = vec4(m0, m1, m2, m3);
    let xlat = vec2(bitcast<f32>(info[info_offset + 4u]), bitcast<f32>(info[info_offset + 5u]));
    let xy = info[info_offset + 6u];
    let width_height = info[info_offset + 7u];
    let sample_alpha = info[info_offset + 8u];
    let alpha = f32(sample_alpha & 0xFFu) / 255.0;
    let format = sample_alpha >> 15u;
    let alpha_type = (sample_alpha >> 14u) & 0x1u;
    let x_extend = (sample_alpha >> 10u) & 0x3u;
    let y_extend = (sample_alpha >> 8u) & 0x3u;
    // xy and width_height each pack two u16 values; these are numeric
    // conversions, not bitcasts.
    let x = f32(xy >> 16u);
    let y = f32(xy & 0xffffu);
    let width = f32(width_height >> 16u);
    let height = f32(width_height & 0xffffu);
    return CmdImage(matrx, xlat, vec2(x, y), vec2(width, height), format, x_extend, y_extend, alpha, alpha_type);
}

// Decodes a CMD_END_CLIP payload: the packed blend mode (which may carry the
// clip difference/hard mask bits) and the layer alpha, which doubles as the
// coverage threshold for hard mask clips.
fn read_end_clip(cmd_ix: u32) -> CmdEndClip {
    let blend = ptcl[cmd_ix + 1u];
    let alpha = bitcast<f32>(ptcl[cmd_ix + 2u]);
    return CmdEndClip(blend, alpha);
}

// Extracts the packed blend mode ((mix << 8) | compose) from a draw-flags word.
fn read_draw_blend_mode(draw_flags: u32) -> u32 {
    return (draw_flags & DRAW_FLAGS_BLEND_MODE_MASK) >> DRAW_FLAGS_BLEND_MODE_SHIFT;
}

// Extracts the mix (blending) mode from a draw-flags word.
fn read_draw_mix_mode(draw_flags: u32) -> u32 {
    return read_draw_blend_mode(draw_flags) >> 8u;
}

// Extracts the Porter-Duff compose mode from a draw-flags word.
fn read_draw_compose_mode(draw_flags: u32) -> u32 {
    return read_draw_blend_mode(draw_flags) & 0xffu;
}

// Extracts the layer alpha from a draw-flags word, stored as a 16-bit
// normalized value.
fn read_draw_blend_alpha(draw_flags: u32) -> f32 {
    let packed = (draw_flags & DRAW_FLAGS_BLEND_ALPHA_MASK) >> DRAW_FLAGS_BLEND_ALPHA_SHIFT;
    return f32(packed) / 65535.0;
}

// True when the flags encode plain source-over at full layer alpha, which
// allows compose_draw to take the fast path.
fn is_default_draw_blend(draw_flags: u32) -> bool {
    return read_draw_blend_mode(draw_flags) == ((MIX_NORMAL << 8u) | COMPOSE_SRC_OVER)
        && (draw_flags & DRAW_FLAGS_BLEND_ALPHA_MASK) == DRAW_FLAGS_BLEND_ALPHA_MASK;
}

// Composites a premultiplied source over a premultiplied backdrop using the
// mix/compose modes and layer alpha packed in draw_flags. Plain source-over at
// full alpha takes a fast path. The common Porter-Duff compose operators are
// expanded inline with the mix result weighted by the shared-alpha region;
// anything else falls back to blend_mix_compose.
fn compose_draw(backdrop: vec4<f32>, source: vec4<f32>, draw_flags: u32) -> vec4<f32> {
    let effective_alpha = source.a * read_draw_blend_alpha(draw_flags);

    if is_default_draw_blend(draw_flags) {
        return backdrop * (1.0 - source.a) + source;
    }

    let cb = unpremultiply(backdrop);
    let cs = unpremultiply(source);
    let ab = backdrop.a;
    let as_ = effective_alpha;
    let mix_mode = read_draw_mix_mode(draw_flags);
    let compose_mode = read_draw_compose_mode(draw_flags);
    let shared_alpha = as_ * ab;

    switch compose_mode {
        case COMPOSE_CLEAR: {
            return vec4(0.0);
        }
        case COMPOSE_COPY: {
            return vec4(cs * as_, as_);
        }
        case COMPOSE_DEST: {
            return backdrop;
        }
        case COMPOSE_SRC_OVER: {
            let blend = blend_mix(cb, cs, mix_mode);
            let dst_weight = ab - shared_alpha;
            let src_weight = as_ - shared_alpha;
            let alpha = dst_weight + as_;
            let premul = (cb * dst_weight) + (cs * src_weight) + (blend * shared_alpha);
            return vec4(premul, alpha);
        }
        case COMPOSE_DEST_OVER: {
            let blend = blend_mix(cs, cb, mix_mode);
            let dst_weight = as_ - shared_alpha;
            let src_weight = ab - shared_alpha;
            let alpha = dst_weight + ab;
            let premul = (cs * dst_weight) + (cb * src_weight) + (blend * shared_alpha);
            return vec4(premul, alpha);
        }
        case COMPOSE_SRC_IN: {
            return vec4(cs * shared_alpha, shared_alpha);
        }
        case COMPOSE_DEST_IN: {
            return vec4(cb * shared_alpha, shared_alpha);
        }
        case COMPOSE_SRC_OUT: {
            let alpha = as_ * (1.0 - ab);
            return vec4(cs * alpha, alpha);
        }
        case COMPOSE_DEST_OUT: {
            let alpha = ab * (1.0 - as_);
            return vec4(cb * alpha, alpha);
        }
        case COMPOSE_SRC_ATOP: {
            let blend = blend_mix(cb, cs, mix_mode);
            let dst_weight = ab - shared_alpha;
            let premul = (cb * dst_weight) + (blend * shared_alpha);
            return vec4(premul, ab);
        }
        case COMPOSE_DEST_ATOP: {
            let blend = blend_mix(cs, cb, mix_mode);
            let dst_weight = as_ - shared_alpha;
            let premul = (cs * dst_weight) + (blend * shared_alpha);
            return vec4(premul, as_);
        }
        case COMPOSE_XOR: {
            let src_weight = as_ * (1.0 - ab);
            let dst_weight = ab * (1.0 - as_);
            return vec4((cs * src_weight) + (cb * dst_weight), src_weight + dst_weight);
        }
        default: {
            return blend_mix_compose(backdrop, source * read_draw_blend_alpha(draw_flags), read_draw_blend_mode(draw_flags));
        }
    }
}

// Applies compose_draw, then lerps between the backdrop and the composed
// result by coverage so partial coverage attenuates the whole composite
// (including destructive compose modes), not just the source alpha.
fn compose_draw_with_coverage(backdrop: vec4<f32>, source: vec4<f32>, coverage: f32, draw_flags: u32) -> vec4<f32> {
    let composed = compose_draw(backdrop, source, draw_flags);
    return backdrop + ((composed - backdrop) * coverage);
}

const PIXEL_FORMAT_RGBA: u32 = 0u;
const PIXEL_FORMAT_BGRA: u32 = 1u;
// Normalises the channel order of a pixel loaded from an image, based on the
// image's declared format.
fn pixel_format(pixel: vec4f, format: u32) -> vec4f {
    switch format {
        case PIXEL_FORMAT_BGRA: {
            // The conversion from RGBA to BGRA is its own inverse.
            return pixel.bgra;
        }
        case PIXEL_FORMAT_RGBA, default: {
            return pixel;
        }
    }
}

const ALPHA: u32 = 0u;
const PREMULTIPLIED_ALPHA: u32 = 1u;
// Premultiplies the pixel's alpha unless the image already stores
// premultiplied values.
fn maybe_premul_alpha(pixel: vec4f, alpha_type: u32) -> vec4f {
    switch alpha_type {
        case PREMULTIPLIED_ALPHA: {
            return pixel;
        }
        case ALPHA, default: {
            return premul_alpha(pixel);
        }
    }
}

// Extend (wrap) modes for gradient parameters and image sampling.
const EXTEND_PAD: u32 = 0u;
const EXTEND_REPEAT: u32 = 1u;
const EXTEND_REFLECT: u32 = 2u;
const EXTEND_DECAL: u32 = 3u;
// Maps a gradient parameter t into [0, 1] according to the extend mode.
// Negative-t handling deliberately matches the CPU gradient brushes; see the
// per-case notes.
fn extend_mode_normalized(t: f32, mode: u32) -> f32 {
    switch mode {
        case EXTEND_PAD: {
            return clamp(t, 0.0, 1.0);
        }
        case EXTEND_REPEAT: {
            // The CPU gradient brushes do not wrap values before the first stop.
            // They hold the first stop for t < 0 and only repeat for t >= 0.
            return select(fract(t), 0.0, t < 0.0);
        }
        case EXTEND_REFLECT, default: {
            // Likewise, CPU reflection clamps negative values to the first stop
            // and only reflects once the parameter moves forward beyond 0.
            let clamped = max(t, 0.0);
            return abs(clamped - 2.0 * round(0.5 * clamped));
        }
    }
}

// Wraps an integer sample coordinate into [0, max) for repeat tiling.
fn image_repeat_mode_i32(t: f32, max: i32) -> i32 {
    let value = i32(t);
    let magnitude = select(value, -value, value < 0);
    let remainder = magnitude % max;
    let signed_remainder = select(remainder, -remainder, value < 0);

    // Match the CPU ImageBrush index calculation: ((value % max) + max) % max.
    return (signed_remainder + max) % max;
}

// Reflects an integer sample coordinate into [0, max), mirroring every other
// tile.
fn image_reflect_mode_i32(t: f32, max: i32) -> i32 {
    // Reflect in pixel space with period 2*max, mirroring once per tile.
    // Matches the CPU ImageBrush: wrap into [0, 2*max) then fold the back half.
    let period = max * 2;
    let value = i32(t);
    let magnitude = select(value, -value, value < 0);
    let remainder = magnitude % period;
    let signed_remainder = select(remainder, -remainder, value < 0);
    var m = (signed_remainder + period) % period;
    if (m >= max) {
        m = period - 1 - m;
    }
    return m;
}

// Converts a continuous sample coordinate to a texel index under the given
// wrap mode. Decal returns -1 for samples outside the source region.
fn image_extend_mode_i32(t: f32, mode: u32, max: i32) -> i32 {
    switch mode {
        case EXTEND_PAD: {
            return clamp(i32(t), 0, max - 1);
        }
        case EXTEND_REPEAT: {
            return image_repeat_mode_i32(t, max);
        }
        case EXTEND_DECAL: {
            // Outside the source region samples as transparent; signalled with a negative index.
            if (t < 0.0 || t >= f32(max)) {
                return -1;
            }
            return i32(t);
        }
        case EXTEND_REFLECT, default: {
            return image_reflect_mode_i32(t, max);
        }
    }
}

// Flag bit indicating the gradient carries an explicit center color, which
// disables the single-triangle barycentric shortcut.
const PATH_GRAD_HAS_EXPLICIT_CENTER_COLOR = 1u;
// Info-stream layout: a 4-word header (center point, max ray distance, packed
// center color) followed by 6-word edge records (start point, end point,
// start color, end color).
const PATH_GRAD_HEADER_WORD_COUNT = 4u;
const PATH_GRAD_EDGE_WORD_COUNT = 6u;

// Returns the info-stream offset of edge record edge_ix, past the gradient's
// 4-word header.
fn path_grad_edge_offset(path_grad: CmdPathGrad, edge_ix: u32) -> u32 {
    return path_grad.data_offset + PATH_GRAD_HEADER_WORD_COUNT + edge_ix * PATH_GRAD_EDGE_WORD_COUNT;
}

// Loads a point stored as two f32 words in the info stream.
fn path_grad_load_point(offset: u32) -> vec2<f32> {
    return vec2<f32>(bitcast<f32>(info[offset]), bitcast<f32>(info[offset + 1u]));
}

// Loads a color stored as one packed rgba8 word in the info stream.
fn path_grad_load_color(offset: u32) -> vec4<f32> {
    return unpack4x8unorm(info[offset]);
}

// 2D cross product (the z component of the 3D cross product).
fn cross2(a: vec2<f32>, b: vec2<f32>) -> f32 {
    return a.x * b.y - a.y * b.x;
}

// Intersects the segment p + t*r (t in [0, 1]) with the segment q + u*s
// (u in [0, 1]). Returns (1, x, y, u) on a hit, where (x, y) is the
// intersection point and u the parameter along the second segment; returns
// all zeros when the segments are near-parallel or the intersection falls
// outside either parameter range.
fn path_grad_line_intersection(p: vec2<f32>, r: vec2<f32>, q: vec2<f32>, s: vec2<f32>) -> vec4<f32> {
    let denominator = cross2(r, s);
    if abs(denominator) <= 1.0e-6 {
        return vec4<f32>(0.0);
    }

    let qp = q - p;
    let t = cross2(qp, s) / denominator;
    let u = cross2(qp, r) / denominator;
    if t < 0.0 || t > 1.0 || u < 0.0 || u > 1.0 {
        return vec4<f32>(0.0);
    }

    let point = p + t * r;
    return vec4<f32>(1.0, point.x, point.y, u);
}

// Point-in-triangle test with barycentric output. Returns (1, u, v) when the
// point lies inside triangle v1-v2-v3, where u and v weight v2 and v3 and
// (1 - u - v) weights v1; returns all zeros when the point is outside or the
// triangle is degenerate.
fn path_grad_point_on_triangle(v1: vec2<f32>, v2: vec2<f32>, v3: vec2<f32>, point: vec2<f32>) -> vec3<f32> {
    let e1 = v2 - v1;
    let e2 = v3 - v2;
    let e3 = v1 - v3;
    let pv1 = point - v1;
    let pv2 = point - v2;
    let pv3 = point - v3;
    let d1 = cross2(e1, pv1);
    let d2 = cross2(e2, pv2);
    let d3 = cross2(e3, pv3);
    let has_negative = d1 < 0.0 || d2 < 0.0 || d3 < 0.0;
    let has_positive = d1 > 0.0 || d2 > 0.0 || d3 > 0.0;
    if has_negative && has_positive {
        return vec3<f32>(0.0);
    }

    let d00 = dot(e1, e1);
    let d01 = -dot(e1, e3);
    let d11 = dot(e3, e3);
    let d20 = dot(pv1, e1);
    let d21 = -dot(pv1, e3);
    let denominator = (d00 * d11) - (d01 * d01);
    if abs(denominator) <= 1.0e-6 {
        return vec3<f32>(0.0);
    }

    let u = ((d11 * d20) - (d01 * d21)) / denominator;
    let v = ((d00 * d21) - (d01 * d20)) / denominator;
    return vec3<f32>(1.0, u, v);
}

// Evaluates the path gradient brush at a point, returning a premultiplied
// color. A three-edge gradient without an explicit center color interpolates
// its vertex colors barycentrically. Otherwise a ray is cast from the point
// away from the gradient center to find the nearest edge intersection; the
// interpolated edge color is then mixed toward the center color by the ratio
// of the point's distance from the edge to the center's distance from the
// edge. Returns transparent when the point is not covered by any edge.
fn evaluate_path_gradient(path_grad: CmdPathGrad, point: vec2<f32>) -> vec4<f32> {
    let center = path_grad_load_point(path_grad.data_offset);
    let center_color = path_grad_load_color(path_grad.data_offset + 3u);

    if all(point == center) {
        return premul_alpha(center_color);
    }

    if path_grad.edge_count == 3u && (path_grad.flags & PATH_GRAD_HAS_EXPLICIT_CENTER_COLOR) == 0u {
        let edge0 = path_grad_edge_offset(path_grad, 0u);
        let edge1 = path_grad_edge_offset(path_grad, 1u);
        let edge2 = path_grad_edge_offset(path_grad, 2u);
        let v1 = path_grad_load_point(edge0);
        let v2 = path_grad_load_point(edge1);
        let v3 = path_grad_load_point(edge2);
        let triangle = path_grad_point_on_triangle(v1, v2, v3, point);
        if triangle.x == 0.0 {
            return vec4<f32>(0.0);
        }

        let c0 = path_grad_load_color(edge0 + 4u);
        let c1 = path_grad_load_color(edge0 + 5u);
        let c2 = path_grad_load_color(edge2 + 4u);
        return premul_alpha(((1.0 - triangle.y - triangle.z) * c0) + (triangle.y * c1) + (triangle.z * c2));
    }

    let delta = point - center;
    let delta_length_squared = dot(delta, delta);
    if delta_length_squared == 0.0 {
        return premul_alpha(center_color);
    }

    let max_distance = bitcast<f32>(info[path_grad.data_offset + 2u]);
    let direction = delta * inverseSqrt(delta_length_squared);
    let ray = direction * max_distance;
    var closest_distance = 3.4028234663852886e38;
    var closest_point = vec2<f32>(0.0);
    var closest_color = vec4<f32>(0.0);
    var found = false;

    for (var edge_ix = 0u; edge_ix < path_grad.edge_count; edge_ix += 1u) {
        let edge = path_grad_edge_offset(path_grad, edge_ix);
        let start = path_grad_load_point(edge);
        let end = path_grad_load_point(edge + 2u);
        let segment = end - start;
        let intersection = path_grad_line_intersection(point, ray, start, segment);
        if intersection.x != 0.0 {
            let intersection_point = intersection.yz;
            let distance_squared = dot(intersection_point - point, intersection_point - point);
            if distance_squared < closest_distance {
                closest_distance = distance_squared;
                closest_point = intersection_point;
                closest_color = mix(path_grad_load_color(edge + 4u), path_grad_load_color(edge + 5u), intersection.w);
                found = true;
            }
        }
    }

    if !found {
        return vec4<f32>(0.0);
    }

    let center_distance = distance(closest_point, center);
    let ratio = select(0.0, distance(closest_point, point) / center_distance, center_distance > 0.0);
    return premul_alpha(mix(closest_color, center_color, ratio));
}

// Number of horizontally adjacent pixels shaded by each thread.
const PIXELS_PER_THREAD = 4u;

// Computes per-pixel coverage for a CMD_FILL using analytic area
// anti-aliasing. Signed winding is accumulated from the backdrop and every
// segment in the tile, the fill rule is applied, aliased fills are quantized
// against their per-fill threshold, and coverage outside the fill's raster
// interest rectangle is zeroed. xy is the thread's first pixel in tile-local
// space (segments are tile-relative); global_xy is the same pixel in
// full-target space. result receives coverage for PIXELS_PER_THREAD adjacent
// pixels.
//
// FIXME: This should return an array when https://github.com/gfx-rs/naga/issues/1930 is fixed.
fn fill_path(fill: CmdFill, xy: vec2<f32>, global_xy: vec2<f32>, result: ptr<function, array<f32, PIXELS_PER_THREAD>>) {
    // size_and_rule: bit 0 = even-odd, bit 1 = aliased coverage, bits 2.. = segment count.
    let n_segs = fill.size_and_rule >> 2u;
    let even_odd = (fill.size_and_rule & 1u) != 0u;
    let aliased = (fill.size_and_rule & 2u) != 0u;
    var area: array<f32, PIXELS_PER_THREAD>;
    let backdrop_f = f32(fill.backdrop);
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        area[i] = backdrop_f;
    }
    for (var i = 0u; i < n_segs; i++) {
        let seg_off = fill.seg_data + i;
        let segment = segments[seg_off];
        let y = segment.point0.y - xy.y;
        let delta = segment.point1 - segment.point0;
        let y0 = clamp(y, 0.0, 1.0);
        let y1 = clamp(y + delta.y, 0.0, 1.0);
        let dy = y0 - y1;
        // dy is the segment's signed vertical extent clipped to this pixel
        // row; zero means the segment does not cross the row.
        if dy != 0.0 {
            let vec_y_recip = 1.0 / delta.y;
            let t0 = (y0 - y) * vec_y_recip;
            let t1 = (y1 - y) * vec_y_recip;
            let startx = segment.point0.x - xy.x;
            let x0 = startx + t0 * delta.x;
            let x1 = startx + t1 * delta.x;
            let xmin0 = min(x0, x1);
            let xmax0 = max(x0, x1);
            for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                let i_f = f32(i);
                let xmin = min(xmin0 - i_f, 1.0) - 1.0e-6;
                let xmax = xmax0 - i_f;
                let b = min(xmax, 1.0);
                let c = max(b, 0.0);
                let d = max(xmin, 0.0);
                let a = (b + 0.5 * (d * d - c * c) - xmin) / (xmax - xmin);
                area[i] += a * dy;
            }
        }
        // Winding contribution from segments crossing the tile's left edge.
        let y_edge = sign(delta.x) * clamp(xy.y - segment.y_edge + 1.0, 0.0, 1.0);
        for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
            area[i] += y_edge;
        }
    }
    if even_odd {
        // even-odd winding rule
        for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
            let a = area[i];
            area[i] = abs(a - 2.0 * round(0.5 * a));
        }
    } else {
        // non-zero winding rule
        for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
            area[i] = min(abs(area[i]), 1.0);
        }
    }
    if aliased {
        // Aliased fills quantize analytic coverage against this fill's own coverage threshold,
        // matching the CPU rasterizer's per-fill RasterizationMode.Aliased + AntialiasThreshold.
        let threshold = fill.coverage_threshold;
        for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
            area[i] = select(0.0, 1.0, area[i] >= threshold);
        }
    }
    // Discard coverage outside the fill's raster interest rectangle
    // (expressed in full-target coordinates).
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        let pixel = global_xy + vec2<f32>(f32(i), 0.0);
        if pixel.x < fill.interest.x || pixel.y < fill.interest.y || pixel.x >= fill.interest.z || pixel.y >= fill.interest.w {
            area[i] = 0.0;
        }
    }

    *result = area;
}

// Entry point: one workgroup per tile, each thread shading PIXELS_PER_THREAD
// horizontally adjacent pixels. Seeds pixel state from the backdrop texture,
// interprets the tile's PTCL commands until CMD_END, and writes the result to
// the output texture with straight alpha.
//
// The X workgroup size should be TILE_WIDTH / PIXELS_PER_THREAD.
@compute @workgroup_size(4, 16)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
    @builtin(workgroup_id) wg_id: vec3<u32>,
) {
    if ptcl[0] == ~0u {
        // An earlier stage has failed, don't try to render.
        // We use ptcl[0] for this so we don't use up a binding for bump.
        return;
    }
    let tile_ix = wg_id.y * config.width_in_tiles + wg_id.x;
    // Full-target pixel position; y is offset by the chunk's starting tile row
    // so oversized scenes render correctly in tile-row windows.
    let xy = vec2(f32(global_id.x * PIXELS_PER_THREAD), f32(config.chunk_tile_y_start * TILE_HEIGHT + global_id.y));
    let xy_uint = vec2<u32>(xy);
    let local_xy = vec2(f32(local_id.x * PIXELS_PER_THREAD), f32(local_id.y));
    var rgba: array<vec4<f32>, PIXELS_PER_THREAD>;
    // Seed each pixel from the existing target contents, converting the
    // straight-alpha backdrop to the premultiplied form used internally.
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        let coords = vec2<i32>(xy_uint + vec2(i, 0u));
        let backdrop_raw = textureLoad(backdrop_texture, coords, 0);
        rgba[i] = vec4(backdrop_raw.rgb * backdrop_raw.a, backdrop_raw.a);
    }
    var blend_stack: array<array<u32, PIXELS_PER_THREAD>, BLEND_STACK_SPLIT>;
    var clip_depth = 0u;
    var area: array<f32, PIXELS_PER_THREAD>;
    var cmd_ix = tile_ix * PTCL_INITIAL_ALLOC;
    // The first word of each tile's PTCL slot is its blend spill offset.
    let blend_offset = ptcl[cmd_ix];
    cmd_ix += 1u;
    // main interpretation loop
    while true {
        let tag = ptcl[cmd_ix];
        if tag == CMD_END {
            break;
        }
        switch tag {
            case CMD_FILL: {
                let fill = read_fill(cmd_ix);
                fill_path(fill, local_xy, xy, &area);
                cmd_ix += 9u;
            }
            case CMD_SOLID: {
                // Full coverage, restricted to the command's raster interest
                // rectangle.
                let interest = vec4<f32>(
                    bitcast<f32>(ptcl[cmd_ix + 1u]),
                    bitcast<f32>(ptcl[cmd_ix + 2u]),
                    bitcast<f32>(ptcl[cmd_ix + 3u]),
                    bitcast<f32>(ptcl[cmd_ix + 4u]));
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    let pixel = xy + vec2<f32>(f32(i), 0.0);
                    area[i] = select(0.0, 1.0, pixel.x >= interest.x && pixel.y >= interest.y && pixel.x < interest.z && pixel.y < interest.w);
                }
                cmd_ix += 5u;
            }
            case CMD_COLOR: {
                let color = read_color(cmd_ix);
                let fg = unpack4x8unorm(color.rgba_color);
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        rgba[i] = compose_draw_with_coverage(rgba[i], fg, area[i], color.draw_flags);
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_RECOLOR: {
                let recolor = read_recolor(cmd_ix);
                let source = unpack4x8unorm(recolor.source_color);
                let target_color = unpack4x8unorm(recolor.target_color);
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        let bg = rgba[i];
                        // Compare in straight-alpha space against the source
                        // key color, using squared RGBA distance.
                        let bg_sep = vec4(bg.rgb / max(bg.a, 1e-6), bg.a);
                        let delta = bg_sep - source;
                        let distance = dot(delta, delta);
                        if distance <= recolor.threshold {
                            // Blend strength ramps from 1 at an exact match to
                            // 0 at the threshold boundary.
                            let t = (recolor.threshold - distance) / recolor.threshold;
                            let target_premul = premul_alpha(target_color);
                            let recolored = target_premul * t + bg * (1.0 - target_color.a * t);
                            rgba[i] = compose_draw_with_coverage(bg, recolored, area[i], recolor.draw_flags);
                        }
                    }
                }
                cmd_ix += 5u;
            }
            case CMD_BEGIN_CLIP: {
                // Save the current layer color and start a fresh transparent
                // layer. Shallow stack entries live in registers; deeper ones
                // spill to the blend buffer.
                if clip_depth < BLEND_STACK_SPLIT {
                    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        blend_stack[clip_depth][i] = pack4x8unorm(rgba[i]);
                        rgba[i] = vec4(0.0);
                    }
                } else {
                    let blend_in_scratch = clip_depth - BLEND_STACK_SPLIT;
                    let local_tile_ix = local_id.x * PIXELS_PER_THREAD + local_id.y * TILE_WIDTH;
                    let local_blend_start = blend_offset + blend_in_scratch * TILE_WIDTH * TILE_HEIGHT + local_tile_ix;
                    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        blend_spill[local_blend_start + i] = pack4x8unorm(rgba[i]);
                        rgba[i] = vec4(0.0);
                    }
                }
                clip_depth += 1u;
                cmd_ix += 1u;
            }
            case CMD_END_CLIP: {
                let end_clip = read_end_clip(cmd_ix);
                clip_depth -= 1u;
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    var bg_rgba: u32;
                    if clip_depth < BLEND_STACK_SPLIT {
                        bg_rgba = blend_stack[clip_depth][i];
                    } else {
                        let blend_in_scratch = clip_depth - BLEND_STACK_SPLIT;
                        let local_tile_ix = local_id.x * PIXELS_PER_THREAD + local_id.y * TILE_WIDTH;
                        let local_blend_start = blend_offset + blend_in_scratch * TILE_WIDTH * TILE_HEIGHT + local_tile_ix;
                        bg_rgba = blend_spill[local_blend_start + i];
                    }
                    // Difference clips reuse the same clip path but invert the mask at the
                    // point where the saved backdrop is restored. This keeps clip operation
                    // semantics in the clip stack instead of fabricating inverse path geometry.
                    // Hard mask clips binarize coverage against the stored alpha
                    // threshold instead of scaling the layer by it.
                    let is_hard_clip = (end_clip.blend & CLIP_HARD_MASK_BIT) != 0u;
                    var source_clip_area = area[i];
                    if is_hard_clip {
                        source_clip_area = select(0.0, 1.0, source_clip_area > end_clip.alpha);
                    }

                    // Strip the local mask bits so only the packed blend mode remains.
                    let clip_area = select(source_clip_area, 1.0 - source_clip_area, (end_clip.blend & CLIP_DIFFERENCE_MASK_BIT) != 0u);
                    var clip_blend = end_clip.blend & ~CLIP_DIFFERENCE_MASK_BIT;
                    if is_hard_clip {
                        clip_blend &= ~CLIP_HARD_MASK_BIT;
                    }

                    let bg = unpack4x8unorm(bg_rgba);
                    let clip_alpha = select(end_clip.alpha, 1.0, is_hard_clip);
                    let fg = rgba[i] * clip_alpha;

                    if clip_blend == LUMINANCE_MASK_LAYER {
                        // TODO: Does this case apply more generally?
                        // See https://github.com/linebender/vello/issues/1061
                        // TODO: How do we handle anti-aliased edges here?
                        // This is really an imaging model question
                        if clip_area == 0.0 {
                            rgba[i] = bg;
                            continue;
                        }

                        let luminance = clamp(svg_lum(unpremultiply(fg)) * fg.a, 0.0, 1.0);
                        let composed = bg * luminance;
                        rgba[i] = bg + ((composed - bg) * clip_area);
                    } else {
                        let composed = blend_mix_compose(bg, fg, clip_blend);
                        rgba[i] = bg + ((composed - bg) * clip_area);
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_JUMP: {
                // Continue interpretation in the tile's next PTCL block.
                cmd_ix = ptcl[cmd_ix + 1u];
            }
            case CMD_LIN_GRAD: {
                let lin = read_lin_grad(cmd_ix);
                // The draw-flags word sits immediately before the brush data
                // in the info stream (same layout for all gradient commands).
                let draw_flags = info[ptcl[cmd_ix + 2u] - 1u];
                let d = lin.line_x * (xy.x + 0.5) + lin.line_y * (xy.y + 0.5) + lin.line_c;
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        let my_d = d + lin.line_x * f32(i);
                        let x = i32(round(extend_mode_normalized(my_d, lin.extend_mode) * f32(GRADIENT_WIDTH - 1)));
                        let fg_rgba = textureLoad(gradients, vec2(x, i32(lin.index)), 0);
                        rgba[i] = compose_draw_with_coverage(rgba[i], fg_rgba, area[i], draw_flags);
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_RAD_GRAD: {
                let rad = read_rad_grad(cmd_ix);
                let draw_flags = info[ptcl[cmd_ix + 2u] - 1u];
                // Two-point conical gradient. The kind, focal parameters, and
                // flags are precomputed by draw_leaf; the parameter t is
                // evaluated per kind below.
                let focal_x = rad.focal_x;
                let radius = rad.radius;
                let is_strip = rad.kind == RAD_GRAD_KIND_STRIP;
                let is_circular = rad.kind == RAD_GRAD_KIND_CIRCULAR;
                let is_focal_on_circle = rad.kind == RAD_GRAD_KIND_FOCAL_ON_CIRCLE;
                let is_swapped = (rad.flags & RAD_GRAD_SWAPPED) != 0u;
                let r1_recip = select(1.0 / radius, 0.0, is_circular);
                let less_scale = select(1.0, -1.0, is_swapped || (1.0 - focal_x) < 0.0);
                let t_sign = sign(1.0 - focal_x);
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    let my_xy = vec2(xy.x + f32(i) + 0.5, xy.y + 0.5);
                    let local_xy = rad.matrx.xy * my_xy.x + rad.matrx.zw * my_xy.y + rad.xlat;
                    let x = local_xy.x;
                    let y = local_xy.y;
                    let xx = x * x;
                    let yy = y * y;
                    var t = 0.0;
                    var is_valid = true;
                    if is_strip {
                        let a = radius - yy;
                        t = sqrt(a) + x;
                        is_valid = a >= 0.0;
                    } else if is_focal_on_circle {
                        t = (xx + yy) / x;
                        is_valid = t >= 0.0 && x != 0.0;
                    } else if radius > 1.0 {
                        t = sqrt(xx + yy) - x * r1_recip;
                    } else { // radius < 1.0
                        let a = xx - yy;
                        t = less_scale * sqrt(a) - x * r1_recip;
                        is_valid = a >= 0.0 && t >= 0.0;
                    }
                    if is_valid {
                        t = extend_mode_normalized(focal_x + t_sign * t, rad.extend_mode);
                        t = select(t, 1.0 - t, is_swapped);
                        let x = i32(round(t * f32(GRADIENT_WIDTH - 1)));
                        let fg_rgba = textureLoad(gradients, vec2(x, i32(rad.index)), 0);
                        rgba[i] = compose_draw_with_coverage(rgba[i], fg_rgba, area[i], draw_flags);
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_ELLIPTIC_GRAD: {
                let elliptic = read_elliptic_grad(cmd_ix);
                let draw_flags = info[ptcl[cmd_ix + 2u] - 1u];
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        let my_xy = vec2(xy.x + f32(i) + 0.5, xy.y + 0.5);
                        let local_xy = elliptic.matrx.xy * my_xy.x + elliptic.matrx.zw * my_xy.y + elliptic.xlat;
                        let radius = length(local_xy);
                        // radius == radius rejects NaN from degenerate transforms.
                        if radius == radius {
                            let t = extend_mode_normalized(radius, elliptic.extend_mode);
                            let ramp_x = i32(round(t * f32(GRADIENT_WIDTH - 1)));
                            let fg_rgba = textureLoad(gradients, vec2(ramp_x, i32(elliptic.index)), 0);
                            rgba[i] = compose_draw_with_coverage(rgba[i], fg_rgba, area[i], draw_flags);
                        }
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_SWEEP_GRAD: {
                let sweep = read_sweep_grad(cmd_ix);
                let draw_flags = info[ptcl[cmd_ix + 2u] - 1u];
                let scale = 1.0 / (sweep.t1 - sweep.t0);
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        let my_xy = vec2(xy.x + f32(i) + 0.5, xy.y + 0.5);
                        let local_xy = sweep.matrx.xy * my_xy.x + sweep.matrx.zw * my_xy.y + sweep.xlat;
                        let x = local_xy.x;
                        let y = local_xy.y;
                        // xy_to_unit_angle from Skia:
                        // See <https://github.com/google/skia/blob/30bba741989865c157c7a997a0caebe94921276b/src/opts/SkRasterPipeline_opts.h#L5859>
                        let xabs = abs(x);
                        let yabs = abs(y);
                        let slope = min(xabs, yabs) / max(xabs, yabs);
                        let s = slope * slope;
                        // again, from Skia:
                        // Use a 7th degree polynomial to approximate atan.
                        // This was generated using sollya.gforge.inria.fr.
                        // A float optimized polynomial was generated using the following command.
                        // P1 = fpminimax((1/(2*Pi))*atan(x),[|1,3,5,7|],[|24...|],[2^(-40),1],relative);
                        var phi = slope * (0.15912117063999176025390625f + s * (-5.185396969318389892578125e-2f + s * (2.476101927459239959716796875e-2f + s * (-7.0547382347285747528076171875e-3f))));
                        phi = select(phi, 1.0 / 4.0 - phi, xabs < yabs);
                        phi = select(phi, 1.0 / 2.0 - phi, x < 0.0);
                        phi = select(phi, 1.0 - phi, y < 0.0);
                        phi = select(phi, 0.0, phi != phi); // check for NaN
                        phi = fract(1.0 - phi);
                        phi = (phi - sweep.t0) * scale;
                        let t = extend_mode_normalized(phi, sweep.extend_mode);
                        let ramp_x = i32(round(t * f32(GRADIENT_WIDTH - 1)));
                        let fg_rgba = textureLoad(gradients, vec2(ramp_x, i32(sweep.index)), 0);
                        rgba[i] = compose_draw_with_coverage(rgba[i], fg_rgba, area[i], draw_flags);
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_PATH_GRAD: {
                let path_grad = read_path_grad(cmd_ix);
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        let my_xy = vec2(xy.x + f32(i) + 0.5, xy.y + 0.5);
                        let fg_rgba = evaluate_path_gradient(path_grad, my_xy);
                        rgba[i] = compose_draw_with_coverage(rgba[i], fg_rgba, area[i], path_grad.draw_flags);
                    }
                }
                cmd_ix += 5u;
            }
            case CMD_IMAGE: {
                let image = read_image(cmd_ix);
                let draw_flags = info[ptcl[cmd_ix + 1u] - 1u];
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    // We only need to load from the textures if the value will be used.
                    if area[i] != 0.0 {
                        let my_xy = vec2(xy.x + f32(i), xy.y);
                        var atlas_uv = image.matrx.xy * my_xy.x + image.matrx.zw * my_xy.y + image.xlat;
                        let atlas_ix = image_extend_mode_i32(atlas_uv.x, image.x_extend_mode, i32(image.extents.x));
                        let atlas_iy = image_extend_mode_i32(atlas_uv.y, image.y_extend_mode, i32(image.extents.y));
                        // A negative index means the decal (None) wrap mode fell outside the source
                        // region; that pixel contributes nothing.
                        if (atlas_ix >= 0 && atlas_iy >= 0) {
                            let atlas_uv_clamped = vec2<i32>(i32(image.atlas_offset.x) + atlas_ix, i32(image.atlas_offset.y) + atlas_iy);
                            let fg_rgba = maybe_premul_alpha(textureLoad(image_atlas, atlas_uv_clamped, 0), image.alpha_type);
                            let fg_i = pixel_format(fg_rgba * image.alpha, image.format);
                            rgba[i] = compose_draw_with_coverage(rgba[i], fg_i, area[i], draw_flags);
                        }
                    }
                }
                cmd_ix += 2u;
            }
            default: {}
        }
    }
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        let coords = xy_uint + vec2(i, 0u);
        if coords.x < config.target_width && coords.y < config.target_height {
            let fg = rgba[i];
            // Convert to straight alpha for the output; the epsilon avoids
            // NaN when alpha is zero.
            let a_inv = 1.0 / max(fg.a, 1e-6);
            let rgba_sep = vec4(fg.rgb * a_inv, fg.a);
            textureStore(output, vec2<i32>(coords), rgba_sep);
        }
    }
}

// Converts a straight-alpha color to premultiplied form.
fn premul_alpha(rgba: vec4<f32>) -> vec4<f32> {
    return vec4(rgba.rgb * rgba.a, rgba.a);
}
