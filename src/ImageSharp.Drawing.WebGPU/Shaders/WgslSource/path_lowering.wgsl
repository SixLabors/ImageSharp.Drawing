// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Path-lowering stage. Fill paths already contain final line segments, which this stage transforms
// and writes to LineSoup. Stroke paths contain centre lines; this stage expands them into
// the same joins, miters, arcs, and caps as the CPU stroker. It also calculates each path's bounds.
// No curve subdivision occurs here.
//
// Inputs: configuration, encoded scene data, and path-tag prefix sums.
// Outputs: path bounds, line allocation counts, and final device-space lines.

#import config
#import drawtag
#import pathtag
#import segment
#import bump

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage> scene: array<u32>;

@group(0) @binding(2)
var<storage> tag_monoids: array<TagMonoid>;

// Per-path device-space bounding box plus draw metadata copied from the style stream. The extents
// are atomic so the many invocations lowering segments of the same path can merge their results.
struct AtomicPathBbox {
    x0: atomic<i32>,
    y0: atomic<i32>,
    x1: atomic<i32>,
    y1: atomic<i32>,
    draw_flags: u32,
    trans_ix: u32,
    // Must mirror PathBbox in bbox.wgsl: both views alias the same buffer, so the strides must match.
    coverage_data: f32,
    _padding: u32,
    interest: vec4<f32>,
}

@group(0) @binding(3)
var<storage, read_write> path_bboxes: array<AtomicPathBbox>;

@group(0) @binding(4)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(5)
var<storage, read_write> lines: array<LineSoup>;

// A tangent shorter than this value has no stable direction for a join or cap. The encoder removes
// normal micro-segments; this value remains as the stroker's division-by-zero limit.
const TANGENT_THRESH: f32 = 1e-6;

// ---- CPU stroker port ----
//
// The stroke geometry below is a direct port of the CPU backend's stroker
// (DefaultRasterizer.StrokeLinearizer, itself a port of PolygonStroker) so the GPU emits the
// same outline vertices: identical joins, miters, arcs, and caps. Emission is chained: each
// appended point writes one line from the previously appended point, so the per-vertex join
// connectors splice exactly into the per-segment offset edges. Overshoots along a shared offset
// line cancel in the signed winding sum, reproducing the CPU's trimmed outline coverage.

// Appends `p` to the outline chain, writing a line from the previous chain point (`last`) and
// advancing it. Coincident points are skipped so the chain never emits zero-length lines.
fn stroke_chain_point(path_ix: u32, last: ptr<function, vec2f>, p: vec2f, transform: Transform) {
    if all(*last == p) {
        return;
    }
    output_line_with_transform(path_ix, *last, p, transform);
    *last = p;
}

// Port of the CPU stroker's NormalizePositiveAngle: wraps into [0, 2*PI).
fn stroke_normalize_positive_angle(angle: f32) -> f32 {
    let full_turn = 6.283185307179586;
    var a = angle - (full_turn * floor(angle / full_turn));
    if a < 0.0 {
        a += full_turn;
    }
    if a >= full_turn {
        a -= full_turn;
    }
    return a;
}

// Port of the CPU stroker's GetArcSubdivisionCount (round cap tessellation): returns the number
// of interior vertices needed to keep the arc's chordal error within the arc detail scale.
fn stroke_arc_subdivision_count(radius: f32, sweep: f32, arc_detail_scale: f32) -> u32 {
    let safe_radius = max(radius, TANGENT_THRESH);
    let safe_scale = max(arc_detail_scale, 0.01);
    let ratio = clamp(safe_radius / (safe_radius + (0.125 / safe_scale)), -1.0, 1.0);
    let theta = acos(ratio) * 2.0;
    if theta <= 0.0 {
        return 0u;
    }
    // Bounded for GPU safety; far above any real tessellation demand.
    return min(u32(max(sweep / theta, 0.0)), 1024u);
}

