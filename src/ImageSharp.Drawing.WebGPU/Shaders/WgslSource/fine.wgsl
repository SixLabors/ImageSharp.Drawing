// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Fine rasterizer: the final stage of the pipeline. Each workgroup shades one
// 16x16 tile by interpreting the tile's command list (PTCL) written by
// coarse.wgsl. Antialiased fills use analytic area coverage. Aliased fills use
// exact row-centre and column-centre crossings. Brushes (solid color, recolor,
// linear/radial/elliptic/sweep/path gradients, images) are then evaluated and
// composited, including clip mask begin/end handling.
//
// Inputs: config uniform, segment buffer, ptcl and info streams, gradient ramp
// texture, image atlas, and a backdrop texture holding the existing target
// contents. Output format, numeric encoding, and alpha representation are
// specialized by FineAreaComputeShader. blend_spill provides scratch for clip
// stacks deeper than BLEND_STACK_SPLIT.
//
// Ported from Vello's fine.wgsl (vello_shaders/shader/fine.wgsl). Local
// divergences from upstream: centre-sampled aliased fills, extra PTCL commands
// (CMD_RECOLOR, CMD_ELLIPTIC_GRAD, CMD_PATH_GRAD), clip difference and isolation
// bits, per-command raster interest rectangles, backdrop texture
// seeding, tile-row chunking via config.chunk_tile_y_start, and gradient extend
// behavior deliberately matched to the CPU brushes.

#import segment
#import config
#import drawtag
#import tile

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
var<storage, read_write> blend_spill: array<vec2<u32>>;

// Final render target; its declaration and encoding are specialized by the host.
@group(0) @binding(5)
var output: texture_storage_2d<rgba8unorm, write>;

// Gradient ramp texture: one GRADIENT_WIDTH-texel row per gradient.
@group(0) @binding(6)
var gradients: texture_2d<f32>;

// Atlas holding image brush sources.
@group(0) @binding(7)
var image_atlas: texture_2d<f32>;

// Existing target contents used to seed each pixel in the target representation.
@group(0) @binding(8)
var backdrop_texture: texture_2d<f32>;

// The packed scene stream is bound here so aliased fills can read profile records. The table at
// profile_records_base starts with the X and Y record counts. X records follow, then Y records.
// Each record contains a minimum coordinate, a maximum coordinate, and a contour link.
@group(0) @binding(9)
var<storage> scene_data: array<u32>;

// Coarse rewrites each non-empty path tile's segment field to an inverted slice index and then
// preserves its original segment count in backdrop. Aliased row walks use adjacent slices for
// the exact half-pixel crossing halo.
@group(0) @binding(10)
var<storage> path_tiles: array<Tile>;

// Decodes a CMD_FILL payload: packed segment-count/fill-rule word, segment
// base index, backdrop winding, coverage data, and the raster interest
// rectangle. Antialiased text may use the coverage word for a perceptual boost;
// coarse repacks aliased fills with the path-tile index and neighbor flags used
// by the exact crossing halo. As for all read_* decoders, cmd_ix addresses the
// command tag and the payload words follow it.
fn read_fill(cmd_ix: u32) -> CmdFill {
    let size_and_rule = ptcl[cmd_ix + 1u];
    let seg_data = ptcl[cmd_ix + 2u];
    let backdrop = i32(ptcl[cmd_ix + 3u]);
    let coverage_data = ptcl[cmd_ix + 4u];
    let interest = vec4<f32>(
        bitcast<f32>(ptcl[cmd_ix + 5u]),
        bitcast<f32>(ptcl[cmd_ix + 6u]),
        bitcast<f32>(ptcl[cmd_ix + 7u]),
        bitcast<f32>(ptcl[cmd_ix + 8u]));
    return CmdFill(size_and_rule, seg_data, backdrop, coverage_data, interest);
}

// Expands one RGBA color stored as two binary16 pairs. Brush payloads use
// binary16 so RgbaHalf targets are not prematurely reduced to RGBA8.
fn unpack_color_f16(rg: u32, ba: u32) -> vec4<f32> {
    return vec4(unpack2x16float(rg), unpack2x16float(ba));
}

// Decodes a CMD_COLOR payload: binary16 associated color and draw flags.
fn read_color(cmd_ix: u32) -> CmdColor {
    let color_rg = ptcl[cmd_ix + 1u];
    let color_ba = ptcl[cmd_ix + 2u];
    let draw_flags = ptcl[cmd_ix + 3u];
    return CmdColor(color_rg, color_ba, draw_flags);
}

// Decodes a CMD_RECOLOR reference to its target-specialized auxiliary record.
fn read_recolor(cmd_ix: u32) -> CmdRecolor {
    return CmdRecolor(ptcl[cmd_ix + 1u], ptcl[cmd_ix + 2u]);
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
// matrix and translation (from the info stream) mapping pixels into gradient
// space, plus the kind used to evaluate zero-width ellipses without undefined
// matrix arithmetic.
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
    let kind = info[info_offset + 6u];
    return CmdEllipticGrad(index, extend_mode, matrx, xlat, kind);
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
    let format = (sample_alpha >> 15u) & 0x1u;
    let alpha_type = (sample_alpha >> 14u) & 0x1u;
    let signed_unit = (sample_alpha >> 16u) & 0x1u;
    let x_extend = (sample_alpha >> 10u) & 0x3u;
    let y_extend = (sample_alpha >> 8u) & 0x3u;
    // xy and width_height each pack two u16 values; these are numeric
    // conversions, not bitcasts.
    let x = f32(xy >> 16u);
    let y = f32(xy & 0xffffu);
    let width = f32(width_height >> 16u);
    let height = f32(width_height & 0xffffu);
    return CmdImage(matrx, xlat, vec2(x, y), vec2(width, height), format, x_extend, y_extend, alpha, alpha_type, signed_unit);
}

