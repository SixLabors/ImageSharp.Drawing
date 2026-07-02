// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Flatten stage: converts encoded path segments into a device-space "line soup" and accumulates
// per-path bounding boxes for the downstream draw_leaf/binning/coarse/fine stages. Inputs are the
// scene stream (path tags, points, styles, transforms) at binding 1, the config uniform at
// binding 0, and the pathtag prefix sums at binding 2; outputs are the atomic path bboxes
// (binding 3), the bump allocators (binding 4), and the LineSoup buffer (binding 5). Ported from
// Vello's flatten.wgsl. Fills of every segment type route through flatten_euler exactly as in
// upstream Vello: read_path_segment lowers lines to degenerate cubics and the Euler-spiral math
// resolves them through its low-curvature path. The C# encoder currently pre-flattens fill curves
// on the CPU, so in practice only line segments arrive for fills. Stroke expansion diverges from
// upstream: it is a port of the CPU PolygonStroker (chained joins, miters, arcs, and caps) with
// quadto tags reused as stroke tangent markers rather than curves.

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
// are atomic so the many invocations flattening segments of the same path can merge their results.
struct AtomicPathBbox {
    x0: atomic<i32>,
    y0: atomic<i32>,
    x1: atomic<i32>,
    y1: atomic<i32>,
    draw_flags: u32,
    trans_ix: u32,
    // Must mirror PathBbox in bbox.wgsl: both views alias the same buffer, so the strides must match.
    coverage_threshold: f32,
    _padding: u32,
    interest: vec4<f32>,
}

@group(0) @binding(3)
var<storage, read_write> path_bboxes: array<AtomicPathBbox>;

@group(0) @binding(4)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(5)
var<storage, read_write> lines: array<LineSoup>;

// Functions for Euler spirals

// Geometric Hermite parameters of a curve segment: tangent angles at each endpoint measured
// relative to the chord, the chord length, and an estimate of the Euler-spiral fit error.
struct CubicParams {
    th0: f32,
    th1: f32,
    chord_len: f32,
    err: f32,
}

// Normalized Euler spiral parameters: th0 is the start tangent angle relative to the chord,
// k0 and k1 the curvature and its derivative, ch the chord length of the unit-arclength spiral.
struct EulerParams {
    th0: f32,
    // th1 need not be explicitly stored, as it can be derived from k0 - th0
    k0: f32,
    k1: f32,
    ch: f32,
}

// An Euler spiral segment between two endpoints with its normalized spiral parameters.
struct EulerSeg {
    p0: vec2f,
    p1: vec2f,
    params: EulerParams,
}

// Threshold below which a derivative is considered too small.
const DERIV_THRESH: f32 = 1e-6;
const DERIV_THRESH_SQUARED: f32 = DERIV_THRESH * DERIV_THRESH;
// Amount to nudge t when derivative is near-zero.
const DERIV_EPS: f32 = 1e-6;
// Limit for subdivision of cubic Beziers.
const SUBDIV_LIMIT: f32 = 1.0 / 65536.0;
// Robust ESPC computation: below this value, treat curve as circular arc
const K1_THRESH: f32 = 1e-3;
// Robust ESPC: below this value, evaluate ES rather than parallel curve
const DIST_THRESH: f32 = 1e-3;
// Threshold for tangents to be considered near zero length
const TANGENT_THRESH: f32 = 1e-6;

