// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Stage to compute counts of number of segments in each tile

#import bump
#import config
#import segment
#import tile

struct AtomicTile {
    backdrop: atomic<i32>,
    segment_count_or_ix: atomic<u32>,
}

@group(0) @binding(0)
var<uniform> config: Config;

@group(0) @binding(1)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(2)
var<storage> lines: array<LineSoup>;

@group(0) @binding(3)
var<storage> paths: array<Path>;

@group(0) @binding(4)
var<storage> rows: array<PathRow>;

@group(0) @binding(5)
var<storage, read_write> tile: array<AtomicTile>;

@group(0) @binding(6)
var<storage, read_write> seg_counts: array<SegmentCount>;

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
    var count = 0u;
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
    count = count_x + span(s0.y, s1.y);
    let line_ix = global_id.x;

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
    if s0.y >= f32(bbox.w) || s1.y <= f32(bbox.y) || xmin >= f32(bbox.z) || bbox.z <= bbox.x {
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

    if max(s0.x, s1.x) <= f32(bbox.x) {
        imax = imin;
    } else {
        let fudge = select(1.0, 0.0, is_positive_slope);
        if xmin < f32(bbox.x) {
            var f = round((x_sign * (f32(bbox.x) - x0) - b + fudge) / a);
            if (x0 + x_sign * floor(a * f + b) < f32(bbox.x)) == is_positive_slope {
                f += 1.0;
            }
            if is_positive_slope {
                if u32(f) > imin {
                    imin = u32(f);
                }
            } else {
                if u32(f) < imax {
                    imax = u32(f);
                }
            }
        }
        if max(s0.x, s1.x) > f32(bbox.z) {
            var f = round((x_sign * (f32(bbox.z) - x0) - b + fudge) / a);
            if (x0 + x_sign * floor(a * f + b) < f32(bbox.z)) == is_positive_slope {
                f += 1.0;
            }
            if is_positive_slope {
                imax = min(imax, u32(f));
            } else {
                imin = max(imin, u32(f));
            }
        }
    }

    let delta = select(1, -1, is_down);
    imax = max(imin, imax);
    var last_z = floor(a * (f32(imin) - 1.0) + b);
    let seg_base = atomicAdd(&bump.seg_counts, imax - imin);
    for (var i = imin; i < imax; i += 1u) {
        let zf = a * f32(i) + b;
        let z = floor(zf);
        let y = i32(y0 + f32(i) - z);
        let x = i32(x0 + x_sign * z);
        let row = rows[path.rows + u32(y - bbox.y)];
        let top_edge = select(last_z == z, y0 == s0.y, i == 0u);

        // Top-edge backdrop propagation must fire even when the crossing
        // column itself lies outside the sparse row's allocated column span —
        // for example, a line entering from the left of bbox (x < bbox.x).
        // path_row_span already clamped x_bump to bbox.x and expanded row.x0
        // accordingly, so clamp the bump target to row.x0 here to route the
        // winding into the row's leftmost allocated tile. Skipping this (as
        // the old in-range-only path did) left fills whose left edges crossed
        // the bbox boundary with zero-backdrop interior tiles — rendering
        // horizontal row-aligned gaps.
        if top_edge && row.x0 < row.x1 && x + 1 < i32(row.x1) {
            let x_bump = max(x + 1, i32(row.x0));
            let bump_ix = row.tiles + u32(x_bump) - row.x0;
            atomicAdd(&tile[bump_ix].backdrop, delta);
        }

        if u32(x) < row.x0 || u32(x) >= row.x1 {
            last_z = z;
            continue;
        }

        let tile_ix = row.tiles + u32(x) - row.x0;
        let seg_within_slice = atomicAdd(&tile[tile_ix].segment_count_or_ix, 1u);
        let counts = (seg_within_slice << 16u) | i;
        let seg_count = SegmentCount(line_ix, counts);
        let seg_ix = seg_base + i - imin;
        if seg_ix < config.seg_counts_size {
            seg_counts[seg_ix] = seg_count;
        }

        last_z = z;
    }
}
