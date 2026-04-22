// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Derive sparse per-row tile spans from the flattened line stream.

#import bump
#import config
#import segment
#import tile

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(2)
var<storage> lines: array<LineSoup>;

@group(0) @binding(3)
var<storage> paths: array<Path>;

@group(0) @binding(4)
var<storage, read_write> rows: array<AtomicPathRow>;

fn span(a: f32, b: f32) -> u32 {
    return u32(max(ceil(max(a, b)) - floor(min(a, b)), 1.0));
}

const ONE_MINUS_ULP: f32 = 0.99999994;
const ROBUST_EPSILON: f32 = 2e-7;

@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
) {
    let n_lines = atomicLoad(&bump.lines);
    if global_id.x >= n_lines {
        return;
    }

    let line = lines[global_id.x];
    let is_down = line.p1.y >= line.p0.y;
    let xy0 = select(line.p1, line.p0, is_down);
    let xy1 = select(line.p0, line.p1, is_down);
    let s0 = xy0 * TILE_SCALE;
    let s1 = xy1 * TILE_SCALE;
    let count_x = span(s0.x, s1.x) - 1u;
    let count = count_x + span(s0.y, s1.y);

    let dx = abs(s1.x - s0.x);
    let dy = s1.y - s0.y;
    if dx + dy == 0.0 {
        return;
    }
    if dy == 0.0 && floor(s0.y) == s0.y {
        return;
    }

    let idxdy = 1.0 / (dx + dy);
    var a = dx * idxdy;
    let is_positive_slope = s1.x >= s0.x;
    let x_sign = select(-1.0, 1.0, is_positive_slope);
    let xt0 = floor(s0.x * x_sign);
    let c = s0.x * x_sign - xt0;
    let y0 = floor(s0.y);
    let ytop = select(y0 + 1.0, ceil(s0.y), s0.y == s1.y);
    let b = min((dy * c + dx * (ytop - s0.y)) * idxdy, ONE_MINUS_ULP);
    let robust_err = floor(a * (f32(count) - 1.0) + b) - f32(count_x);
    if robust_err != 0.0 {
        a -= ROBUST_EPSILON * sign(robust_err);
    }
    let x0 = xt0 * x_sign + select(-1.0, 0.0, is_positive_slope);

    let path = paths[line.path_ix];
    let bbox = vec4<i32>(path.bbox);
    let xmin = min(s0.x, s1.x);
    if s0.y >= f32(bbox.w) || s1.y <= f32(bbox.y) || bbox.z <= bbox.x {
        return;
    }

    if xmin > f32(bbox.z) {
        let ymin_right = max(i32(ceil(s0.y)), bbox.y);
        let ymax_right = min(i32(ceil(s1.y)), bbox.w);
        for (var y = ymin_right; y < ymax_right; y += 1) {
            let row_ix = path.rows + u32(y - bbox.y);
            atomicOr(&rows[row_ix].tiles, PATH_ROW_FLAG_TOUCHES_RIGHT);
        }

        return;
    }

    var imin = 0u;
    if s0.y < f32(bbox.y) {
        var iminf = round((f32(bbox.y) - y0 + b - a) / (1.0 - a)) - 1.0;
        if y0 + iminf - floor(a * iminf + b) < f32(bbox.y) {
            iminf += 1.0;
        }
        imin = u32(iminf);
    }

    var imax = count;
    if s1.y > f32(bbox.w) {
        var imaxf = round((f32(bbox.w) - y0 + b - a) / (1.0 - a)) - 1.0;
        if y0 + imaxf - floor(a * imaxf + b) < f32(bbox.w) {
            imaxf += 1.0;
        }
        imax = u32(imaxf);
    }

    let delta = select(1, -1, is_down);
    var ymin = 0;
    var ymax = 0;
    if max(s0.x, s1.x) <= f32(bbox.x) {
        ymin = i32(ceil(s0.y));
        ymax = i32(ceil(s1.y));
        imax = imin;
    } else {
        let fudge = select(1.0, 0.0, is_positive_slope);
        if xmin < f32(bbox.x) {
            var f = round((x_sign * (f32(bbox.x) - x0) - b + fudge) / a);
            if (x0 + x_sign * floor(a * f + b) < f32(bbox.x)) == is_positive_slope {
                f += 1.0;
            }
            let ynext = i32(y0 + f - floor(a * f + b) + 1.0);
            if is_positive_slope {
                if u32(f) > imin {
                    ymin = i32(y0 + select(1.0, 0.0, y0 == s0.y));
                    ymax = ynext;
                    imin = u32(f);
                }
            } else {
                if u32(f) < imax {
                    ymin = ynext;
                    ymax = i32(ceil(s1.y));
                    imax = u32(f);
                }
            }
        }
        if max(s0.x, s1.x) > f32(bbox.z) {
            var f = round((x_sign * (f32(bbox.z) - x0) - b + fudge) / a);
            if (x0 + x_sign * floor(a * f + b) < f32(bbox.z)) == is_positive_slope {
                f += 1.0;
            }
            // Rows whose iterations were truncated because the line was past
            // bbox.z still need to be flagged as TOUCHES_RIGHT so tile_alloc
            // will expand row.x1 to cover the sparse fill interior. Without
            // this, a row whose only line activity is off the right side of
            // the bbox leaves row.x1 at its initial (0) or too-narrow value,
            // and backdrop propagation stops short — producing a horizontal
            // gap in the fill at exactly that tile row. This mirrors the
            // row.backdrop accumulation performed in the xmin < bbox.x
            // branch above.
            let ynext_r = i32(y0 + f - floor(a * f + b) + 1.0);
            var yflag_min: i32 = 0;
            var yflag_max: i32 = 0;
            if is_positive_slope && u32(f) < imax {
                yflag_min = ynext_r;
                yflag_max = i32(ceil(s1.y));
                imax = u32(f);
            } else if !is_positive_slope && u32(f) > imin {
                yflag_min = i32(y0 + select(1.0, 0.0, y0 == s0.y));
                yflag_max = ynext_r;
                imin = u32(f);
            }
            yflag_min = max(yflag_min, i32(bbox.y));
            yflag_max = min(yflag_max, i32(bbox.w));
            for (var y = yflag_min; y < yflag_max; y += 1) {
                let row_ix = path.rows + u32(y - i32(bbox.y));
                atomicOr(&rows[row_ix].tiles, PATH_ROW_FLAG_TOUCHES_RIGHT);
            }
        }
    }

    imax = max(imin, imax);

    ymin = max(ymin, bbox.y);
    ymax = min(ymax, bbox.w);
    for (var y = ymin; y < ymax; y += 1) {
        let row_ix = path.rows + u32(y - bbox.y);
        atomicAdd(&rows[row_ix].backdrop, delta);
    }

    var last_z = floor(a * (f32(imin) - 1.0) + b);
    for (var i = imin; i < imax; i += 1u) {
        let zf = a * f32(i) + b;
        let z = floor(zf);
        let y = i32(y0 + f32(i) - z);
        let x = i32(x0 + x_sign * z);
        let row_ix = path.rows + u32(y - bbox.y);
        if x >= bbox.z {
            atomicOr(&rows[row_ix].tiles, PATH_ROW_FLAG_TOUCHES_RIGHT);
            last_z = z;
            continue;
        }

        let min_x = u32(max(x, bbox.x));
        atomicMin(&rows[row_ix].x0, min_x);
        atomicMax(&rows[row_ix].x1, min_x + 1u);

        let top_edge = select(last_z == z, y0 == s0.y, i == 0u);
        if top_edge && x + 1 < bbox.z {
            let x_bump = u32(max(x + 1, bbox.x));
            atomicMin(&rows[row_ix].x0, x_bump);
            atomicMax(&rows[row_ix].x1, x_bump + 1u);
        }

        last_z = z;
    }
}