// Port of the CPU stroker's AppendDirectedArcContour (round caps): steps through the positive
// angular sweep from center + from_offset to center + to_offset. The chain is assumed to already
// sit at center + from_offset; only interior and end points are appended.
fn stroke_chain_arc(
    path_ix: u32, last: ptr<function, vec2f>,
    center: vec2f, from_offset: vec2f, to_offset: vec2f,
    arc_detail_scale: f32, transform: Transform,
) {
    let radius = length(from_offset);
    if radius <= TANGENT_THRESH {
        stroke_chain_point(path_ix, last, center + to_offset, transform);
        return;
    }
    // WGSL atan2 is implementation-defined when x is zero, and axis-aligned caps produce
    // exactly those inputs. The sweep must come from atan2(cross, dot) and interior points
    // must rotate the start offset; absolute angles of the offsets are not computable here.
    let cross_ft = (from_offset.x * to_offset.y) - (from_offset.y * to_offset.x);
    let sweep = stroke_normalize_positive_angle(atan2(cross_ft, dot(from_offset, to_offset)));
    let n = stroke_arc_subdivision_count(radius, sweep, arc_detail_scale);
    let step = sweep / f32(n + 1u);
    let rot_c = cos(step);
    let rot_s = sin(step);
    var offset = from_offset;
    for (var i = 1u; i <= n; i += 1u) {
        offset = vec2((offset.x * rot_c) - (offset.y * rot_s), (offset.x * rot_s) + (offset.y * rot_c));
        stroke_chain_point(path_ix, last, center + offset, transform);
    }
    stroke_chain_point(path_ix, last, center + to_offset, transform);
}

// Port of PolygonStroker.CalcArc (round joins): sweeps from offset o1 to offset o2 around corner
// v1 with a fixed angular step derived from the arc detail scale. Both endpoints are appended,
// so the caller's chain need not already sit on the arc.
fn stroke_calc_arc(
    path_ix: u32, last: ptr<function, vec2f>,
    v1: vec2f, o1: vec2f, o2: vec2f,
    half_width: f32, arc_detail_scale: f32, transform: Transform,
) {
    // WGSL atan2 is implementation-defined when x is zero, and axis-aligned joins produce
    // exactly those inputs. The sweep must come from atan2(cross, dot) and interior points
    // must rotate o1; absolute angles of the offsets are not computable here.
    let cross_oo = (o1.x * o2.y) - (o1.y * o2.x);
    let sweep = stroke_normalize_positive_angle(atan2(cross_oo, dot(o1, o2)));
    let da = acos(half_width / (half_width + (0.125 / arc_detail_scale))) * 2.0;
    stroke_chain_point(path_ix, last, v1 + o1, transform);
    // Bounded for GPU safety; matches the CPU count for all real detail scales.
    let n = clamp(i32(sweep / da), 0, 1024);
    let step = sweep / f32(n + 1);
    let rot_c = cos(step);
    let rot_s = sin(step);
    var offset = o1;
    for (var i = 0; i < n; i += 1) {
        offset = vec2((offset.x * rot_c) - (offset.y * rot_s), (offset.x * rot_s) + (offset.y * rot_c));
        stroke_chain_point(path_ix, last, v1 + offset, transform);
    }
    stroke_chain_point(path_ix, last, v1 + o2, transform);
}

// Port of PolygonStroker.CrossProduct: signed area of triangle (a, b, p), used as a
// side-of-line test for p against the line through a and b.
fn stroke_cross_product(a: vec2f, b: vec2f, p: vec2f) -> f32 {
    return ((p.x - b.x) * (b.y - a.y)) - ((p.y - b.y) * (b.x - a.x));
}

// Port of PolygonStroker.TryCalcIntersection: infinite line intersection of (a, b) and (c, d).
// Returns false for near-parallel lines; `hit` is written only on success.
fn stroke_try_intersect(a: vec2f, b: vec2f, c: vec2f, d: vec2f, hit: ptr<function, vec2f>) -> bool {
    let ab = b - a;
    let cd = d - c;
    let denominator = (ab.x * cd.y) - (ab.y * cd.x);
    if abs(denominator) < 1e-7 {
        return false;
    }
    let ca = c - a;
    let t = ((ca.x * cd.y) - (ca.y * cd.x)) / denominator;
    *hit = a + (ab * t);
    return true;
}

