// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Fine rasterizer.

struct Tile {
    backdrop: i32,
    segments: u32,
}

#import segment
#import config
#import drawtag

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage> segments: array<Segment>;

#import blend
#import ptcl

const GRADIENT_WIDTH = 512;

const IMAGE_QUALITY_LOW = 0u;
const IMAGE_QUALITY_MEDIUM = 1u;
const IMAGE_QUALITY_HIGH = 2u;

const LUMINANCE_MASK_LAYER = 0x10000u;

@group(0) @binding(2)
var<storage> ptcl: array<u32>;

@group(0) @binding(3)
var<storage> info: array<u32>;

@group(0) @binding(4)
var<storage, read_write> blend_spill: array<u32>;

@group(0) @binding(5)
var output: texture_storage_2d<rgba8unorm, write>;

@group(0) @binding(6)
var gradients: texture_2d<f32>;

@group(0) @binding(7)
var image_atlas: texture_2d<f32>;

@group(0) @binding(8)
var backdrop_texture: texture_2d<f32>;

fn read_fill(cmd_ix: u32) -> CmdFill {
    let size_and_rule = ptcl[cmd_ix + 1u];
    let seg_data = ptcl[cmd_ix + 2u];
    let backdrop = i32(ptcl[cmd_ix + 3u]);
    return CmdFill(size_and_rule, seg_data, backdrop);
}

fn read_color(cmd_ix: u32) -> CmdColor {
    let rgba_color = ptcl[cmd_ix + 1u];
    let draw_flags = ptcl[cmd_ix + 2u];
    return CmdColor(rgba_color, draw_flags);
}

fn read_recolor(cmd_ix: u32) -> CmdRecolor {
    let source_color = ptcl[cmd_ix + 1u];
    let target_color = ptcl[cmd_ix + 2u];
    let threshold = bitcast<f32>(ptcl[cmd_ix + 3u]);
    let draw_flags = ptcl[cmd_ix + 4u];
    return CmdRecolor(source_color, target_color, threshold, draw_flags);
}

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

fn read_path_grad(cmd_ix: u32) -> CmdPathGrad {
    let data_offset = ptcl[cmd_ix + 1u];
    let edge_count = ptcl[cmd_ix + 2u];
    let flags = ptcl[cmd_ix + 3u];
    let draw_flags = ptcl[cmd_ix + 4u];
    return CmdPathGrad(data_offset, edge_count, flags, draw_flags);
}

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
    let quality = (sample_alpha >> 12u) & 0x3u;
    let x_extend = (sample_alpha >> 10u) & 0x3u;
    let y_extend = (sample_alpha >> 8u) & 0x3u;
    // The following are not intended to be bitcasts
    let x = f32(xy >> 16u);
    let y = f32(xy & 0xffffu);
    let width = f32(width_height >> 16u);
    let height = f32(width_height & 0xffffu);
    return CmdImage(matrx, xlat, vec2(x, y), vec2(width, height), format, x_extend, y_extend, quality, alpha, alpha_type);
}

fn read_end_clip(cmd_ix: u32) -> CmdEndClip {
    let blend = ptcl[cmd_ix + 1u];
    let alpha = bitcast<f32>(ptcl[cmd_ix + 2u]);
    return CmdEndClip(blend, alpha);
}

fn read_draw_blend_mode(draw_flags: u32) -> u32 {
    return (draw_flags & DRAW_FLAGS_BLEND_MODE_MASK) >> DRAW_FLAGS_BLEND_MODE_SHIFT;
}

fn read_draw_mix_mode(draw_flags: u32) -> u32 {
    return read_draw_blend_mode(draw_flags) >> 8u;
}

fn read_draw_compose_mode(draw_flags: u32) -> u32 {
    return read_draw_blend_mode(draw_flags) & 0xffu;
}

fn read_draw_blend_alpha(draw_flags: u32) -> f32 {
    let packed = (draw_flags & DRAW_FLAGS_BLEND_ALPHA_MASK) >> DRAW_FLAGS_BLEND_ALPHA_SHIFT;
    return f32(packed) / 65535.0;
}

fn is_default_draw_blend(draw_flags: u32) -> bool {
    return read_draw_blend_mode(draw_flags) == ((MIX_NORMAL << 8u) | COMPOSE_SRC_OVER)
        && (draw_flags & DRAW_FLAGS_BLEND_ALPHA_MASK) == DRAW_FLAGS_BLEND_ALPHA_MASK;
}