// Decodes a CMD_END_CLIP payload: the packed blend mode, including the local
// difference and isolation bits, and the layer alpha.
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

// Recolor's inner blend uses a distance-derived alpha rather than the layer alpha in
// draw_flags. Keep that distinct from compose_draw so ordinary draws retain their exact path.
fn compose_recolor_inner(backdrop: vec4<f32>, source: vec4<f32>, blend_alpha: f32, draw_flags: u32) -> vec4<f32> {
    let effective_alpha = source.a * blend_alpha;

    if read_draw_blend_mode(draw_flags) == ((MIX_NORMAL << 8u) | COMPOSE_SRC_OVER) {
        return backdrop * (1.0 - effective_alpha) + (source * blend_alpha);
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
            return blend_mix_compose(backdrop, source * blend_alpha, read_draw_blend_mode(draw_flags));
        }
    }
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

// CPU-backed atlases contain native TPixel values. NormalizedByte4 maps its signed
// native components into ImageSharp's unit color range.
fn decode_image_numeric(pixel: vec4f, signed_unit: u32) -> vec4f {
    return select(pixel, (pixel + vec4f(1.0)) * 0.5, signed_unit != 0u);
}

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
        case EXTEND_REFLECT: {
            // Likewise, CPU reflection clamps negative values to the first stop
            // and only reflects once the parameter moves forward beyond 0.
            let clamped = max(t, 0.0);
            return abs(clamped - 2.0 * round(0.5 * clamped));
        }
        case EXTEND_DECAL, default: {
            // DontFill contributes no color outside the finite gradient interval. A negative
            // sentinel lets every gradient evaluator skip its texture load without adding a
            // second mode-dependent range test to the normal pad/repeat/reflect paths.
            return select(t, -1.0, t < 0.0 || t > 1.0);
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
// Info-stream layout: a 7-word header (center point, max ray distance, and four
// center-color components) followed by 12-word edge records (start point, end
// point, and four components for each endpoint color).
const PATH_GRAD_HEADER_WORD_COUNT = 7u;
const PATH_GRAD_EDGE_WORD_COUNT = 12u;

// Returns the info-stream offset of edge record edge_ix, past the gradient's
// 7-word header.
fn path_grad_edge_offset(path_grad: CmdPathGrad, edge_ix: u32) -> u32 {
    return path_grad.data_offset + PATH_GRAD_HEADER_WORD_COUNT + edge_ix * PATH_GRAD_EDGE_WORD_COUNT;
}

// Loads a point stored as two f32 words in the info stream.
fn path_grad_load_point(offset: u32) -> vec2<f32> {
    return vec2<f32>(bitcast<f32>(info[offset]), bitcast<f32>(info[offset + 1u]));
}

// Loads an associated color stored as four f32 words in the info stream.
fn path_grad_load_color(offset: u32) -> vec4<f32> {
    return vec4<f32>(
        bitcast<f32>(info[offset]),
        bitcast<f32>(info[offset + 1u]),
        bitcast<f32>(info[offset + 2u]),
        bitcast<f32>(info[offset + 3u]));
}

// 2D cross product (the z component of the 3D cross product).
fn cross2(a: vec2<f32>, b: vec2<f32>) -> f32 {
    return a.x * b.y - a.y * b.x;
}

// Intersects the segment p + t*r with the segment q + u*s using the same
// epsilon-expanded parameter bounds as PolygonUtilities on the CPU.
fn path_grad_line_intersection(p: vec2<f32>, r: vec2<f32>, q: vec2<f32>, s: vec2<f32>) -> vec4<f32> {
    let denominator = cross2(r, s);
    if denominator > -1.0e-3 && denominator < 1.0e-3 {
        return vec4<f32>(0.0);
    }

    let qp = q - p;
    let t = cross2(qp, s) / denominator;
    let u = cross2(qp, r) / denominator;
    if t <= -1.0e-3 || t >= 1.001 || u <= -1.0e-3 || u >= 1.001 {
        return vec4<f32>(0.0);
    }

    let point = p + t * r;
    return vec4<f32>(1.0, point.x, point.y, u);
}

// Point-in-triangle test with barycentric output. The sign-product test is a
// direct port of the CPU brush, including its treatment of a zero cross product.
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
    let d12_sign = sign(d1 * d2);
    if d12_sign * sign(d1 * d3) == -1.0 || d12_sign * sign(d2 * d3) == -1.0 {
        return vec3<f32>(0.0);
    }

    let d00 = dot(e1, e1);
    let d01 = -dot(e1, e3);
    let d11 = dot(e3, e3);
    let d20 = dot(pv1, e1);
    let d21 = -dot(pv1, e3);
    let denominator = (d00 * d11) - (d01 * d01);
    let u = ((d11 * d20) - (d01 * d21)) / denominator;
    let v = ((d00 * d21) - (d01 * d20)) / denominator;
    return vec3<f32>(1.0, u, v);
}