// Port of PolygonStroker.CalcMiter: appends the miter apex at corner v1 between segments
// (v0 -> v1) and (v1 -> v2) with offset vectors o1/o2, or the configured fallback when the apex
// exceeds half_width * miter_limit. `overflow_mode`: 0 = clip, 1 = revert, 2 = round.
// `bevel_distance` is the distance from v1 to the bevel midpoint, used to interpolate the
// clipped miter.
fn stroke_calc_miter(
    path_ix: u32, last: ptr<function, vec2f>,
    v0: vec2f, v1: vec2f, v2: vec2f,
    o1: vec2f, o2: vec2f,
    overflow_mode: u32, miter_limit: f32, bevel_distance: f32,
    half_width: f32, arc_detail_scale: f32, transform: Transform,
) {
    var xi = v1;
    var intersection_distance = 1.0;
    let limit = half_width * miter_limit;
    var miter_limit_exceeded = true;
    var intersection_failed = true;
    var hit = vec2(0.0);

    if stroke_try_intersect(v0 + o1, v1 + o1, v1 + o2, v2 + o2, &hit) {
        xi = hit;
        intersection_distance = distance(v1, hit);
        if intersection_distance <= limit {
            stroke_chain_point(path_ix, last, hit, transform);
            miter_limit_exceeded = false;
        }
        intersection_failed = false;
    } else {
        // Parallel/near-parallel fallback: probe a candidate offset point.
        let probe = v1 + o1;
        if (stroke_cross_product(v0, v1, probe) < 0.0) == (stroke_cross_product(v1, v2, probe) < 0.0) {
            stroke_chain_point(path_ix, last, probe, transform);
            miter_limit_exceeded = false;
        }
    }

    if !miter_limit_exceeded {
        return;
    }

    switch overflow_mode {
        case 1u: {
            stroke_chain_point(path_ix, last, v1 + o1, transform);
            stroke_chain_point(path_ix, last, v1 + o2, transform);
        }
        case 2u: {
            stroke_calc_arc(path_ix, last, v1, o1, o2, half_width, arc_detail_scale, transform);
        }
        default: {
            if intersection_failed {
                // No reliable apex: project a clipped bevel from local tangent/perpendicular vectors.
                stroke_chain_point(path_ix, last, v1 + o1 + (miter_limit * vec2(-o1.y, o1.x)), transform);
                stroke_chain_point(path_ix, last, v1 + o2 - (miter_limit * vec2(-o2.y, o2.x)), transform);
            } else {
                let q1 = v1 + o1;
                let q2 = v1 + o2;
                let ratio = (limit - bevel_distance) / (intersection_distance - bevel_distance);
                stroke_chain_point(path_ix, last, q1 + ((xi - q1) * ratio), transform);
                stroke_chain_point(path_ix, last, q2 + ((xi - q2) * ratio), transform);
            }
        }
    }
}