// Computes geometric Hermite parameters for a curve piece from its endpoints (p0, p1) and
// endpoint derivatives (q0, q1) over parameter width dt, including an error estimate for
// approximating the piece with a single Euler spiral segment.
fn cubic_from_points_derivs(p0: vec2f, p1: vec2f, q0: vec2f, q1: vec2f, dt: f32) -> CubicParams {
    let chord = p1 - p0;
    let chord_squared = dot(chord, chord);
    let chord_len = sqrt(chord_squared);
    if chord_squared < DERIV_THRESH_SQUARED {
        let chord_err = sqrt((9. / 32.0) * (dot(q0, q0) + dot(q1, q1))) * dt;
        return CubicParams(0.0, 0.0, DERIV_THRESH, chord_err);
    }
    let scale = dt / chord_squared;
    let h0 = vec2(q0.x * chord.x + q0.y * chord.y, q0.y * chord.x - q0.x * chord.y);
    let th0 = atan2(h0.y, h0.x);
    let d0 = length(h0) * scale;
    let h1 = vec2(q1.x * chord.x + q1.y * chord.y, q1.x * chord.y - q1.y * chord.x);
    let th1 = atan2(h1.y, h1.x);
    let d1 = length(h1) * scale;

    // Estimate error of geometric Hermite interpolation to Euler spiral.
    let cth0 = cos(th0);
    let cth1 = cos(th1);
    var err = 2.0;
    if cth0 * cth1 >= 0.0 {
        let e0 = (2. / 3.) / max(1.0 + cth0, 1e-9);
        let e1 = (2. / 3.) / max(1.0 + cth1, 1e-9);
        let s0 = sin(th0);
        let s1 = sin(th1);
        let s01 = cth0 * s1 + cth1 * s0;
        let amin = 0.15 * (2. * e0 * s0 + 2. * e1 * s1 - e0 * e1 * s01);
        let a = 0.15 * (2. * d0 * s0 + 2. * d1 * s1 - d0 * d1 * s01);
        let aerr = abs(a - amin);
        let symm = abs(th0 + th1);
        let asymm = abs(th0 - th1);
        let dist = length(vec2(d0 - e0, d1 - e1));
        let symm2 = symm * symm;
        let ctr = (4.625e-6 * symm * symm2 + 7.5e-3 * asymm) * symm2;
        let halo = (5e-3 * symm + 7e-2 * asymm) * dist;
        err = ctr + 1.55 * aerr + halo;
    }
    err *= chord_len;
    return CubicParams(th0, th1, chord_len, err);
}

// Solves for the Euler spiral whose endpoint tangent angles (relative to the chord) are th0 and
// th1, using polynomial approximations for the curvature terms and the normalized chord length.
fn es_params_from_angles(th0: f32, th1: f32) -> EulerParams {
    let k0 = th0 + th1;
    let dth = th1 - th0;
    let d2 = dth * dth;
    let k2 = k0 * k0;
    var a = 6.0;
    a -= d2 * (1. / 70.);
    a -= (d2 * d2) * (1. / 10780.);
    a += (d2 * d2 * d2) * 2.769178184818219e-07;
    let b = -0.1 + d2 * (1. / 4200.) + d2 * d2 * 1.6959677820260655e-05;
    let c = -1. / 1400. + d2 * 6.84915970574303e-05 - k2 * 7.936475029053326e-06;
    a += (b + c * k2) * k2;
    let k1 = dth * a;

    // calculation of chord
    var ch = 1.0;
    ch -= d2 * (1. / 40.);
    ch += (d2 * d2) * 0.00034226190482569864;
    ch -= (d2 * d2 * d2) * 1.9349474568904524e-06;
    let b_ = -1. / 24. + d2 * 0.0024702380951963226 - d2 * d2 * 3.7297408997537985e-05;
    let c_ = 1. / 1920. - d2 * 4.87350869747975e-05 - k2 * 3.1001936068463107e-06;
    ch += (b_ + c_ * k2) * k2;
    return EulerParams(th0, k0, k1, ch);
}

// Evaluates the tangent angle of the Euler spiral at parameter t, relative to the chord.
fn es_params_eval_th(params: EulerParams, t: f32) -> f32 {
    return (params.k0 + 0.5 * params.k1 * (t - 1.0)) * t - params.th0;
}

