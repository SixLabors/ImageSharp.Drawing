// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Counts paths per tile to size tile command lists.
/// </summary>
internal static class PathCountComputeShader
{
    /// <summary>
    /// Gets the null-terminated WGSL source for the path count pass.
    /// </summary>
    private static readonly byte[] CodeBytes =
    [
        ..
        """
        // Path count stage.

        const STAGE_BINNING: u32 = 0x1u;
        const STAGE_TILE_ALLOC: u32 = 0x2u;
        const STAGE_FLATTEN: u32 = 0x4u;
        const STAGE_PATH_COUNT: u32 = 0x8u;
        const STAGE_COARSE: u32 = 0x10u;

        struct BumpAllocators {
            failed: atomic<u32>,
            binning: atomic<u32>,
            ptcl: atomic<u32>,
            tile: atomic<u32>,
            seg_counts: atomic<u32>,
            segments: atomic<u32>,
            blend: atomic<u32>,
            lines: atomic<u32>,
        }

        struct Config {
            width_in_tiles: u32,
            height_in_tiles: u32,
            target_width: u32,
            target_height: u32,
            base_color: u32,
            n_drawobj: u32,
            n_path: u32,
            n_clip: u32,
            bin_data_start: u32,
            pathtag_base: u32,
            pathdata_base: u32,
            drawtag_base: u32,
            drawdata_base: u32,
            transform_base: u32,
            style_base: u32,
            lines_size: u32,
            binning_size: u32,
            tiles_size: u32,
            seg_counts_size: u32,
            segments_size: u32,
            blend_size: u32,
            ptcl_size: u32,
        }

        const TILE_WIDTH = 16u;
        const TILE_HEIGHT = 16u;
        const TILE_SCALE = 0.0625;

        struct LineSoup {
            path_ix: u32,
            p0: vec2<f32>,
            p1: vec2<f32>,
        }

        struct SegmentCount {
            line_ix: u32,
            counts: u32,
        }

        struct Path {
            bbox: vec4<u32>,
            tiles: u32,
        }

        struct Tile {
            backdrop: i32,
            segment_count_or_ix: u32,
        }

        // TODO: this is cut'n'pasted from path_coarse.
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
        var<storage, read_write> tile: array<AtomicTile>;

        @group(0) @binding(5)
        var<storage, read_write> seg_counts: array<SegmentCount>;

        fn span(a: f32, b: f32) -> u32 {
            return u32(max(ceil(max(a, b)) - floor(min(a, b)), 1.0));
        }

        const ONE_MINUS_ULP: f32 = 0.99999994;
        const ROBUST_EPSILON: f32 = 2e-7;

        @compute @workgroup_size(256)
        fn cs_main(
            @builtin(global_invocation_id) global_id: vec3<u32>,
        ) {
            let n_lines = atomicLoad(&bump.lines);
            var count = 0u;
            if global_id.x < n_lines {
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
                let stride = bbox.z - bbox.x;
                if s0.y >= f32(bbox.w) || s1.y <= f32(bbox.y) || xmin >= f32(bbox.z) || stride == 0 {
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
                        if is_positive_slope {
                            imax = min(imax, u32(f));
                        } else {
                            imin = max(imin, u32(f));
                        }
                    }
                }
                imax = max(imin, imax);
                ymin = max(ymin, bbox.y);
                ymax = min(ymax, bbox.w);
                for (var y = ymin; y < ymax; y++) {
                    let base = i32(path.tiles) + (y - bbox.y) * stride;
                    atomicAdd(&tile[base].backdrop, delta);
                }
                var last_z = floor(a * (f32(imin) - 1.0) + b);
                let seg_base = atomicAdd(&bump.seg_counts, imax - imin);
                for (var i = imin; i < imax; i++) {
                    let subix = i;
                    let zf = a * f32(subix) + b;
                    let z = floor(zf);
                    let y = i32(y0 + f32(subix) - z);
                    let x = i32(x0 + x_sign * z);
                    let base = i32(path.tiles) + (y - bbox.y) * stride - bbox.x;
                    let top_edge = select(last_z == z, y0 == s0.y, subix == 0u);
                    if top_edge && x + 1 < bbox.z {
                        let x_bump = max(x + 1, bbox.x);
                        atomicAdd(&tile[base + x_bump].backdrop, delta);
                    }
                    let seg_within_slice = atomicAdd(&tile[base + x].segment_count_or_ix, 1u);
                    let counts = (seg_within_slice << 16u) | subix;
                    let seg_count = SegmentCount(line_ix, counts);
                    let seg_ix = seg_base + i - imin;
                    if seg_ix < config.seg_counts_size {
                        seg_counts[seg_ix] = seg_count;
                    }
                    last_z = z;
                }
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
