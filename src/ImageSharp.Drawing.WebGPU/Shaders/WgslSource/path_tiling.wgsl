// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Writes the final per-tile path segments. One thread per SegmentCount
// record from path_count: replays the line's tile-crossing traversal to
// find the record's tile, clips the line to that tile, applies numerical
// robustness nudges, and stores the tile-relative Segment in the slot
// range reserved by the coarse stage.
//
// Inputs: bump (seg_counts total), seg_counts (from path_count), lines
// (LineSoup), paths and rows (sparse row records), tiles (coarse output;
// segment_count_or_ix holds the inverted segment base index).
// Outputs: segments (tile-relative endpoints plus y_edge, consumed by
// fine).
//
// Ported from Vello's path_tiling.wgsl, modified for the local sparse
// tile-row model: the tile index is resolved through the path's PathRow
// record (row.tiles + x - row.x0) instead of a dense bbox grid.

#import bump
#import config
#import segment
#import tile

@group(0) @binding(0)
var<storage, read_write> bump: BumpAllocators;

@group(0) @binding(1)
var<storage> seg_counts: array<SegmentCount>;

@group(0) @binding(2)
var<storage> lines: array<LineSoup>;

@group(0) @binding(3)
var<storage> paths: array<Path>;

@group(0) @binding(4)
var<storage> rows: array<PathRow>;

@group(0) @binding(5)
var<storage> tiles: array<Tile>;

@group(0) @binding(6)
var<storage, read_write> segments: array<Segment>;

// Returns the number of tile grid cells spanned by the interval [a, b]
// (in tile units), with a minimum of 1.
fn span(a: f32, b: f32) -> u32 {
    return u32(max(ceil(max(a, b)) - floor(min(a, b)), 1.0));
}

// Largest f32 strictly less than 1.0; clamping b below 1 keeps the first
// floor(a * i + b) evaluation from overshooting a boundary.
const ONE_MINUS_ULP: f32 = 0.99999994;
// Slope nudge applied when accumulated rounding in floor(a * i + b)
// disagrees with the exact crossing count for the full line. Must match
// path_count exactly so both stages assign a crossing to the same tile.
const ROBUST_EPSILON: f32 = 2e-7;