fn compose_draw(backdrop: vec4<f32>, source: vec4<f32>, draw_flags: u32) -> vec4<f32> {
    let effective_alpha = source.a * read_draw_blend_alpha(draw_flags);
    if effective_alpha <= (0.5 / 255.0) {
        return backdrop;
    }

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

const PIXEL_FORMAT_RGBA: u32 = 0u;
const PIXEL_FORMAT_BGRA: u32 = 1u;
// Normalises subpixel order loaded from an image, based on the image's format.
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
// Premultiplies alpha if not already
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

const EXTEND_PAD: u32 = 0u;
const EXTEND_REPEAT: u32 = 1u;
const EXTEND_REFLECT: u32 = 2u;
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

fn extend_mode(t: f32, mode: u32, max: f32) -> f32 {
    switch mode {
        case EXTEND_PAD: {
            return clamp(t, 0.0, max);
        }
        case EXTEND_REPEAT: {
            return extend_mode_normalized(t / max, mode) * max;
        }
        case EXTEND_REFLECT, default: {
            return extend_mode_normalized(t / max, mode) * max;
        }
    }
}

fn image_extend_mode_normalized(t: f32, mode: u32) -> f32 {
    switch mode {
        case EXTEND_PAD: {
            return clamp(t, 0.0, 1.0);
        }
        case EXTEND_REPEAT: {
            return fract(t);
        }
        case EXTEND_REFLECT, default: {
            let reflected = fract(0.5 * t) * 2.0;
            return 1.0 - abs(reflected - 1.0);
        }
    }
}

fn image_extend_mode(t: f32, mode: u32, max: f32) -> f32 {
    switch mode {
        case EXTEND_PAD: {
            return clamp(t, 0.0, max);
        }
        case EXTEND_REPEAT: {
            return image_extend_mode_normalized(t / max, mode) * max;
        }
        case EXTEND_REFLECT, default: {
            return image_extend_mode_normalized(t / max, mode) * max;
        }
    }
}

fn image_extend_mode_i32(t: f32, mode: u32, max: i32) -> i32 {
    switch mode {
        case EXTEND_PAD: {
            return clamp(i32(t), 0, max - 1);
        }
        case EXTEND_REPEAT: {
            let wrapped = i32(t) % max;
            return select(wrapped + max, wrapped, wrapped >= 0);
        }
        case EXTEND_REFLECT, default: {
            let reflected = clamp(image_extend_mode(t, mode, f32(max)), 0.0, f32(max - 1));
            return i32(reflected);
        }
    }
}

const PATH_GRAD_HAS_EXPLICIT_CENTER_COLOR = 1u;
const PATH_GRAD_HEADER_WORD_COUNT = 4u;
const PATH_GRAD_EDGE_WORD_COUNT = 6u;

fn path_grad_edge_offset(path_grad: CmdPathGrad, edge_ix: u32) -> u32 {
    return path_grad.data_offset + PATH_GRAD_HEADER_WORD_COUNT + edge_ix * PATH_GRAD_EDGE_WORD_COUNT;
}

fn path_grad_load_point(offset: u32) -> vec2<f32> {
    return vec2<f32>(bitcast<f32>(info[offset]), bitcast<f32>(info[offset + 1u]));
}

fn path_grad_load_color(offset: u32) -> vec4<f32> {
    return unpack4x8unorm(info[offset]);
}

fn cross2(a: vec2<f32>, b: vec2<f32>) -> f32 {
    return a.x * b.y - a.y * b.x;
}

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

const PIXELS_PER_THREAD = 4u;

// Analytic area anti-aliasing.
//
// FIXME: This should return an array when https://github.com/gfx-rs/naga/issues/1930 is fixed.
fn fill_path(fill: CmdFill, xy: vec2<f32>, result: ptr<function, array<f32, PIXELS_PER_THREAD>>) {
    let n_segs = fill.size_and_rule >> 1u;
    let even_odd = (fill.size_and_rule & 1u) != 0u;
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
    *result = area;
}

// The X size should be 16 / PIXELS_PER_THREAD
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
    let xy = vec2(f32(global_id.x * PIXELS_PER_THREAD), f32(config.chunk_tile_y_start * TILE_HEIGHT + global_id.y));
    let xy_uint = vec2<u32>(xy);
    let local_xy = vec2(f32(local_id.x * PIXELS_PER_THREAD), f32(local_id.y));
    var rgba: array<vec4<f32>, PIXELS_PER_THREAD>;
    for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
        let coords = vec2<i32>(xy_uint + vec2(i, 0u));
        let backdrop_raw = textureLoad(backdrop_texture, coords, 0);
        rgba[i] = vec4(backdrop_raw.rgb * backdrop_raw.a, backdrop_raw.a);
    }
    var blend_stack: array<array<u32, PIXELS_PER_THREAD>, BLEND_STACK_SPLIT>;
    var clip_depth = 0u;
    var area: array<f32, PIXELS_PER_THREAD>;
    var cmd_ix = tile_ix * PTCL_INITIAL_ALLOC;
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
                fill_path(fill, local_xy, &area);
                cmd_ix += 4u;
            }
            case CMD_SOLID: {
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    area[i] = 1.0;
                }
                cmd_ix += 1u;
            }
            case CMD_COLOR: {
                let color = read_color(cmd_ix);
                let fg = unpack4x8unorm(color.rgba_color);
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        let fg_i = fg * area[i];
                        rgba[i] = compose_draw(rgba[i], fg_i, color.draw_flags);
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
                        let bg_sep = vec4(bg.rgb / max(bg.a, 1e-6), bg.a);
                        let delta = bg_sep - source;
                        let distance = dot(delta, delta);
                        if distance <= recolor.threshold {
                            let t = (recolor.threshold - distance) / recolor.threshold;
                            let target_premul = premul_alpha(target_color);
                            let recolored = target_premul * t + bg * (1.0 - target_color.a * t);
                            let fg = (recolored - bg) * area[i] + bg * area[i];
                            rgba[i] = compose_draw(bg, fg, recolor.draw_flags);
                        }
                    }
                }
                cmd_ix += 5u;
            }
            case CMD_BEGIN_CLIP: {
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
                    let bg = unpack4x8unorm(bg_rgba);
                    let fg = rgba[i] * area[i] * end_clip.alpha;
                    if end_clip.blend == LUMINANCE_MASK_LAYER {
                        // TODO: Does this case apply more generally?
                        // See https://github.com/linebender/vello/issues/1061
                        // TODO: How do we handle anti-aliased edges here?
                        // This is really an imaging model question
                        if area[i] == 0f {
                            rgba[i] = bg;
                            continue;
                        }
                        let luminance = clamp(svg_lum(unpremultiply(fg)) * fg.a, 0.0, 1.0);
                        rgba[i] = bg * luminance;
                    } else {
                        rgba[i] = blend_mix_compose(bg, fg, end_clip.blend);
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_JUMP: {
                cmd_ix = ptcl[cmd_ix + 1u];
            }
            case CMD_LIN_GRAD: {
                let lin = read_lin_grad(cmd_ix);
                let draw_flags = info[ptcl[cmd_ix + 2u] - 1u];
                let d = lin.line_x * (xy.x + 0.5) + lin.line_y * (xy.y + 0.5) + lin.line_c;
                for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                    if area[i] != 0.0 {
                        let my_d = d + lin.line_x * f32(i);
                        let x = i32(round(extend_mode_normalized(my_d, lin.extend_mode) * f32(GRADIENT_WIDTH - 1)));
                        let fg_rgba = textureLoad(gradients, vec2(x, i32(lin.index)), 0);
                        let fg_i = fg_rgba * area[i];
                        rgba[i] = compose_draw(rgba[i], fg_i, draw_flags);
                    }
                }
                cmd_ix += 3u;
            }
            case CMD_RAD_GRAD: {
                let rad = read_rad_grad(cmd_ix);
                let draw_flags = info[ptcl[cmd_ix + 2u] - 1u];
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
                        let fg_i = fg_rgba * area[i];
                        rgba[i] = compose_draw(rgba[i], fg_i, draw_flags);
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
                        if radius == radius {
                            let t = extend_mode_normalized(radius, elliptic.extend_mode);
                            let ramp_x = i32(round(t * f32(GRADIENT_WIDTH - 1)));
                            let fg_rgba = textureLoad(gradients, vec2(ramp_x, i32(elliptic.index)), 0);
                            let fg_i = fg_rgba * area[i];
                            rgba[i] = compose_draw(rgba[i], fg_i, draw_flags);
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
                        let fg_i = fg_rgba * area[i];
                        rgba[i] = compose_draw(rgba[i], fg_i, draw_flags);
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
                        let fg_i = fg_rgba * area[i];
                        rgba[i] = compose_draw(rgba[i], fg_i, path_grad.draw_flags);
                    }
                }
                cmd_ix += 5u;
            }
            case CMD_IMAGE: {
                let image = read_image(cmd_ix);
                let draw_flags = info[ptcl[cmd_ix + 1u] - 1u];
                let atlas_max = image.atlas_offset + image.extents - vec2(1.0);
                switch image.quality {
                    case IMAGE_QUALITY_LOW: {
                        for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                            // We only need to load from the textures if the value will be used.
                            if area[i] != 0.0 {
                                let my_xy = vec2(xy.x + f32(i), xy.y);
                                var atlas_uv = image.matrx.xy * my_xy.x + image.matrx.zw * my_xy.y + image.xlat;
                                let atlas_ix = image_extend_mode_i32(atlas_uv.x, image.x_extend_mode, i32(image.extents.x));
                                let atlas_iy = image_extend_mode_i32(atlas_uv.y, image.y_extend_mode, i32(image.extents.y));
                                let atlas_uv_clamped = vec2<i32>(i32(image.atlas_offset.x) + atlas_ix, i32(image.atlas_offset.y) + atlas_iy);
                                // Nearest neighbor sampling
                                let fg_rgba = maybe_premul_alpha(textureLoad(image_atlas, atlas_uv_clamped, 0), image.alpha_type);
                                let fg_i = pixel_format(fg_rgba * area[i] * image.alpha, image.format);
                                rgba[i] = compose_draw(rgba[i], fg_i, draw_flags);
                            }
                        }
                    }
                    case IMAGE_QUALITY_MEDIUM, default: {
                        // We don't have an implementation for `IMAGE_QUALITY_HIGH` yet, just use the same as medium
                        for (var i = 0u; i < PIXELS_PER_THREAD; i += 1u) {
                            // We only need to load from the textures if the value will be used.
                            if area[i] != 0.0 {
                                let my_xy = vec2(xy.x + f32(i), xy.y);
                                var atlas_uv = image.matrx.xy * my_xy.x + image.matrx.zw * my_xy.y + image.xlat;
                                atlas_uv.x = image_extend_mode(atlas_uv.x, image.x_extend_mode, image.extents.x);
                                atlas_uv.y = image_extend_mode(atlas_uv.y, image.y_extend_mode, image.extents.y);
                                atlas_uv = atlas_uv + image.atlas_offset - vec2(0.5);
                                // TODO: If the image couldn't be added to the atlas (i.e. was too big), this isn't robust
                                let atlas_uv_clamped = clamp(atlas_uv, image.atlas_offset, atlas_max);
                                // We know that the floor and ceil are within the atlas area because atlas_max and
                                // atlas_offset are integers
                                let uv_quad = vec4(floor(atlas_uv_clamped), ceil(atlas_uv_clamped));
                                let uv_frac = fract(atlas_uv);
                                let a = maybe_premul_alpha(textureLoad(image_atlas, vec2<i32>(uv_quad.xy), 0), image.alpha_type);
                                let b = maybe_premul_alpha(textureLoad(image_atlas, vec2<i32>(uv_quad.xw), 0), image.alpha_type);
                                let c = maybe_premul_alpha(textureLoad(image_atlas, vec2<i32>(uv_quad.zy), 0), image.alpha_type);
                                let d = maybe_premul_alpha(textureLoad(image_atlas, vec2<i32>(uv_quad.zw), 0), image.alpha_type);
                                // Bilinear sampling
                                let fg_rgba = mix(mix(a, b, uv_frac.y), mix(c, d, uv_frac.y), uv_frac.x);
                                let fg_i = pixel_format(fg_rgba * area[i] * image.alpha, image.format);
                                rgba[i] = compose_draw(rgba[i], fg_i, draw_flags);
                            }
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
            // let fg = base_color * (1.0 - foreground.a) + foreground;
            // Max with a small epsilon to avoid NaNs
            let a_inv = 1.0 / max(fg.a, 1e-6);
            let rgba_sep = vec4(fg.rgb * a_inv, fg.a);
            textureStore(output, vec2<i32>(coords), rgba_sep);
        }
    } 
}

fn premul_alpha(rgba: vec4<f32>) -> vec4<f32> {
    return vec4(rgba.rgb * rgba.a, rgba.a);
}