// Evaluates the path gradient brush at a point, returning an associated color.
// Payload colors are associated before interpolation as required by CSS Color 4:
// https://www.w3.org/TR/css-color-4/#interpolation-alpha
// A three-edge gradient without an explicit center color interpolates
// its vertex colors barycentrically. Otherwise a ray is cast from the point
// away from the gradient center to find the nearest edge intersection; the
// interpolated edge color is then mixed toward the center color by the ratio
// of the point's distance from the edge to the center's distance from the
// edge. Returns transparent when the point is not covered by any edge.
fn evaluate_path_gradient(path_grad: CmdPathGrad, point: vec2<f32>) -> vec4<f32> {
    let center = path_grad_load_point(path_grad.data_offset);
    let center_color = path_grad_load_color(path_grad.data_offset + 3u);

    if all(point == center) {
        return center_color;
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
        let c1 = path_grad_load_color(edge0 + 8u);
        let c2 = path_grad_load_color(edge2 + 4u);
        return ((1.0 - triangle.y - triangle.z) * c0) + (triangle.y * c1) + (triangle.z * c2);
    }

    let delta = point - center;
    let delta_length_squared = dot(delta, delta);
    if delta_length_squared == 0.0 {
        return center_color;
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
                // PolygonUtilities accepts a small negative edge parameter, while the CPU
                // edge-color ratio is distance based and therefore uses its magnitude.
                closest_color = mix(path_grad_load_color(edge + 4u), path_grad_load_color(edge + 8u), abs(intersection.w));
                found = true;
            }
        }
    }

    if !found {
        return vec4<f32>(0.0);
    }

    let center_distance = distance(closest_point, center);
    let ratio = select(0.0, distance(closest_point, point) / center_distance, center_distance > 0.0);
    return mix(closest_color, center_color, ratio);
}

// Number of horizontally adjacent pixels shaded by each thread.
const PIXELS_PER_THREAD = 4u;

// Tests whether a centre-free interval ends at a contour tip and must remain unlit.
//
// A centre-free interval is the closed span between two crossings when that span contains no pixel
// centre. It normally lights the pixel containing its midpoint. Do not light that pixel when the
// two boundary profiles meet in the contour and both end in the same gap between centre lines.
// That shape is a terminating tip, not a continuing thin feature. Keep the tip when it reaches at
// least halfway into the pixel and the interval is also at least half a pixel long.
//
// `x_axis` selects X profiles for a vertical interval and Y profiles for a horizontal interval.
// `a` and `b` identify the profiles on the two edges that bound the interval.
fn profile_is_stub(x_axis: bool, a: u32, b: u32, centre_px: f32, span_px: f32) -> bool {
    // A sentinel identifier or an absent record table means the interval cannot be classified as
    // a terminating tip. Keep its midpoint pixel.
    if a == PROFILE_ID_SENTINEL || b == PROFILE_ID_SENTINEL || config.profile_records_base == 0u {
        return false;
    }

    // The record region starts with the X and Y counts. Each following record has three words:
    // minimum, maximum, and adjacency link.
    let records = config.profile_records_base;
    let x_count = scene_data[records];
    let y_count = scene_data[records + 1u];
    var base = records + 2u;
    var count = x_count;
    if !x_axis {
        base = records + 2u + x_count * 3u;
        count = y_count;
    }
    if a >= count || b >= count {
        return false;
    }

    let link_a = bitcast<i32>(scene_data[base + a * 3u + 2u]);
    let link_b = bitcast<i32>(scene_data[base + b * 3u + 2u]);

    // Bit 0 records a connection to the previous identifier. Bits 1..31 contain the one-based
    // identifier joined across a closed contour's final point.
    let adjacent = (b == a + 1u && (link_b & 1) != 0)
        || (a == b + 1u && (link_a & 1) != 0)
        || (link_a >> 1u) == i32(b + 1u)
        || (link_b >> 1u) == i32(a + 1u);
    if !adjacent {
        return false;
    }

    let min_a = bitcast<i32>(scene_data[base + a * 3u]);
    let max_a = bitcast<i32>(scene_data[base + a * 3u + 1u]);
    let min_b = bitcast<i32>(scene_data[base + b * 3u]);
    let max_b = bitcast<i32>(scene_data[base + b * 3u + 1u]);
    let centre = i32(round(centre_px * 256.0));
    let span = i32(round(span_px * 256.0));

    // A tip on the positive side has both profile ends before the next centre. A tip on the
    // negative side has both profile starts after the previous centre. Keep either tip when it
    // reaches halfway across its pixel and the interval is at least half a pixel wide.
    if max_a < centre + 256 && max_b < centre + 256 {
        let tip = max(max_a, max_b);
        return !((tip & 255) >= 128 && span >= 128);
    }
    if min_a > centre - 256 && min_b > centre - 256 {
        let tip = min(min_a, min_b);
        let fraction = tip & 255;
        return !(fraction != 0 && fraction <= 128 && span >= 128);
    }
    return false;
}

// Fixed shared-memory capacities for one row or column crossing list. On overflow, the exact
// centre-winding result remains valid. Only midpoint recovery for a centre-free interval is skipped.
const ROW_CROSSING_CAPACITY = 16u;
const COLUMN_CROSSING_CAPACITY = 8u;

// Four invocations shade each row, but all four need the same crossing list. The invocation whose
// first pixel has X = 0 scans the geometry once and calculates winding for all sixteen row centres.
// This replaces four identical segment scans, four halo scans, and four sorts per row.
var<workgroup> aliased_row_crossings: array<array<u32, ROW_CROSSING_CAPACITY>, TILE_HEIGHT>;
var<workgroup> aliased_row_counts: array<u32, TILE_HEIGHT>;
var<workgroup> aliased_row_overflow: array<u32, TILE_HEIGHT>;
var<workgroup> aliased_row_seed: array<i32, TILE_HEIGHT>;
var<workgroup> aliased_row_winding: array<array<i32, TILE_WIDTH>, TILE_HEIGHT>;

// The four invocations in row zero own four columns each. They build all sixteen column lists once.
var<workgroup> aliased_column_crossings: array<array<u32, COLUMN_CROSSING_CAPACITY>, TILE_WIDTH>;
var<workgroup> aliased_column_counts: array<u32, TILE_WIDTH>;
var<workgroup> aliased_column_overflow: array<u32, TILE_WIDTH>;