// Writes one clipped segment. seg_within_line selects which of the line's
// tile crossings this record represents (recomputed with the same
// parameterization as path_count); seg_within_slice is the segment's slot
// within the tile's reserved range.
@compute @workgroup_size(256)
fn main(
    @builtin(global_invocation_id) global_id: vec3<u32>,
) {
    let n_segments = atomicLoad(&bump.seg_counts);
    if global_id.x >= n_segments {
        return;
    }

    let seg_count = seg_counts[global_id.x];
    let line = lines[seg_count.line_ix];
    let counts = seg_count.counts;
    let seg_within_slice = counts >> 16u;
    let seg_within_line = counts & 0xffffu;

    let is_down = line.p1.y >= line.p0.y;
    var xy0 = select(line.p1, line.p0, is_down);
    var xy1 = select(line.p0, line.p1, is_down);
    let s0 = xy0 * TILE_SCALE;
    let s1 = xy1 * TILE_SCALE;
    let count_x = span(s0.x, s1.x) - 1u;
    let count = count_x + span(s0.y, s1.y);
    let dx = abs(s1.x - s0.x);
    let dy = s1.y - s0.y;
    let idxdy = 1.0 / (dx + dy);
    var a = dx * idxdy;
    let is_positive_slope = s1.x >= s0.x;
    let x_sign = select(-1.0, 1.0, is_positive_slope);
    let xt0 = floor(s0.x * x_sign);
    let c = s0.x * x_sign - xt0;
    let y0i = floor(s0.y);
    let ytop = select(y0i + 1.0, ceil(s0.y), s0.y == s1.y);
    let b = min((dy * c + dx * (ytop - s0.y)) * idxdy, ONE_MINUS_ULP);
    let robust_err = floor(a * (f32(count) - 1.0) + b) - f32(count_x);
    if robust_err != 0.0 {
        a -= ROBUST_EPSILON * sign(robust_err);
    }
    let x0i = i32(xt0 * x_sign + 0.5 * (x_sign - 1.0));
    let z = floor(a * f32(seg_within_line) + b);
    let x = x0i + i32(x_sign * z);
    let y = i32(y0i + f32(seg_within_line) - z);

    let path = paths[line.path_ix];
    let row = rows[path.rows + u32(y) - path.bbox.y];
    let tile_ix = row.tiles + u32(x) - row.x0;
    let tile = tiles[tile_ix];
    // Coarse stores the inverted segment base index; a non-inverted value
    // (negative after ~) means the tile was never allocated, so the
    // segment has nowhere to go.
    let seg_start = ~tile.segment_count_or_ix;
    if i32(seg_start) < 0 {
        return;
    }

    let tile_xy = vec2(f32(x) * f32(TILE_WIDTH), f32(y) * f32(TILE_HEIGHT));
    let tile_xy1 = tile_xy + vec2(f32(TILE_WIDTH), f32(TILE_HEIGHT));
    // Clip the segment's start to the tile edge it entered through, unless
    // this is the line's first crossing (the true endpoint is inside).
    if seg_within_line > 0u {
        let z_prev = floor(a * (f32(seg_within_line) - 1.0) + b);
        if z == z_prev {
            // Top edge is clipped.
            var xt = xy0.x + (xy1.x - xy0.x) * (tile_xy.y - xy0.y) / (xy1.y - xy0.y);
            xt = clamp(xt, tile_xy.x + 1e-3, tile_xy1.x);
            xy0 = vec2(xt, tile_xy.y);
        } else {
            // If is_positive_slope, the left edge is clipped, else the right.
            let x_clip = select(tile_xy1.x, tile_xy.x, is_positive_slope);
            var yt = xy0.y + (xy1.y - xy0.y) * (x_clip - xy0.x) / (xy1.x - xy0.x);
            yt = clamp(yt, tile_xy.y + 1e-3, tile_xy1.y);
            xy0 = vec2(x_clip, yt);
        }
    }
    // Likewise clip the segment's end to the tile edge it exits through,
    // unless this is the line's last crossing.
    if seg_within_line < count - 1u {
        let z_next = floor(a * (f32(seg_within_line) + 1.0) + b);
        if z == z_next {
            // Bottom edge is clipped.
            var xt = xy0.x + (xy1.x - xy0.x) * (tile_xy1.y - xy0.y) / (xy1.y - xy0.y);
            xt = clamp(xt, tile_xy.x + 1e-3, tile_xy1.x);
            xy1 = vec2(xt, tile_xy1.y);
        } else {
            // If is_positive_slope, the right edge is clipped, else the left.
            let x_clip = select(tile_xy.x, tile_xy1.x, is_positive_slope);
            var yt = xy0.y + (xy1.y - xy0.y) * (x_clip - xy0.x) / (xy1.x - xy0.x);
            yt = clamp(yt, tile_xy.y + 1e-3, tile_xy1.y);
            xy1 = vec2(x_clip, yt);
        }
    }

    // Numerical robustness for fine: y_edge records where the segment
    // meets the tile's left edge (1e9 means it does not), and endpoints
    // exactly on that edge are nudged inward by EPSILON so fine's winding
    // computation never sees an ambiguous x == 0 coordinate.
    var y_edge = 1e9;
    var p0 = xy0 - tile_xy;
    var p1 = xy1 - tile_xy;
    let EPSILON = 1e-6;
    if p0.x == 0.0 {
        if p1.x == 0.0 {
            p0.x = EPSILON;
            if p0.y == 0.0 {
                // Vertical line covering the entire left edge of the tile.
                p1.x = EPSILON;
                p1.y = f32(TILE_HEIGHT);
            } else {
                // The owning tile must ignore this boundary edge, but the aliased row walk in
                // the tile on the left still needs its exact crossing to match the CPU's single
                // crossing list for the full row. Preserve the vertical extent under an explicit
                // halo-only marker; ordinary fine integration continues to treat it as absent.
                p1.x = EPSILON;
                y_edge = HALO_ONLY_Y_EDGE;
            }
        } else if p0.y == 0.0 {
            p0.x = EPSILON;
        } else {
            y_edge = p0.y;
        }
    } else if p1.x == 0.0 {
        if p1.y == 0.0 {
            p1.x = EPSILON;
        } else {
            y_edge = p1.y;
        }
    }
    // Make sure there are no vertical lines aligned to the pixel grid in
    // the tile interior; doing this here is cheaper than handling the
    // degenerate case in fine.
    if p0.x == floor(p0.x) && p0.x != 0.0 {
        p0.x -= EPSILON;
    }
    if p1.x == floor(p1.x) && p1.x != 0.0 {
        p1.x -= EPSILON;
    }
    // The traversal used downward-normalized endpoints; swap back so the
    // stored segment keeps the line's original winding direction.
    if !is_down {
        let tmp = p0;
        p0 = p1;
        p1 = tmp;
    }
    let segment = Segment(p0, p1, y_edge, line.tag);
    segments[seg_start + seg_within_slice] = segment;
}