// Integrates the Euler spiral with curvature k0 and curvature derivative k1 over unit arclength,
// using a degree-10 polynomial expansion. Returns the endpoint displacement as (u, v).
fn integ_euler_10(k0: f32, k1: f32) -> vec2f {
    let t1_1 = k0;
    let t1_2 = 0.5 * k1;
    let t2_2 = t1_1 * t1_1;
    let t2_3 = 2. * (t1_1 * t1_2);
    let t2_4 = t1_2 * t1_2;
    let t3_4 = t2_2 * t1_2 + t2_3 * t1_1;
    let t3_6 = t2_4 * t1_2;
    let t4_4 = t2_2 * t2_2;
    let t4_5 = 2. * (t2_2 * t2_3);
    let t4_6 = 2. * (t2_2 * t2_4) + t2_3 * t2_3;
    let t4_7 = 2. * (t2_3 * t2_4);
    let t4_8 = t2_4 * t2_4;
    let t5_6 = t4_4 * t1_2 + t4_5 * t1_1;
    let t5_8 = t4_6 * t1_2 + t4_7 * t1_1;
    let t6_6 = t4_4 * t2_2;
    let t6_7 = t4_4 * t2_3 + t4_5 * t2_2;
    let t6_8 = t4_4 * t2_4 + t4_5 * t2_3 + t4_6 * t2_2;
    let t7_8 = t6_6 * t1_2 + t6_7 * t1_1;
    let t8_8 = t6_6 * t2_2;
    var u = 1.;
    u -= (1. / 24.) * t2_2 + (1. / 160.) * t2_4;
    u += (1. / 1920.) * t4_4 + (1. / 10752.) * t4_6 + (1. / 55296.) * t4_8;
    u -= (1. / 322560.) * t6_6 + (1. / 1658880.) * t6_8;
    u += (1. / 92897280.) * t8_8;
    var v = (1. / 12.) * t1_2;
    v -= (1. / 480.) * t3_4 + (1. / 2688.) * t3_6;
    v += (1. / 53760.) * t5_6 + (1. / 276480.) * t5_8;
    v -= (1. / 11612160.) * t7_8;
    return vec2(u, v);
}

// Evaluates the Euler spiral point at parameter t in chord-relative coordinates, where the
// segment endpoints map to (0, 0) at t = 0 and (1, 0) at t = 1.
fn es_params_eval(params: EulerParams, t: f32) -> vec2f {
    let thm = es_params_eval_th(params, t * 0.5);
    let k0 = params.k0;
    let k1 = params.k1;
    let uv = integ_euler_10((k0 + k1 * (0.5 * t - 0.5)) * t, k1 * t * t);
    let scale = t / params.ch;
    let s = scale * sin(thm);
    let c = scale * cos(thm);
    let x = uv.x * c - uv.y * s;
    let y = -uv.y * c - uv.x * s;
    return vec2(x, y);
}

// Evaluates the parallel curve of the Euler spiral at parameter t, displaced along the spiral
// normal by `offset` (in chord-relative units).
fn es_params_eval_with_offset(params: EulerParams, t: f32, offset: f32) -> vec2f {
    let th = es_params_eval_th(params, t);
    let v = offset * vec2f(sin(th), cos(th));
    return es_params_eval(params, t) + v;
}

// Constructs an Euler spiral segment from its endpoints and normalized spiral parameters.
fn es_seg_from_params(p0: vec2f, p1: vec2f, params: EulerParams) -> EulerSeg {
    return EulerSeg(p0, p1, params);
}

// Evaluates the parallel curve of an Euler spiral segment at parameter t, mapped back into the
// coordinate space of the segment endpoints. Note: the offset is normalized so 1 = chord length.
fn es_seg_eval_with_offset(es: EulerSeg, t: f32, normalized_offset: f32) -> vec2f {
    let chord = es.p1 - es.p0;
    let xy = es_params_eval_with_offset(es.params, t, normalized_offset);
    return es.p0 + vec2f(chord.x * xy.x - chord.y * xy.y, chord.x * xy.y + chord.y * xy.x);
}

// Computes sign(x) * |x|^1.5, used by the ESPC subdivision integrals.
fn pow_1_5_signed(x: f32) -> f32 {
    return x * sqrt(abs(x));
}

// Breakpoints and fitted coefficients for the piecewise ESPC integral approximation below.
const BREAK1: f32 = 0.8;
const BREAK2: f32 = 1.25;
const BREAK3: f32 = 2.1;
const SIN_SCALE: f32 = 1.0976991822760038;
const QUAD_A1: f32 = 0.6406;
const QUAD_B1: f32 = -0.81;
const QUAD_C1: f32 = 0.9148117935952064;
const QUAD_A2: f32 = 0.5;
const QUAD_B2: f32 = -0.156;
const QUAD_C2: f32 = 0.16145779359520596;
const QUAD_W1: f32 = 0.5 * QUAD_B1 / QUAD_A1;
const QUAD_V1: f32 = 1.0 / QUAD_A1;
const QUAD_U1: f32 = QUAD_W1 * QUAD_W1 - QUAD_C1 / QUAD_A1;
const QUAD_W2: f32 = 0.5 * QUAD_B2 / QUAD_A2;
const QUAD_V2: f32 = 1.0 / QUAD_A2;
const QUAD_U2: f32 = QUAD_W2 * QUAD_W2 - QUAD_C2 / QUAD_A2;
const FRAC_PI_4: f32 = 0.7853981633974483;
const CBRT_9_8: f32 = 1.040041911525952;