// Port of PolygonStroker.CalcJoin (the CPU stroker's AppendSideJoinContour): emits one side's
// join point sequence at corner v1 between segments (v0 -> v1, len1) and (v1 -> v2, len2).
fn stroke_side_join(
    path_ix: u32, last: ptr<function, vec2f>,
    style_flags: u32, miter_limit: f32, arc_detail_scale: f32, half_width: f32,
    v0: vec2f, v1: vec2f, v2: vec2f,
    len1: f32, len2: f32,
    transform: Transform,
) {
    let eps = TANGENT_THRESH;
    if len1 < eps || len2 < eps {
        // Degenerate neighborhood: fall back to best available segment direction.
        let l1 = select(len2, len1, len1 >= eps);
        let l2 = select(len1, len2, len2 >= eps);
        let seg1 = v1 - v0;
        let seg2 = v2 - v1;
        let d1 = vec2(seg1.y, -seg1.x) * (half_width / l1);
        let d2 = vec2(seg2.y, -seg2.x) * (half_width / l2);
        stroke_chain_point(path_ix, last, v1 + d1, transform);
        stroke_chain_point(path_ix, last, v1 + d2, transform);
        return;
    }

    let seg_forward = v1 - v0;
    let seg_next = v2 - v1;
    let o1 = vec2(seg_forward.y, -seg_forward.x) * (half_width / len1);
    let o2 = vec2(seg_next.y, -seg_next.x) * (half_width / len2);
    let cp = (seg_next.x * seg_forward.y) - (seg_next.y * seg_forward.x);

    if cp > 0.0 {
        // Inner corner: miter to the offset-line intersection, reverting past the local limit.
        var limit = min(len1, len2) / half_width;
        if limit < 1.01 {
            limit = 1.01;
        }
        stroke_calc_miter(path_ix, last, v0, v1, v2, o1, o2, 1u, limit, 0.0, half_width, arc_detail_scale, transform);
        return;
    }

    // Outer corner.
    let bevel_distance = length((o1 + o2) * 0.5);
    let join_style = style_flags & STYLE_FLAGS_JOIN_MASK;
    if (join_style == STYLE_FLAGS_JOIN_ROUND || join_style == STYLE_FLAGS_JOIN_BEVEL)
        && ((arc_detail_scale * (half_width - bevel_distance)) < (half_width / 1024.0))
    {
        var hit = vec2(0.0);
        if stroke_try_intersect(v0 + o1, v1 + o1, v1 + o2, v2 + o2, &hit) {
            stroke_chain_point(path_ix, last, hit, transform);
        } else {
            stroke_chain_point(path_ix, last, v1 + o1, transform);
        }
        return;
    }

    switch join_style {
        case STYLE_FLAGS_JOIN_MITER: {
            var overflow_mode = 0u;
            if (style_flags & STYLE_FLAGS_JOIN_MITER_REVERT) != 0u {
                overflow_mode = 1u;
            } else if (style_flags & STYLE_FLAGS_JOIN_MITER_ROUND) != 0u {
                overflow_mode = 2u;
            }
            stroke_calc_miter(path_ix, last, v0, v1, v2, o1, o2, overflow_mode, miter_limit, bevel_distance, half_width, arc_detail_scale, transform);
        }
        case STYLE_FLAGS_JOIN_ROUND: {
            stroke_calc_arc(path_ix, last, v1, o1, o2, half_width, arc_detail_scale, transform);
        }
        default: {
            stroke_chain_point(path_ix, last, v1 + o1, transform);
            stroke_chain_point(path_ix, last, v1 + o2, transform);
        }
    }
}

// Port of the CPU stroker's cap emission: butt connects the offset ends, square extends them by
// the tangent before connecting, round steps the CPU cap arc. The chain runs cap0 -> cap1 so
// caps splice into the offset edges with consistent winding.
fn draw_cap(
    path_ix: u32, cap_style: u32, arc_detail_scale: f32, point: vec2f,
    cap0: vec2f, cap1: vec2f, offset_tangent: vec2f,
    transform: Transform,
) {
    var last = cap0;
    if cap_style == STYLE_FLAGS_CAP_ROUND {
        stroke_chain_arc(path_ix, &last, point, cap0 - point, cap1 - point, arc_detail_scale, transform);
        return;
    }

    if cap_style == STYLE_FLAGS_CAP_SQUARE {
        stroke_chain_point(path_ix, &last, cap0 + offset_tangent, transform);
        stroke_chain_point(path_ix, &last, cap1 + offset_tangent, transform);
    }
    stroke_chain_point(path_ix, &last, cap1, transform);
}

// Emits both join connectors at corner v1, splicing the per-segment offset edges into the CPU
// stroker's outline: the forward side walks (v0, v1, v2) and the reverse side walks (v2, v1, v0),
// exactly like the CPU stroker's two contour passes over the same corner.
fn draw_join(
    path_ix: u32, style_flags: u32, miter_limit: f32, arc_detail_scale: f32, half_width: f32,
    v0: vec2f, v1: vec2f, v2: vec2f,
    len1: f32, len2: f32,
    n_prev: vec2f, n_next: vec2f,
    transform: Transform,
) {
    var last = v1 + n_prev;
    stroke_side_join(path_ix, &last, style_flags, miter_limit, arc_detail_scale, half_width, v0, v1, v2, len1, len2, transform);
    stroke_chain_point(path_ix, &last, v1 + n_next, transform);

    last = v1 - n_next;
    stroke_side_join(path_ix, &last, style_flags, miter_limit, arc_detail_scale, half_width, v2, v1, v0, len2, len1, transform);
    stroke_chain_point(path_ix, &last, v1 - n_prev, transform);
}