// Packs one crossing into a sortable word:
// bits  0..15: profile identifier
// bit      16: positive winding direction
// bits 17..31: tile-relative position, biased by 16 pixels, with 9 fractional bits
//
// The full 15-bit position field is required for the right halo, which extends to X = 16.5.
fn pack_crossing(position: f32, direction_positive: bool, profile_id: u32) -> u32 {
    let fixed = min(u32(max(position + 16.0, 0.0) * 512.0), 0x7fffu);
    return (fixed << 17u) | (u32(direction_positive) << 16u) | profile_id;
}

// Recovers the position a crossing was packed with.
fn unpack_crossing_position(packed: u32) -> f32 {
    return (f32(packed >> 17u) / 512.0) - 16.0;
}

// Inserts one crossing into its row's shared sorted list. One invocation owns each row, so the
// insertion needs no atomics. Keeping the list sorted here also removes four per-thread sorts.
fn insert_aliased_row_crossing(row: u32, packed: u32) {
    let count = aliased_row_counts[row];
    if count < ROW_CROSSING_CAPACITY {
        var insert = count;
        while insert > 0u && aliased_row_crossings[row][insert - 1u] > packed {
            aliased_row_crossings[row][insert] = aliased_row_crossings[row][insert - 1u];
            insert -= 1u;
        }

        aliased_row_crossings[row][insert] = packed;
        aliased_row_counts[row] = count + 1u;
    } else {
        aliased_row_overflow[row] = 1u;
    }
}