// Piecewise approximation of the ESPC (Euler spiral parallel curve) subdivision density
// integral. Odd in x: the sign of the input is preserved.
fn espc_int_approx(x: f32) -> f32 {
    let y = abs(x);
    var a: f32;
    if y < BREAK1 {
        a = sin(SIN_SCALE * y) * (1.0 / SIN_SCALE);
    } else if y < BREAK2 {
        a = (sqrt(8.0) / 3.0) * pow_1_5_signed(y - 1.0) + FRAC_PI_4;
    } else {
        let abc = select(vec3(QUAD_A2, QUAD_B2, QUAD_C2), vec3(QUAD_A1, QUAD_B1, QUAD_C1), y < BREAK3);
        a = (abc.x * y + abc.y) * y + abc.z;
    };
    return a * sign(x);
}

// Piecewise approximation of the inverse of espc_int_approx, used to map evenly spaced
// subdivision fractions back to spiral parameters. Odd in x.
fn espc_int_inv_approx(x: f32) -> f32 {
    let y = abs(x);
    var a: f32;
    if y < 0.7010707591262915 {
        a = asin(y * SIN_SCALE) * (1.0 / SIN_SCALE);
    } else if y < 0.903249293595206 {
        let b = y - FRAC_PI_4;
        let u = pow(abs(b), 2. / 3.) * sign(b);
        a = u * CBRT_9_8 + 1.0;
    } else {
        let uvw = select(vec3(QUAD_U2, QUAD_V2, QUAD_W2), vec3(QUAD_U1, QUAD_V1, QUAD_W1), y < 2.038857793595206);
        a = sqrt(uvw.x + uvw.y * y) - uvw.z;
    }
    return a * sign(x);
}

// A point on a curve together with its first derivative at the same parameter.
struct PointDeriv {
    point: vec2f,
    deriv: vec2f,
}

// Evaluates a cubic Bezier and its first derivative at parameter t. The returned derivative is
// scaled by 1/3 relative to the true derivative.
fn eval_cubic_and_deriv(p0: vec2f, p1: vec2f, p2: vec2f, p3: vec2f, t: f32) -> PointDeriv {
    let m = 1.0 - t;
    let mm = m * m;
    let mt = m * t;
    let tt = t * t;
    let p = p0 * (mm * m) + (p1 * (3.0 * mm) + p2 * (3.0 * mt) + p3 * tt) * t;
    let q = (p1 - p0) * mm + (p2 - p1) * (2.0 * mt) + (p3 - p2) * tt;
    return PointDeriv(p, q);
}

// Returns the start tangent of a cubic Bezier, falling back to later control points when the
// leading ones are coincident so degenerate segments still yield a usable direction.
fn cubic_start_tangent(p0: vec2f, p1: vec2f, p2: vec2f, p3: vec2f) -> vec2f {
    let EPS = 1e-12;
    let d01 = p1 - p0;
    let d02 = p2 - p0;
    let d03 = p3 - p0;
    return select(select(d03, d02, dot(d02, d02) > EPS), d01, dot(d01, d01) > EPS);
}

// Robustness regimes for the ESPC subdivision integral in flatten_euler: near-zero curvature
// derivative and near-zero offset each get a numerically stable specialization.
const ESPC_ROBUST_NORMAL = 0;
const ESPC_ROBUST_LOW_K1 = 1;
const ESPC_ROBUST_LOW_DIST = 2;