// Reads a point stored as two f32 words at offset `ix` in the path data stream.
fn read_f32_point(ix: u32) -> vec2f {
    let x = bitcast<f32>(scene[pathdata_base + ix]);
    let y = bitcast<f32>(scene[pathdata_base + ix + 1u]);
    return vec2(x, y);
}

// Reads a point packed as two signed 16-bit values in a single word at offset `ix` in the path
// data stream (x in the low half, y in the high half).
fn read_i16_point(ix: u32) -> vec2f {
    let raw = scene[pathdata_base + ix];
    let x = f32(i32(raw << 16u) >> 16u);
    let y = f32(i32(raw) >> 16u);
    return vec2(x, y);
}

// A 2D transform: 2x2 matrix (columns (mat.x, mat.y) and (mat.z, mat.w)) plus translation,
// extended with a perspective row to support projective transforms. Local divergence from Vello,
// whose transforms are affine only.
struct Transform {
    mat: vec4f,
    translate: vec2f,
    perspective: vec3f,
}

// Reads transform `ix` from the scene stream: 9 words covering the 2x2 matrix, translation, and
// perspective row. Local divergence: Vello stores 6-word affine transforms.
fn read_transform(transform_base: u32, ix: u32) -> Transform {
    let base = transform_base + ix * 9u;
    let mat = vec4(
        bitcast<f32>(scene[base]),
        bitcast<f32>(scene[base + 1u]),
        bitcast<f32>(scene[base + 2u]),
        bitcast<f32>(scene[base + 3u]));
    let translate = vec2(
        bitcast<f32>(scene[base + 4u]),
        bitcast<f32>(scene[base + 5u]));
    let perspective = vec3(
        bitcast<f32>(scene[base + 6u]),
        bitcast<f32>(scene[base + 7u]),
        bitcast<f32>(scene[base + 8u]));
    return Transform(mat, translate, perspective);
}

// Applies the projective transform to point p. The homogeneous divisor is clamped away from
// zero so points at or behind the horizon cannot produce infinities or NaNs.
fn transform_apply(transform: Transform, p: vec2f) -> vec2f {
    let px = fma(transform.mat.x, p.x, fma(transform.mat.z, p.y, transform.translate.x));
    let py = fma(transform.mat.y, p.x, fma(transform.mat.w, p.y, transform.translate.y));
    let w = fma(transform.perspective.x, p.x, fma(transform.perspective.y, p.y, transform.perspective.z));
    return vec2(px, py) / max(w, 0.0000001);
}

// Floors to i32; used for the conservative lower bounds of the path bbox.
fn round_down(x: f32) -> i32 {
    return i32(floor(x));
}

// Ceils to i32; used for the conservative upper bounds of the path bbox.
fn round_up(x: f32) -> i32 {
    return i32(ceil(x));
}

// A path tag byte paired with its exclusive-prefix tag monoid (running stream offsets).
struct PathTagData {
    tag_byte: u32,
    monoid: TagMonoid,
}

// Computes the exclusive-prefix tag monoid for path tag index `ix` by combining the workgroup
// prefix from tag_monoids (produced by pathtag_reduce/scan) with a local reduction of the
// preceding bytes in the same tag word.
fn compute_tag_monoid(ix: u32) -> PathTagData {
    let tag_word = scene[config.pathtag_base + (ix >> 2u)];
    let shift = (ix & 3u) * 8u;
    var tm = reduce_tag(tag_word & ((1u << shift) - 1u));
    // TODO: this can be a read buf overflow. Conditionalize by tag byte?
    tm = combine_tag_monoid(tag_monoids[ix >> 2u], tm);
    var tag_byte = (tag_word >> shift) & 0xffu;
    // The encoded streams begin after the implicit identity transform and current style,
    // so these indices are rebased to the actual payload starts.
    tm.trans_ix -= 1u;
    tm.style_ix -= STYLE_SIZE_IN_WORDS;
    return PathTagData(tag_byte, tm);
}

// Endpoints of one final line or one stroke tangent marker.
struct LinePoints {
    p0: vec2f,
    p1: vec2f,
}