// Computes binary coverage for one 16x16 tile.
//
// The workgroup has 64 invocations. Each invocation returns four adjacent pixels in one row.
// Processing has four phases:
// 1. Sixteen row owners and four column owners clear their shared lists.
// 2. Row owners scan current and neighboring tile segments. Column owners scan current segments.
// 3. A barrier publishes the sorted crossings and exact centre windings.
// 4. Every invocation consumes its row list and four column lists.
//
// Exact winding decides normal centre coverage. A closed interval that contains no centre lights
// its midpoint pixel unless its profiles identify a terminating tip. The column walk applies the
// same rule vertically to recover horizontal features that lie between two row centres.
fn fill_path_aliased(fill: CmdFill, xy: vec2<f32>, global_xy: vec2<f32>, result: ptr<function, array<f32, PIXELS_PER_THREAD>>) {
    let n_segs = fill.size_and_rule >> 2u;
    let even_odd = (fill.size_and_rule & 1u) != 0u;
    let sample_y = xy.y + 0.5;
    let row = u32(xy.y);
    let owns_row = xy.x == 0.0;
    let owns_columns = xy.y == 0.0;

    // A tile can contain consecutive aliased fill commands. All invocations must finish reading
    // the previous command's shared row and column state before its owners clear it.
    workgroupBarrier();

    if owns_row {
        aliased_row_counts[row] = 0u;
        aliased_row_overflow[row] = 0u;
        aliased_row_seed[row] = fill.backdrop;
        for (var i = 0u; i < TILE_WIDTH; i += 1u) {
            aliased_row_winding[row][i] = fill.backdrop;
        }
    }

    if owns_columns {
        for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
            let column = u32(xy.x) + i;
            aliased_column_counts[column] = 0u;
            aliased_column_overflow[column] = 0u;
        }
    }

    // The sixteen row owners and four column owners are the only invocations that need segment
    // data. The remaining 45 invocations wait at the collection barrier and then consume their
    // row and columns. Row zero, X zero owns both one row and four columns.
    if owns_row || owns_columns {
        for (var s = 0u; s < n_segs; s++) {
            let segment = segments[fill.seg_data + s];
            if segment.y_edge == HALO_ONLY_Y_EDGE {
                continue;
            }

            let delta = segment.point1 - segment.point0;

            // The CPU captures crossings after rounding endpoints to 24.8. Use the same integer
            // interpolation so hinted edges and exact half-pixel positions select the same pixels.
            let p0_fixed = vec2<i32>(round(segment.point0 * 256.0));
            let p1_fixed = vec2<i32>(round(segment.point1 * 256.0));

            if owns_row {
                let sample_y_fixed = i32(sample_y * 256.0);

                // Half-open span ownership counts a vertex on the centre line on exactly one edge.
                let crosses_row = (p0_fixed.y <= sample_y_fixed) != (p1_fixed.y <= sample_y_fixed);
                if crosses_row {
                    let x_cross_fixed = p0_fixed.x
                        + (((p1_fixed.x - p0_fixed.x) * (sample_y_fixed - p0_fixed.y)) / (p1_fixed.y - p0_fixed.y));

                    // Increasing local X walks from the tile's left edge. Match the sign used by
                    // the backdrop and y_edge values so all three contributions form one winding.
                    let direction = -i32(sign(delta.y));
                    for (var k = 0u; k < TILE_WIDTH; k += 1u) {
                        if x_cross_fixed <= (i32(k) << 8) + 128 {
                            aliased_row_winding[row][k] += direction;
                        }
                    }

                    // A horizontal interval uses the Y profiles of its two boundary edges. Their
                    // identifiers occupy bits 16..31 of the segment tag.
                    insert_aliased_row_crossing(
                        row,
                        pack_crossing(f32(x_cross_fixed) / 256.0, direction > 0, segment.tag >> 16u));
                }

                // y_edge represents the part of this segment clipped to the left of the tile. Add
                // its winding step to every centre below that crossing. It is not a column event;
                // the corresponding geometry lies outside this tile.
                if segment.y_edge < 1.0e9 {
                    // NearestCrossingCount rounds an exact half-row carry away from zero, so a
                    // left-edge crossing on the row centre is included.
                    let step = select(0, i32(sign(delta.x)), sample_y >= segment.y_edge);
                    for (var k = 0u; k < TILE_WIDTH; k += 1u) {
                        aliased_row_winding[row][k] += step;
                    }

                    aliased_row_seed[row] += step;
                }
            }

            if owns_columns {
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    let column = u32(xy.x) + i;
                    let sample_x_fixed = (i32(column) << 8) + 128;
                    if (p0_fixed.x <= sample_x_fixed) != (p1_fixed.x <= sample_x_fixed) {
                        let column_count = aliased_column_counts[column];
                        if column_count < COLUMN_CROSSING_CAPACITY {
                            let y_cross_fixed = p0_fixed.y
                                + (((p1_fixed.y - p0_fixed.y) * (sample_x_fixed - p0_fixed.x)) / (p1_fixed.x - p0_fixed.x));

                            // A vertical interval uses the X profiles of its two boundary edges.
                            // Their identifiers occupy bits 0..15 of the segment tag.
                            let packed = pack_crossing(
                                f32(y_cross_fixed) / 256.0,
                                segment.point1.x > segment.point0.x,
                                segment.tag & PROFILE_ID_MASK);

                            // Keep the shared list sorted as it is built. Sorting once here avoids
                            // copying and sorting the same column again in every row.
                            var insert = column_count;
                            while insert > 0u && aliased_column_crossings[column][insert - 1u] > packed {
                                aliased_column_crossings[column][insert] = aliased_column_crossings[column][insert - 1u];
                                insert -= 1u;
                            }

                            aliased_column_crossings[column][insert] = packed;
                            aliased_column_counts[column] = column_count + 1u;
                        } else {
                            aliased_column_overflow[column] = 1u;
                        }
                    }
                }
            }
        }
    }

    // A CPU row list is not split at tile boundaries. A crossing no more than half a pixel outside
    // this tile can pair with a local crossing to form a centre-free interval whose midpoint belongs
    // to this tile. Read that narrow halo from the two real neighboring tile slices. Do not add halo
    // crossings to centre winding; backdrop and y_edge already include their winding effect.
    let tile_and_neighbors = fill.coverage_data;
    let current_tile = tile_and_neighbors & ALIASED_TILE_INDEX_MASK;
    let halo_tiles = array<u32, 2>(
        select(INVALID_TILE_INDEX, current_tile - 1u, (tile_and_neighbors & ALIASED_LEFT_NEIGHBOR_BIT) != 0u),
        select(INVALID_TILE_INDEX, current_tile + 1u, (tile_and_neighbors & ALIASED_RIGHT_NEIGHBOR_BIT) != 0u));
    if owns_row {
        for (var h = 0u; h < 2u; h += 1u) {
            let halo_tile_ix = halo_tiles[h];
            if halo_tile_ix == INVALID_TILE_INDEX {
                continue;
            }

            let halo_data = ~path_tiles[halo_tile_ix].segment_count_or_ix;
            let halo_count = u32(path_tiles[halo_tile_ix].backdrop);
            let fixed_offset = select(-4096, 4096, h != 0u);
            for (var s = 0u; s < halo_count; s += 1u) {
                let segment = segments[halo_data + s];
                let p0_fixed = vec2<i32>(round(segment.point0 * 256.0));
                let p1_fixed = vec2<i32>(round(segment.point1 * 256.0));
                let sample_y_fixed = i32(sample_y * 256.0);
                if (p0_fixed.y <= sample_y_fixed) == (p1_fixed.y <= sample_y_fixed) {
                    continue;
                }

                let local_x_fixed = p0_fixed.x
                    + (((p1_fixed.x - p0_fixed.x) * (sample_y_fixed - p0_fixed.y)) / (p1_fixed.y - p0_fixed.y))
                    + fixed_offset;
                let in_halo = select(
                    local_x_fixed >= -128 && local_x_fixed <= 0,
                    local_x_fixed >= 4096 && local_x_fixed <= 4224,
                    h != 0u);
                if !in_halo {
                    continue;
                }

                let direction = -i32(sign(segment.point1.y - segment.point0.y));
                insert_aliased_row_crossing(
                    row,
                    pack_crossing(f32(local_x_fixed) / 256.0, direction > 0, segment.tag >> 16u));

                if h == 0u {
                    // row_seed is the winding at X = 0. The sorted list begins in the left halo, so
                    // remove each halo crossing to obtain the winding immediately before that list.
                    aliased_row_seed[row] -= direction;
                }
            }
        }
    }

    // Publish the row and column collections before any invocation reads its four-pixel slice.
    workgroupBarrier();

    let row_count = aliased_row_counts[row];
    let row_overflow = aliased_row_overflow[row] != 0u;
    let row_seed = aliased_row_seed[row];

    var lit: array<bool, PIXELS_PER_THREAD>;
    for (var k = 0u; k < PIXELS_PER_THREAD; k += 1u) {
        let centre_winding = aliased_row_winding[row][u32(xy.x) + k];
        if even_odd {
            lit[k] = (centre_winding & 1) != 0;
        } else {
            lit[k] = centre_winding != 0;
        }
    }

    // Walk the sorted row crossings from left to right. row_seed is the winding immediately before
    // the first local or halo crossing.
    if !row_overflow && row_count > 0u {
        var w = row_seed;
        if even_odd {
            w &= 1;
        }

        var interval_start = 0.0;
        var enter_id = PROFILE_ID_SENTINEL;
        var has_local_start = false;
        for (var c = 0u; c < row_count; c += 1u) {
            let packed = aliased_row_crossings[row][c];
            let position = unpack_crossing_position(packed) - xy.x;
            let previous = w;
            if even_odd {
                w ^= 1;
            } else {
                w += select(-1, 1, ((packed >> 16u) & 1u) != 0u);
            }

            // Interval coverage is closed at both ends. The direct winding comparison already
            // includes an opening edge on a centre, but it excludes a closing edge on a centre.
            // Restore that closing endpoint explicitly.
            let centre_index = i32(floor(position));
            if centre_index >= 0 && centre_index < i32(PIXELS_PER_THREAD)
                && position == f32(centre_index) + 0.5 && (previous != 0 || w != 0) {
                lit[u32(centre_index)] = true;
            }

            if previous == 0 && w != 0 {
                interval_start = position;
                enter_id = packed & PROFILE_ID_MASK;
                has_local_start = true;
                continue;
            }
            if previous == 0 || w != 0 {
                continue;
            }

            // An interval already open before the halo is not a local centre-free interval; it may
            // have covered centres farther left. The clipped-left case is different: the CPU keeps
            // the closing edge's one-pixel extension, so its first destination pixel remains covered.
            if !has_local_start {
                let clipped_left = (tile_and_neighbors & ALIASED_CLIPPED_LEFT_BIT) != 0u;
                if clipped_left && position == 0.0 && global_xy.x + 1.0 == fill.interest.z {
                    lit[0] = true;
                }

                continue;
            }
            has_local_start = false;

            // For closed endpoint coverage, the first contained centre is ceil(start - 0.5) and
            // the last is floor(end - 0.5). last < first means the interval contains no centre.
            let first = i32(ceil(interval_start - 0.5));
            let last = i32(floor(position - 0.5));
            if last >= first {
                continue;
            }

            let interval_start_fixed = i32(round((interval_start + xy.x) * 256.0));
            let position_fixed = i32(round((position + xy.x) * 256.0));
            let midpoint_pixel = ((interval_start_fixed >> 1) + (position_fixed >> 1)) >> 8;
            let local = midpoint_pixel - i32(xy.x);
            if local >= 0 && local < i32(PIXELS_PER_THREAD) && !lit[u32(local)]
                && !profile_is_stub(false, enter_id, packed & PROFILE_ID_MASK, global_xy.y + 0.5, position - interval_start) {
                lit[u32(local)] = true;
            }
        }
    }

    // The CPU column pass starts each 16-row band at zero and accepts only intervals with both
    // crossings inside that band. Do not derive a top-edge winding seed: that would turn geometry
    // clipped above the band into a new open interval and could add a pixel absent from the CPU.
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        let column = u32(xy.x) + i;
        let column_count = aliased_column_counts[column];

        // Match the CPU's merge order. It consults column results only when this row has at least
        // one horizontal-centre crossing. Overflow disables only this secondary recovery.
        if lit[i] || row_overflow || row_count == 0u || aliased_column_overflow[column] != 0u || column_count < 2u {
            continue;
        }

        var w = 0;

        var interval_start = 0.0;
        var enter_id = PROFILE_ID_SENTINEL;
        for (var c = 0u; c < column_count; c += 1u) {
            let packed = aliased_column_crossings[column][c];
            let position = unpack_crossing_position(packed);
            let previous = w;
            if even_odd {
                w ^= 1;
            } else {
                w += select(-1, 1, ((packed >> 16u) & 1u) != 0u);
            }

            if previous == 0 && w != 0 {
                interval_start = position;
                enter_id = packed & PROFILE_ID_MASK;
                continue;
            }
            if previous == 0 || w != 0 {
                continue;
            }

            // Closed at both ends, as in the row walk: an interval reaching a row centre
            // exactly is owned by that row's walk and is not a collapsed interval.
            if i32(floor(position - 0.5)) >= i32(ceil(interval_start - 0.5)) {
                continue;
            }

            // Match the CPU's overflow-safe fixed-point midpoint exactly: it
            // halves each endpoint before adding, so two odd coordinates land
            // one 24.8 unit below their mathematical average.
            let interval_start_fixed = i32(round(interval_start * 256.0));
            let position_fixed = i32(round(position * 256.0));
            let midpoint_row = ((interval_start_fixed >> 1) + (position_fixed >> 1)) >> 8;
            if midpoint_row == i32(xy.y)
                && !profile_is_stub(true, enter_id, packed & PROFILE_ID_MASK, global_xy.x + f32(i) + 0.5, position - interval_start) {
                lit[i] = true;
            }
        }
    }

    var area: array<f32, PIXELS_PER_THREAD>;
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        area[i] = select(0.0, 1.0, lit[i]);
    }

    // Discard coverage outside the fill's raster interest rectangle.
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        let pixel = global_xy + vec2<f32>(f32(i), 0.0);
        if pixel.x < fill.interest.x || pixel.y < fill.interest.y || pixel.x >= fill.interest.z || pixel.y >= fill.interest.w {
            area[i] = 0.0;
        }
    }

    *result = area;
}