// Flattens a cubic Bezier by first converting it into Euler spiral segments, then computing a
// near-optimal flattening of the parallel curves of the Euler spiral segments. With offset == 0
// the curve itself is flattened in device space; a non-zero offset flattens the parallel curve in
// local space and transforms each emitted line. `start_p`/`end_p` pin the exact endpoints so
// adjacent segments stay watertight. Retained from Vello: the C# encoder currently pre-flattens
// fill curves to lines (degree-raised to cubics), so this path sees no true curves today, but the
// capability is kept intact.
fn flatten_euler(
    cubic: CubicPoints,
    path_ix: u32,
    local_to_device: Transform,
    offset: f32,
    start_p: vec2f,
    end_p: vec2f,
) {
    var p0: vec2f;
    var p1: vec2f;
    var p2: vec2f;
    var p3: vec2f;
    var scale: f32;
    var transform: Transform;
    var t_start = start_p;
    var t_end = end_p;
    if offset == 0. {
        let t = local_to_device;
        p0 = transform_apply(t, cubic.p0);
        p1 = transform_apply(t, cubic.p1);
        p2 = transform_apply(t, cubic.p2);
        p3 = transform_apply(t, cubic.p3);
        scale = 1.;
        transform = transform_identity();
        t_start = p0;
        t_end = p3;
    } else {
        p0 = cubic.p0;
        p1 = cubic.p1;
        p2 = cubic.p2;
        p3 = cubic.p3;

        transform = local_to_device;
        let mat = transform.mat;
        // The scale is the semi-major axis of the ellipse given by the unit
        // circle transformed by `transform`. This is the greater of the two
        // singular values of the 2x2 `transform` matrix (ignoring
        // translation).
        scale = 0.5 * (length(vec2(mat.x + mat.w, mat.y - mat.z)) +
                length(vec2(mat.x - mat.w, mat.y + mat.z)));
    }

    // Drop zero length lines. This is an exact equality test because dropping very short
    // line segments may result in loss of watertightness.
    if all(p0 == p1) && all(p0 == p2) && all(p0 == p3) {
        return;
    }

    let tol = 0.25;
    var t0_u = 0u;
    var dt = 1.0;
    var last_p = p0;
    var last_q = p1 - p0;
    if dot(last_q, last_q) < DERIV_THRESH_SQUARED {
        last_q = eval_cubic_and_deriv(p0, p1, p2, p3, DERIV_EPS).deriv;
    }
    var last_t = 0.0;
    var lp0 = t_start;
    loop {
        let t0 = f32(t0_u) * dt;
        if t0 == 1.0 {
            break;
        }
        var t1 = t0 + dt;
        let this_p0 = last_p;
        let this_q0 = last_q;
        var this_pq1 = eval_cubic_and_deriv(p0, p1, p2, p3, t1);
        if dot(this_pq1.deriv, this_pq1.deriv) < DERIV_THRESH_SQUARED {
            let new_pq1 = eval_cubic_and_deriv(p0, p1, p2, p3, t1 - DERIV_EPS);
            this_pq1.deriv = new_pq1.deriv;
            if t1 < 1.0 {
                this_pq1.point = new_pq1.point;
                t1 = t1 - DERIV_EPS;
            }
        }
        let actual_dt = t1 - last_t;
        let cubic_params = cubic_from_points_derivs(this_p0, this_pq1.point, this_q0, this_pq1.deriv, actual_dt);
        if cubic_params.err * scale <= tol || dt <= SUBDIV_LIMIT {
            let euler_params = es_params_from_angles(cubic_params.th0, cubic_params.th1);
            let es = es_seg_from_params(this_p0, this_pq1.point, euler_params);
            let k0 = es.params.k0 - 0.5 * es.params.k1;
            let k1 = es.params.k1;
            let normalized_offset = offset / cubic_params.chord_len;
            let dist_scaled = normalized_offset * es.params.ch;
            let scale_multiplier = sqrt(0.125 * scale * cubic_params.chord_len / (es.params.ch * tol));
            var a = 0.0;
            var b = 0.0;
            var integral = 0.0;
            var int0 = 0.0;
            var n_frac: f32;
            var robust = ESPC_ROBUST_NORMAL;
            if abs(k1) < K1_THRESH {
                let k = es.params.k0;
                n_frac = sqrt(abs(k * (k * dist_scaled + 1.0)));
                robust = ESPC_ROBUST_LOW_K1;
            } else if abs(dist_scaled) < DIST_THRESH {
                a = k1;
                b = k0;
                int0 = pow_1_5_signed(b);
                let int1 = pow_1_5_signed(a + b);
                integral = int1 - int0;
                n_frac = (2. / 3.) * integral / a;
                robust = ESPC_ROBUST_LOW_DIST;
            } else {
                a = -2.0 * dist_scaled * k1;
                b = -1.0 - 2.0 * dist_scaled * k0;
                int0 = espc_int_approx(b);
                let int1 = espc_int_approx(a + b);
                integral = int1 - int0;
                let k_peak = k0 - k1 * b / a;
                let integrand_peak = sqrt(abs(k_peak * (k_peak * dist_scaled + 1.0)));
                n_frac = integral * integrand_peak / a;
            }
            // Bound number of subdivisions to a reasonable number when the scale is huge.
            // This may give slightly incorrect rendering but avoids hangs.
            // TODO: aggressively cull to viewport
            let n = clamp(ceil(n_frac * scale_multiplier), 1.0, 100.0);
            for (var i = 0u; i < u32(n); i++) {
                var lp1: vec2f;
                if i + 1u == u32(n) && t1 == 1.0 {
                    lp1 = t_end;
                } else {
                    let t = f32(i + 1u) / n;
                    var s = t;
                    if robust != ESPC_ROBUST_LOW_K1 {
                        let u = integral * t + int0;
                        var inv: f32;
                        if robust == ESPC_ROBUST_LOW_DIST {
                            inv = pow(abs(u), 2. / 3.) * sign(u);
                        } else {
                            inv = espc_int_inv_approx(u);
                        }
                        s = (inv - b) / a;
                    }
                    lp1 = es_seg_eval_with_offset(es, s, normalized_offset);
                }
                let l0 = select(lp1, lp0, offset >= 0.);
                let l1 = select(lp0, lp1, offset >= 0.);
                output_line_with_transform(path_ix, l0, l1, transform);
                lp0 = lp1;
            }
            last_p = this_pq1.point;
            last_q = this_pq1.deriv;
            last_t = t1;
            // Dyadic bookkeeping: advance to the next interval, then merge fully consumed
            // sibling intervals back into a coarser dt so the walk stays O(log) deep.
            t0_u += 1u;
            let shift = countTrailingZeros(t0_u);
            t0_u >>= shift;
            dt *= f32(1u << shift);
        } else {
            // Fit error too large for one Euler segment: halve the interval and retry.
            t0_u = t0_u * 2u;
            dt *= 0.5;
        }
    }
}

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
    let start_angle = atan2(from_offset.y, from_offset.x);
    let end_angle = atan2(to_offset.y, to_offset.x);
    let sweep = stroke_normalize_positive_angle(end_angle - start_angle);
    let n = stroke_arc_subdivision_count(radius, sweep, arc_detail_scale);
    let step = sweep / f32(n + 1u);
    for (var i = 1u; i <= n; i += 1u) {
        let a = start_angle + (step * f32(i));
        stroke_chain_point(path_ix, last, center + (vec2(cos(a), sin(a)) * radius), transform);
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
    var a1 = atan2(o1.y, o1.x);
    var a2 = atan2(o2.y, o2.x);
    let da = acos(half_width / (half_width + (0.125 / arc_detail_scale))) * 2.0;
    stroke_chain_point(path_ix, last, v1 + o1, transform);
    if a1 > a2 {
        a2 += 6.283185307179586;
    }
    // Bounded for GPU safety; matches the CPU count for all real detail scales.
    let n = clamp(i32((a2 - a1) / da), 0, 1024);
    let step = (a2 - a1) / f32(n + 1);
    a1 += step;
    for (var i = 0; i < n; i += 1) {
        stroke_chain_point(path_ix, last, v1 + (vec2(cos(a1), sin(a1)) * half_width), transform);
        a1 += step;
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

// Returns the identity transform: unit matrix, zero translation, trivial perspective row.
fn transform_identity() -> Transform {
    return Transform(vec4(1., 0., 0., 1.), vec2(0.), vec3(0., 0., 1.));
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

// Control points of a cubic Bezier; lines and quads are degree-raised to this common form.
struct CubicPoints {
    p0: vec2f,
    p1: vec2f,
    p2: vec2f,
    p3: vec2f,
}

// Decodes the control points of the segment described by `tag`, reading f32 or packed i16 points
// as indicated by the tag, translating stroke cap markers (quadto-encoded tangents) into lines,
// and degree-raising lines and quads so downstream code handles a single cubic form.
fn read_path_segment(tag: PathTagData, is_stroke: bool) -> CubicPoints {
    var p0: vec2f;
    var p1: vec2f;
    var p2: vec2f;
    var p3: vec2f;

    var seg_type = tag.tag_byte & PATH_TAG_SEG_TYPE;
    let pathseg_offset = tag.monoid.pathseg_offset;
    let is_stroke_cap_marker = is_stroke && (tag.tag_byte & PATH_TAG_SUBPATH_END) != 0u;
    let is_open = seg_type == PATH_TAG_QUADTO;

    if (tag.tag_byte & PATH_TAG_F32) != 0u {
        p0 = read_f32_point(pathseg_offset);
        p1 = read_f32_point(pathseg_offset + 2u);
        if seg_type >= PATH_TAG_QUADTO {
            p2 = read_f32_point(pathseg_offset + 4u);
            if seg_type == PATH_TAG_CUBICTO {
                p3 = read_f32_point(pathseg_offset + 6u);
            }
        }
    } else {
        p0 = read_i16_point(pathseg_offset);
        p1 = read_i16_point(pathseg_offset + 1u);
        if seg_type >= PATH_TAG_QUADTO {
            p2 = read_i16_point(pathseg_offset + 2u);
            if seg_type == PATH_TAG_CUBICTO {
                p3 = read_i16_point(pathseg_offset + 3u);
            }
        }
    }

    if is_stroke_cap_marker && is_open {
        // The stroke cap marker for an open path is encoded as a quadto where the p1 and p2 store
        // the start control point of the subpath and together with p2 forms the start tangent. p0
        // is ignored.
        //
        // This is encoded this way because encoding this as a lineto would require adding a moveto,
        // which would terminate the subpath too early (by setting the SUBPATH_END on the
        // segment preceding the cap marker). This scheme is only used for strokes.
        p0 = p1;
        p1 = p2;
        seg_type = PATH_TAG_LINETO;
    }

    // Degree-raise
    if seg_type == PATH_TAG_LINETO {
        p3 = p1;
        p2 = p3 + (1.0 / 3.0) * (p0 - p3);
        p1 = p0 + (1.0 / 3.0) * (p3 - p0);
    } else if seg_type == PATH_TAG_QUADTO {
        p3 = p2;
        p2 = p1 + (1.0 / 3.0) * (p2 - p1);
        p1 = p1 + (1.0 / 3.0) * (p0 - p1);
    }

    return CubicPoints(p0, p1, p2, p3);
}

// Writes a line into the `lines` buffer at the pre-allocated slot `line_ix` and grows this
// invocation's running bbox. Out-of-range writes are dropped (the bump counter may legitimately
// exceed the buffer size).
fn write_line(line_ix: u32, path_ix: u32, p0: vec2f, p1: vec2f) {
    bbox = vec4(min(bbox.xy, min(p0, p1)), max(bbox.zw, max(p0, p1)));
    if line_ix < config.lines_size {
        lines[line_ix] = LineSoup(path_ix, p0, p1);
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
    var tangent = pts.p3 - pts.p0;
    if !is_stroke_cap_marker {
        tangent = cubic_start_tangent(pts.p0, pts.p1, pts.p2, pts.p3);
    }
    return NeighboringSegment(do_join, length(pts.p3 - pts.p0), tangent);
}

// `pathdata_base` is decoded once and reused by helpers above.
var<private> pathdata_base: u32;

// This is the bounding box of the shape flattened by a single shader invocation. It gets modified
// during LineSoup generation.
var<private> bbox: vec4f;

// Entry point: one invocation per path tag. Decodes the tag's style, transform, and control
// points; records per-path draw metadata on PATH tags; emits fill geometry via flatten_euler or
// stroke outline geometry via the CPU stroker port; then merges this invocation's device-space
// extents into the path's atomic bbox.
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
    // 4 arc detail scale, 5 coverage threshold, 6..9 interest rect.
    let style_flags = scene[config.style_base + style_ix];
    let style_draw_flags = scene[config.style_base + style_ix + 2u];
    let coverage_threshold = bitcast<f32>(scene[config.style_base + style_ix + 5u]);
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
        (*out).coverage_threshold = coverage_threshold;
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
                    var tangent = pts.p3 - pts.p0;
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
                let p1 = pts.p3;
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
            let offset = 0.;
            flatten_euler(pts, path_ix, transform, offset, pts.p0, pts.p3);
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