// Reads one final line. An open-stroke cap marker uses a QuadTo tag only to select its four-word
// data layout. For that marker, the second and third encoded points define the starting tangent.
fn read_path_segment(tag: PathTagData, is_stroke: bool) -> LinePoints {
    var p0: vec2f;
    var p1: vec2f;

    let seg_type = tag.tag_byte & PATH_TAG_SEG_TYPE;
    let pathseg_offset = tag.monoid.pathseg_offset;
    let is_stroke_cap_marker = is_stroke && (tag.tag_byte & PATH_TAG_SUBPATH_END) != 0u;
    let is_open = seg_type == PATH_TAG_QUADTO;
    let carries_start_tangent = is_stroke_cap_marker && is_open;

    if (tag.tag_byte & PATH_TAG_F32) != 0u {
        p0 = read_f32_point(pathseg_offset);
        p1 = read_f32_point(pathseg_offset + 2u);

        if carries_start_tangent {
            p0 = p1;
            p1 = read_f32_point(pathseg_offset + 4u);
        }
    } else {
        p0 = read_i16_point(pathseg_offset);
        p1 = read_i16_point(pathseg_offset + 1u);

        if carries_start_tangent {
            p0 = p1;
            p1 = read_i16_point(pathseg_offset + 2u);
        }
    }

    return LinePoints(p0, p1);
}

// Half-width of the band that snap_to_pixel_grid treats as an exact integer coordinate. The value
// is below visible pixel precision but above the floating-point differences created when separate
// stroke operations calculate a shared endpoint.
const PIXEL_GRID_SNAP_EPSILON: f32 = 1e-3;

// Snaps coordinates lying within PIXEL_GRID_SNAP_EPSILON of an integer device coordinate onto
// that integer. The counting stages resolve grid ties with exact comparisons (the boundary
// horizontal cull and the start-on-boundary backdrop bump in path_count pair up, as do the
// left-edge y_edge assignments in path_tiling); those pairings only hold when every endpoint
// meeting a boundary agrees on whether it lies exactly on it. Separate stroke calculations can
// otherwise produce slightly different values for one shared endpoint and change a tile row's
// winding.
fn snap_to_pixel_grid(p: vec2f) -> vec2f {
    let r = round(p);
    return select(p, r, abs(p - r) < vec2(PIXEL_GRID_SNAP_EPSILON));
}

// Writes a line into the `lines` buffer at the pre-allocated slot `line_ix` and grows this
// invocation's running bbox. Out-of-range writes are dropped (the bump counter may legitimately
// exceed the buffer size).
fn write_line(line_ix: u32, path_ix: u32, p0: vec2f, p1: vec2f) {
    let s0 = snap_to_pixel_grid(p0);
    let s1 = snap_to_pixel_grid(p1);
    bbox = vec4(min(bbox.xy, min(s0, s1)), max(bbox.zw, max(s0, s1)));

    if line_ix < config.lines_size {
        lines[line_ix] = LineSoup(path_ix, profile_tag, s0, s1);
    }
}

// Transforms both endpoints into device space and writes the line at slot `line_ix`.
fn write_line_with_transform(line_ix: u32, path_ix: u32, p0: vec2f, p1: vec2f, t: Transform) {
    let tp0 = transform_apply(t, p0);
    let tp1 = transform_apply(t, p1);
    write_line(line_ix, path_ix, tp0, tp1);
}

// Bump-allocates one slot in the lines buffer and writes the transformed line into it.
fn output_line_with_transform(path_ix: u32, p0: vec2f, p1: vec2f, transform: Transform) {
    let line_ix = atomicAdd(&bump.lines, 1u);
    write_line_with_transform(line_ix, path_ix, p0, p1, transform);
}

// Bump-allocates two consecutive slots in the lines buffer and writes both transformed lines.
fn output_two_lines_with_transform(
    path_ix: u32,
    p00: vec2f, p01: vec2f,
    p10: vec2f, p11: vec2f,
    transform: Transform
) {
    let line_ix = atomicAdd(&bump.lines, 2u);
    write_line_with_transform(line_ix, path_ix, p00, p01, transform);
    write_line_with_transform(line_ix + 1u, path_ix, p10, p11, transform);
}