// Computes per-pixel coverage for a CMD_FILL using analytic area
// anti-aliasing. Signed winding is accumulated from the backdrop and every
// segment in the tile, the fill rule is applied, and coverage outside the
// fill's raster interest rectangle is zeroed. xy is the thread's first pixel
// in tile-local space (segments are tile-relative); global_xy is the same
// pixel in full-target space. result receives coverage for PIXELS_PER_THREAD
// adjacent pixels.
//
// FIXME: This should return an array when https://github.com/gfx-rs/naga/issues/1930 is fixed.
fn fill_path(fill: CmdFill, xy: vec2<f32>, global_xy: vec2<f32>, result: ptr<function, array<f32, PIXELS_PER_THREAD>>) {
    // size_and_rule: bit 0 = even-odd, bit 1 = aliased coverage, bits 2.. = segment count.
    let n_segs = fill.size_and_rule >> 2u;
    let even_odd = (fill.size_and_rule & 1u) != 0u;
    let aliased = (fill.size_and_rule & 2u) != 0u;
    if aliased {
        fill_path_aliased(fill, xy, global_xy, result);
        return;
    }

    var area: array<f32, PIXELS_PER_THREAD>;
    let backdrop_f = f32(fill.backdrop);
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        area[i] = backdrop_f;
    }
    for (var i = 0u; i < n_segs; i++) {
        let seg_off = fill.seg_data + i;
        let segment = segments[seg_off];
        if segment.y_edge == HALO_ONLY_Y_EDGE {
            continue;
        }

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
    if bitcast<f32>(fill.coverage_data) > 0.0 {
        // For antialiased fills the coverage_data word carries the perceptual coverage boost.
        // An S-curve darkens coverage
        // above one half and lightens it below, so text stems solidify while nearly-open
        // counters stay bright; at full strength the remap is exactly smoothstep. Matches
        // the CPU rasterizer's AreaToCoverage boost.
        let boost = bitcast<f32>(fill.coverage_data);
        for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
            let a = area[i];
            area[i] = a + boost * a * (1.0 - a) * (2.0 * a - 1.0);
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
// interprets the tile's PTCL commands until CMD_END, and writes the result in
// the target's configured alpha representation.
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
    // Seed each pixel from the existing target contents. The generated target
    // decoder normalizes numeric encoding and returns the associated form used internally.
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        let coords = vec2<i32>(xy_uint + vec2(i, 0u));
        let backdrop_raw = textureLoad(backdrop_texture, coords, 0);
        rgba[i] = decode_target(backdrop_raw);
    }
    // Clip saves remain in the fine shader's associated working representation. Two
    // binary16 pairs avoid the severe RGBA8 loss that a nested clip previously introduced.
    var blend_stack: array<array<vec2<u32>, PIXELS_PER_THREAD>, BLEND_STACK_SPLIT>;
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
                let fg = decode_paint_color(unpack_color_f16(color.color_rg, color.color_ba));
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        rgba[i] = compose_draw_with_coverage(rgba[i], fg, area[i], color.draw_flags);
                    }
                }
                cmd_ix += 4u;
            }
            case CMD_RECOLOR: {
                let recolor = read_recolor(cmd_ix);
                let source_words = vec4<u32>(info[recolor.data_offset], info[recolor.data_offset + 1u], info[recolor.data_offset + 2u], info[recolor.data_offset + 3u]);
                let target_words = vec4<u32>(info[recolor.data_offset + 4u], info[recolor.data_offset + 5u], info[recolor.data_offset + 6u], info[recolor.data_offset + 7u]);
                let source = bitcast<vec4<f32>>(source_words);
                let target_native = bitcast<vec4<f32>>(target_words);
                let target_internal = recolor_native_to_internal(target_native);
                let threshold = bitcast<f32>(info[recolor.data_offset + 8u]);
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        // CPU Recolor reads a stored TPixel, uses that same value for matching and
                        // both blend backdrops, then writes a TPixel after each blend boundary.
                        let bg_native = recolor_store_target(rgba[i]);
                        let bg_internal = recolor_native_to_internal(bg_native);
                        let delta = bg_native - source;
                        let distance = dot(delta, delta);
                        var overlay_internal = bg_internal;
                        if distance <= threshold {
                            // Blend strength ramps from 1 at an exact match to
                            // 0 at the threshold boundary.
                            let t = (threshold - distance) / threshold;
                            let inner = compose_recolor_inner(bg_internal, target_internal, t, recolor.draw_flags);
                            overlay_internal = recolor_native_to_internal(recolor_store_target(inner));
                        }

                        let outer = compose_draw_with_coverage(bg_internal, overlay_internal, area[i], recolor.draw_flags);
                        rgba[i] = recolor_native_to_internal(recolor_store_target(outer));
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_BEGIN_CLIP: {
                // Save the current tile content, then seed the new group. Isolated groups
                // (layers) start transparent so their contents composite as a unit; clip
                // groups keep the current content so composition modes inside the clip see
                // the same destination the CPU backend's per-draw masking sees. Shallow
                // stack entries live in registers; deeper ones spill to the blend buffer.
                let isolated = ptcl[cmd_ix + 1u] != 0u;
                if clip_depth < BLEND_STACK_SPLIT {
                    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        blend_stack[clip_depth][i] = pack_clip_color(rgba[i]);
                        if isolated {
                            rgba[i] = vec4(0.0);
                        }
                    }
                } else {
                    let blend_in_scratch = clip_depth - BLEND_STACK_SPLIT;
                    let local_tile_ix = local_id.x * PIXELS_PER_THREAD + local_id.y * TILE_WIDTH;
                    let local_blend_start = blend_offset + blend_in_scratch * TILE_WIDTH * TILE_HEIGHT + local_tile_ix;
                    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                        blend_spill[local_blend_start + i] = pack_clip_color(rgba[i]);
                        if isolated {
                            rgba[i] = vec4(0.0);
                        }
                    }
                }
                clip_depth += 1u;
                cmd_ix += 2u;
            }
            case CMD_END_CLIP: {
                let end_clip = read_end_clip(cmd_ix);
                clip_depth -= 1u;
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    var bg_rgba: vec2<u32>;
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
                    // Strip the local mask bits so only the packed blend mode remains.
                    let clip_area = select(area[i], 1.0 - area[i], (end_clip.blend & CLIP_DIFFERENCE_MASK_BIT) != 0u);
                    let isolated = (end_clip.blend & CLIP_ISOLATED_MASK_BIT) != 0u;
                    let clip_blend = end_clip.blend & ~(CLIP_DIFFERENCE_MASK_BIT | CLIP_ISOLATED_MASK_BIT);

                    let bg = unpack_clip_color(bg_rgba);

                    // Non-isolated groups (clip masks) already contain the saved content, so
                    // the pop is a pure coverage lerp between the saved backdrop and the group.
                    // This matches the CPU backend, which applies clip coverage per draw against
                    // the real target, and keeps solid-tile skipped clips and grouped clips
                    // producing identical results for every composition mode.
                    if !isolated {
                        rgba[i] = bg + ((rgba[i] - bg) * clip_area);
                        continue;
                    }

                    let fg = rgba[i] * end_clip.alpha;

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
                        let t = extend_mode_normalized(my_d, lin.extend_mode);
                        var fg_rgba = vec4(0.0);
                        if t >= 0.0 {
                            let x = i32(round(t * f32(GRADIENT_WIDTH - 1)));
                            fg_rgba = textureLoad(gradients, vec2(x, i32(lin.index)), 0);
                        }

                        // CPU gradient brushes return a transparent overlay for DontFill samples,
                        // then blend it normally. Destructive composition modes must therefore
                        // still run when no ramp texel is selected.
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
                    var fg_rgba = vec4(0.0);
                    if is_valid {
                        t = focal_x + t_sign * t;

                        // The conical solver swaps the circles to obtain a stable canonical form.
                        // Restore the brush parameter before applying its repetition semantics.
                        t = select(t, 1.0 - t, is_swapped);
                        t = extend_mode_normalized(t, rad.extend_mode);
                        if t >= 0.0 {
                            let ramp_x = i32(round(t * f32(GRADIENT_WIDTH - 1)));
                            fg_rgba = textureLoad(gradients, vec2(ramp_x, i32(rad.index)), 0);
                        }
                    }

                    // Invalid conical solutions and DontFill both produce the transparent
                    // overlay that the CPU still sends through coverage and composition.
                    rgba[i] = compose_draw_with_coverage(rgba[i], fg_rgba, area[i], draw_flags);
                }
                cmd_ix += 3u;
            }
            case CMD_ELLIPTIC_GRAD: {
                let elliptic = read_elliptic_grad(cmd_ix);
                let draw_flags = info[ptcl[cmd_ix + 2u] - 1u];
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        let my_xy = vec2(xy.x + f32(i) + 0.5, xy.y + 0.5);
                        if elliptic.kind == ELLIPTIC_GRAD_KIND_NORMAL {
                            let local_xy = elliptic.matrx.xy * my_xy.x + elliptic.matrx.zw * my_xy.y + elliptic.xlat;
                            let radius = length(local_xy);
                            var fg_rgba = vec4(0.0);

                            // A NaN brush transform and DontFill both return a transparent CPU
                            // overlay. Skip only the ramp lookup, not coverage or composition.
                            if radius == radius {
                                let t = extend_mode_normalized(radius, elliptic.extend_mode);
                                if t >= 0.0 {
                                    let ramp_x = i32(round(t * f32(GRADIENT_WIDTH - 1)));
                                    fg_rgba = textureLoad(gradients, vec2(ramp_x, i32(elliptic.index)), 0);
                                }
                            }

                            rgba[i] = compose_draw_with_coverage(rgba[i], fg_rgba, area[i], draw_flags);
                        } else {
                            // Keep the CPU order of operations for a collapsed ellipse: subtract
                            // the center first, then rotate. An affine translation would introduce
                            // cancellation error and turn exact on-axis zeroes into tiny values.
                            let center_relative = my_xy - elliptic.xlat;
                            let local_xy = elliptic.matrx.xy * center_relative.x + elliptic.matrx.zw * center_relative.y;
                            let is_undefined = select(
                                local_xy.y == 0.0,
                                local_xy.x == 0.0 || local_xy.y == 0.0,
                                elliptic.kind == ELLIPTIC_GRAD_KIND_POINT);
                            var fg_rgba = vec4(0.0);

                            // CPU division gives NaN on a collapsed axis and +Inf everywhere else.
                            // Its sole NaN check occurs before repetition, so +Inf ultimately selects
                            // the last stop for pad, repeat, and reflect; DontFill remains transparent.
                            if !is_undefined && elliptic.extend_mode != EXTEND_DECAL {
                                fg_rgba = textureLoad(gradients, vec2(i32(GRADIENT_WIDTH - 1), i32(elliptic.index)), 0);
                            }

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
                        let is_center = x == 0.0 && y == 0.0;
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

                        // The center has no direction. Match the CPU brush's stable definition
                        // of final gradient parameter zero, independent of the start angle.
                        phi = select(phi, 0.0, is_center);
                        let t = extend_mode_normalized(phi, sweep.extend_mode);
                        var fg_rgba = vec4(0.0);
                        if t >= 0.0 {
                            let ramp_x = i32(round(t * f32(GRADIENT_WIDTH - 1)));
                            fg_rgba = textureLoad(gradients, vec2(ramp_x, i32(sweep.index)), 0);
                        }

                        // DontFill is a transparent brush sample, not an omitted draw, so it
                        // still participates in Src, Clear, and every other composition mode.
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
                            // Atlas texels use the target TPixel's physical numeric encoding. Decode
                            // SNORM storage before interpreting alpha so associated and unassociated
                            // sources both enter the common composition space.
                            let atlas_color = decode_image_numeric(textureLoad(image_atlas, atlas_uv_clamped, 0), image.signed_unit);
                            let fg_rgba = maybe_premul_alpha(atlas_color, image.alpha_type);
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
            textureStore(output, vec2<i32>(coords), rgba[i]);
        }
    }
}

// Converts a straight-alpha color to premultiplied form.
fn premul_alpha(rgba: vec4<f32>) -> vec4<f32> {
    return vec4(rgba.rgb * rgba.a, rgba.a);
}