// Join information for the segment following the current one: whether to draw a join (versus an
// end cap), its chord length, and its start tangent. The `length` field is a local addition for
// the CPU stroker port, which needs neighbor segment lengths for the inner-corner miter limit.
struct NeighboringSegment {
    do_join: bool,

    length: f32,
    tangent: vec2f,
}

// Reads the segment at tag index `ix` (the segment following the current one) and derives its
// join information. A cap marker encoding a closed subpath still joins back to the start; only
// open subpath cap markers suppress the join in favor of an end cap.
fn read_neighboring_segment(ix: u32) -> NeighboringSegment {
    let tag = compute_tag_monoid(ix);
    let pts = read_path_segment(tag, true);

    let is_closed = (tag.tag_byte & PATH_TAG_SEG_TYPE) == PATH_TAG_LINETO;
    let is_stroke_cap_marker = (tag.tag_byte & PATH_TAG_SUBPATH_END) != 0u;
    let do_join = !is_stroke_cap_marker || is_closed;
    let tangent = pts.p1 - pts.p0;
    return NeighboringSegment(do_join, length(tangent), tangent);
}

// `pathdata_base` is decoded once and reused by helpers above.
var<private> pathdata_base: u32;

// X and Y profile identifiers for the current fill segment. Stroke segments and fills without
// profile data use the all-ones sentinel.
var<private> profile_tag: u32 = PROFILE_TAG_SENTINEL;

// Device-space bounds of the lines written by this invocation.
var<private> bbox: vec4f;

// One invocation processes one path tag. A segment tag emits one fill line or one part of a stroke
// outline. A Path tag writes metadata for the path. At the end, each segment invocation merges its
// line bounds into the path's shared bounds.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
    @builtin(local_invocation_id) local_id: vec3<u32>,
) {
    let ix = global_id.x;
    pathdata_base = config.pathdata_base;
    bbox = vec4(1e31, 1e31, -1e31, -1e31);

    let tag = compute_tag_monoid(ix);
    let path_ix = tag.monoid.path_ix;
    let style_ix = tag.monoid.style_ix;
    let trans_ix = tag.monoid.trans_ix;

    let out = &path_bboxes[path_ix];
    // Style stream layout (words): 0 flags, 1 line width, 2 draw flags, 3 miter limit,
    // 4 arc detail scale, 5 antialiased coverage adjustment, 6..9 interest rect.
    let style_flags = scene[config.style_base + style_ix];
    let style_draw_flags = scene[config.style_base + style_ix + 2u];
    let coverage_data = bitcast<f32>(scene[config.style_base + style_ix + 5u]);
    let style_interest = vec4<f32>(
        bitcast<f32>(scene[config.style_base + style_ix + 6u]),
        bitcast<f32>(scene[config.style_base + style_ix + 7u]),
        bitcast<f32>(scene[config.style_base + style_ix + 8u]),
        bitcast<f32>(scene[config.style_base + style_ix + 9u]));
    // The fill bit is always zero for strokes, which selects the non-zero fill rule.
    let fill_rule = select(DRAW_INFO_FLAGS_FILL_RULE_BIT, 0u, (style_flags & STYLE_FLAGS_FILL) == 0u);
    let draw_flags = style_draw_flags | fill_rule;
    if (tag.tag_byte & PATH_TAG_PATH) != 0u {
        (*out).draw_flags = draw_flags;
        (*out).trans_ix = trans_ix;
        (*out).coverage_data = coverage_data;
        (*out).interest = style_interest;
    }
    // Decode path data
    let seg_type = tag.tag_byte & PATH_TAG_SEG_TYPE;
    if seg_type != 0u {
        let is_stroke = (style_flags & STYLE_FLAGS_STYLE) != 0u;
        let transform = read_transform(config.transform_base, trans_ix);
        let pts = read_path_segment(tag, is_stroke);

        if is_stroke {
            let linewidth = bitcast<f32>(scene[config.style_base + style_ix + 1u]);
            let half_width = 0.5 * linewidth;
            let miter_limit = bitcast<f32>(scene[config.style_base + style_ix + 3u]);
            let arc_detail_scale = bitcast<f32>(scene[config.style_base + style_ix + 4u]);

            let is_open = (tag.tag_byte & PATH_TAG_SEG_TYPE) != PATH_TAG_LINETO;
            let is_stroke_cap_marker = (tag.tag_byte & PATH_TAG_SUBPATH_END) != 0u;
            if is_stroke_cap_marker {
                if is_open {
                    // Start cap. The marker carries the first segment's tangent; the CPU stroker's
                    // caps chain from cap0 to cap1 so they splice into the offset edges.
                    var tangent = pts.p1 - pts.p0;
                    if dot(tangent, tangent) < TANGENT_THRESH * TANGENT_THRESH {
                        tangent = vec2(TANGENT_THRESH, 0.);
                    }
                    let offset_tangent = half_width * normalize(tangent);
                    let n = vec2(offset_tangent.y, -offset_tangent.x);
                    draw_cap(path_ix, (style_flags & STYLE_FLAGS_START_CAP_MASK) >> 2u, arc_detail_scale,
                             pts.p0, pts.p0 - n, pts.p0 + n, -offset_tangent, transform);
                } else {
                    // Don't draw anything if the path is closed.
                }
            } else {
                // CPU stroker port: stroke centerline segments are always straight lines, so the
                // offset edges are straight offset lines; joins and caps carry all the curvature.
                // The encoder collapses micro-segments (1/64 px) exactly like the CPU stroker.
                let p0 = pts.p0;
                let p1 = pts.p1;
                let seg = p1 - p0;
                let len1 = length(seg);
                if len1 >= TANGENT_THRESH {
                    let tangent = seg / len1;
                    let n_prev = half_width * vec2(tangent.y, -tangent.x);

                    // Body offset edges; the reverse-side edge is emitted backwards so the outline
                    // keeps a consistent ring winding.
                    output_two_lines_with_transform(path_ix, p0 + n_prev, p1 + n_prev, p1 - n_prev, p0 - n_prev, transform);

                    let neighbor = read_neighboring_segment(ix + 1u);
                    if neighbor.do_join {
                        var tan_next = neighbor.tangent;
                        if dot(tan_next, tan_next) < TANGENT_THRESH * TANGENT_THRESH {
                            tan_next = vec2(TANGENT_THRESH, 0.);
                        }
                        let next_dir = normalize(tan_next);
                        let len2 = neighbor.length;
                        let v2 = p1 + (next_dir * max(len2, TANGENT_THRESH));
                        let n_next = half_width * vec2(next_dir.y, -next_dir.x);
                        draw_join(path_ix, style_flags, miter_limit, arc_detail_scale, half_width,
                                  p0, p1, v2, len1, len2, n_prev, n_next, transform);
                    } else {
                        // End cap.
                        draw_cap(path_ix, (style_flags & STYLE_FLAGS_END_CAP_MASK), arc_detail_scale,
                                 p1, p1 + n_prev, p1 - n_prev, tangent * half_width, transform);
                    }
                }
            }
        } else {
            // Each final fill segment starts at one pair of path-data words. The same pair index
            // selects its profile tag. Add pathdata_base because a large scene can dispatch only a
            // range of its path tags while all ranges still share one profile-tag table.
            if config.profile_slots_base != 0u {
                let slot = config.profile_slots_base + ((config.pathdata_base + tag.monoid.pathseg_offset) >> 1u);
                profile_tag = scene[slot];
            }

            // Transform the final endpoints directly. Reject a zero-length result before it uses a
            // line slot or changes the path bounds.
            let p0 = transform_apply(transform, pts.p0);
            let p1 = transform_apply(transform, pts.p1);
            if any(p0 != p1) {
                let line_ix = atomicAdd(&bump.lines, 1u);
                write_line(line_ix, path_ix, p0, p1);
            }
        }
        // Update bounding box using atomics only. Computing a monoid is a
        // potential future optimization.
        if bbox.z > bbox.x || bbox.w > bbox.y {
            atomicMin(&(*out).x0, round_down(bbox.x));
            atomicMin(&(*out).y0, round_down(bbox.y));
            atomicMax(&(*out).x1, round_up(bbox.z));
            atomicMax(&(*out).y1, round_up(bbox.w));
        }
    }
}
