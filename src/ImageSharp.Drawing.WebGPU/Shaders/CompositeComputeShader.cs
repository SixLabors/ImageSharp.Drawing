// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using Silk.NET.WebGPU;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Composites prepared commands over coverage in tile order to produce the final output.
/// Coverage is computed inline using a fixed-point scanline rasterizer ported from
/// <see cref="DefaultRasterizer"/>, operating per-tile with workgroup shared memory.
/// Shader source is generated per texture format to match sampling/output requirements.
/// </summary>
internal static class CompositeComputeShader
{
    private static readonly object CacheSync = new();
    private static readonly Dictionary<TextureFormat, byte[]> ShaderCache = [];

    private static readonly string ShaderTemplate =
        """
        struct Edge {
            x0: i32,
            y0: i32,
            x1: i32,
            y1: i32,
        }

        struct Params {
            destination_x: u32,
            destination_y: u32,
            destination_width: u32,
            destination_height: u32,
            edge_start: u32,
            fill_rule_value: u32,
            edge_origin_x: i32,
            edge_origin_y: i32,
            csr_offsets_start: u32,
            csr_band_count: u32,
            brush_type: u32,
            brush_origin_x: u32,
            brush_origin_y: u32,
            brush_region_x: u32,
            brush_region_y: u32,
            brush_region_width: u32,
            brush_region_height: u32,
            color_blend_mode: u32,
            alpha_composition_mode: u32,
            blend_percentage: u32,
            solid_r: u32,
            solid_g: u32,
            solid_b: u32,
            solid_a: u32,
        };

        struct DispatchConfig {
            target_width: u32,
            target_height: u32,
            tile_count_x: u32,
            tile_count_y: u32,
            tile_count: u32,
            command_count: u32,
            source_origin_x: u32,
            source_origin_y: u32,
            output_origin_x: u32,
            output_origin_y: u32,
            width_in_bins: u32,
            height_in_bins: u32,
            bin_count: u32,
            partition_count: u32,
            binning_size: u32,
            bin_data_start: u32,
        };

        @group(0) @binding(0) var<storage, read> edges: array<Edge>;
        @group(0) @binding(1) var backdrop_texture: texture_2d<__BACKDROP_TEXEL_TYPE__>;
        @group(0) @binding(2) var brush_texture: texture_2d<__BACKDROP_TEXEL_TYPE__>;
        @group(0) @binding(3) var output_texture: texture_storage_2d<__OUTPUT_FORMAT__, write>;
        @group(0) @binding(4) var<storage, read> commands: array<Params>;
        @group(0) @binding(5) var<uniform> dispatch_config: DispatchConfig;
        @group(0) @binding(6) var<storage, read> band_offsets: array<u32>;

        // Workgroup shared memory for per-tile coverage accumulation.
        // Layout: 16 rows x 16 columns. Index = row * 16 + col.
        var<workgroup> tile_cover: array<atomic<i32>, 256>;
        var<workgroup> tile_area: array<atomic<i32>, 256>;
        var<workgroup> tile_start_cover: array<atomic<i32>, 16>;

        const FIXED_SHIFT: u32 = 8u;
        const FIXED_ONE: i32 = 256;
        const AREA_SHIFT: u32 = 9u;
        const COV_STEPS: i32 = 256;
        const COV_SCALE: f32 = 1.0 / 256.0;
        const EO_MASK: i32 = 511;
        const EO_PERIOD: i32 = 512;

        fn u32_to_f32(bits: u32) -> f32 {
            return bitcast<f32>(bits);
        }

        __DECODE_TEXEL_FUNCTION__

        __ENCODE_OUTPUT_FUNCTION__

        fn unpremultiply(rgb: vec3<f32>, alpha: f32) -> vec3<f32> {
            if (alpha <= 0.0) {
                return vec3<f32>(0.0);
            }

            return rgb / alpha;
        }

        fn blend_color(backdrop: vec3<f32>, source: vec3<f32>, mode: u32) -> vec3<f32> {
            switch mode {
                case 1u: {
                    return backdrop * source;
                }
                case 2u: {
                    return backdrop + source;
                }
                case 3u: {
                    return backdrop - source;
                }
                case 4u: {
                    return 1.0 - ((1.0 - backdrop) * (1.0 - source));
                }
                case 5u: {
                    return min(backdrop, source);
                }
                case 6u: {
                    return max(backdrop, source);
                }
                case 7u: {
                    return select(
                        2.0 * backdrop * source,
                        1.0 - (2.0 * (1.0 - backdrop) * (1.0 - source)),
                        backdrop >= vec3<f32>(0.5));
                }
                case 8u: {
                    return select(
                        2.0 * backdrop * source,
                        1.0 - (2.0 * (1.0 - backdrop) * (1.0 - source)),
                        source >= vec3<f32>(0.5));
                }
                default: {
                    return source;
                }
            }
        }

        fn compose_pixel(destination_premul: vec4<f32>, source: vec4<f32>, color_mode: u32, alpha_mode: u32) -> vec4<f32> {
            let destination_alpha = destination_premul.a;
            let destination_rgb_straight = unpremultiply(destination_premul.rgb, destination_alpha);
            let source_alpha = source.a;
            let source_rgb = source.rgb;
            let source_premul = source_rgb * source_alpha;
            let forward_blend = blend_color(destination_rgb_straight, source_rgb, color_mode);
            let reverse_blend = blend_color(source_rgb, destination_rgb_straight, color_mode);
            let shared_alpha = source_alpha * destination_alpha;

            switch alpha_mode {
                case 1u: {
                    return vec4<f32>(source_premul, source_alpha);
                }
                case 2u: {
                    let premul = (destination_rgb_straight * (destination_alpha - shared_alpha)) + (forward_blend * shared_alpha);
                    return vec4<f32>(premul, destination_alpha);
                }
                case 3u: {
                    let alpha = source_alpha * destination_alpha;
                    return vec4<f32>(source_premul * destination_alpha, alpha);
                }
                case 4u: {
                    let alpha = source_alpha * (1.0 - destination_alpha);
                    return vec4<f32>(source_premul * (1.0 - destination_alpha), alpha);
                }
                case 5u: {
                    return destination_premul;
                }
                case 6u: {
                    let premul = (source_rgb * (source_alpha - shared_alpha)) + (reverse_blend * shared_alpha);
                    return vec4<f32>(premul, source_alpha);
                }
                case 7u: {
                    let alpha = destination_alpha + source_alpha - shared_alpha;
                    let premul =
                        (source_rgb * (source_alpha - shared_alpha)) +
                        (destination_rgb_straight * (destination_alpha - shared_alpha)) +
                        (reverse_blend * shared_alpha);
                    return vec4<f32>(premul, alpha);
                }
                case 8u: {
                    let alpha = destination_alpha * source_alpha;
                    return vec4<f32>(destination_premul.rgb * source_alpha, alpha);
                }
                case 9u: {
                    let alpha = destination_alpha * (1.0 - source_alpha);
                    return vec4<f32>(destination_premul.rgb * (1.0 - source_alpha), alpha);
                }
                case 10u: {
                    return vec4<f32>(0.0, 0.0, 0.0, 0.0);
                }
                case 11u: {
                    let source_term = source_premul * (1.0 - destination_alpha);
                    let destination_term = destination_premul.rgb * (1.0 - source_alpha);
                    let alpha = source_alpha * (1.0 - destination_alpha) + destination_alpha * (1.0 - source_alpha);
                    return vec4<f32>(source_term + destination_term, alpha);
                }
                default: {
                    let alpha = source_alpha + destination_alpha - shared_alpha;
                    let premul =
                        (destination_rgb_straight * (destination_alpha - shared_alpha)) +
                        (source_rgb * (source_alpha - shared_alpha)) +
                        (forward_blend * shared_alpha);
                    return vec4<f32>(premul, alpha);
                }
            }
        }

        fn positive_mod(value: i32, divisor: i32) -> i32 {
            let m = value % divisor;
            return select(m + divisor, m, m >= 0);
        }

        // -----------------------------------------------------------------------
        // Fixed-point scanline rasterizer (ported from DefaultRasterizer)
        // -----------------------------------------------------------------------

        fn find_adjustment(value: i32) -> i32 {
            let lte0 = (~((value - 1) >> 31)) & 1;
            let div256 = (((value & (FIXED_ONE - 1)) - 1) >> 31) & 1;
            return lte0 & div256;
        }

        fn add_cell(row: i32, col: i32, delta: i32, a: i32) {
            if row < 0 || row >= 16 {
                return;
            }
            if col < 0 {
                atomicAdd(&tile_start_cover[row], delta);
                return;
            }
            if col >= 16 {
                return;
            }
            let idx = u32(row) * 16u + u32(col);
            atomicAdd(&tile_cover[idx], delta);
            atomicAdd(&tile_area[idx], a);
        }

        fn cell_vertical(px: i32, py: i32, x: i32, y0: i32, y1: i32) {
            let delta = y0 - y1;
            let a = delta * ((FIXED_ONE * 2) - x - x);
            add_cell(py, px, delta, a);
        }

        fn cell(row: i32, px: i32, x0: i32, y0: i32, x1: i32, y1: i32) {
            let delta = y0 - y1;
            let a = delta * ((FIXED_ONE * 2) - x0 - x1);
            add_cell(row, px, delta, a);
        }

        fn vertical_down(col_index: i32, y0: i32, y1: i32, x: i32) {
            let row0 = y0 >> FIXED_SHIFT;
            let row1 = (y1 - 1) >> FIXED_SHIFT;
            let fy0 = y0 - (row0 << FIXED_SHIFT);
            let fy1 = y1 - (row1 << FIXED_SHIFT);
            let fx = x - (col_index << FIXED_SHIFT);
            if row0 == row1 {
                cell_vertical(col_index, row0, fx, fy0, fy1);
                return;
            }
            cell_vertical(col_index, row0, fx, fy0, FIXED_ONE);
            for (var row = row0 + 1; row < row1; row++) {
                cell_vertical(col_index, row, fx, 0, FIXED_ONE);
            }
            cell_vertical(col_index, row1, fx, 0, fy1);
        }

        fn vertical_up(col_index: i32, y0: i32, y1: i32, x: i32) {
            let row0 = (y0 - 1) >> FIXED_SHIFT;
            let row1 = y1 >> FIXED_SHIFT;
            let fy0 = y0 - (row0 << FIXED_SHIFT);
            let fy1 = y1 - (row1 << FIXED_SHIFT);
            let fx = x - (col_index << FIXED_SHIFT);
            if row0 == row1 {
                cell_vertical(col_index, row0, fx, fy0, fy1);
                return;
            }
            cell_vertical(col_index, row0, fx, fy0, 0);
            for (var row = row0 - 1; row > row1; row--) {
                cell_vertical(col_index, row, fx, FIXED_ONE, 0);
            }
            cell_vertical(col_index, row1, fx, FIXED_ONE, fy1);
        }

        fn row_down_r(row_idx: i32, p0x: i32, p0y: i32, p1x: i32, p1y: i32) {
            let col0 = p0x >> FIXED_SHIFT;
            let col1 = (p1x - 1) >> FIXED_SHIFT;
            let fx0 = p0x - (col0 << FIXED_SHIFT);
            let fx1 = p1x - (col1 << FIXED_SHIFT);
            if col0 == col1 {
                cell(row_idx, col0, fx0, p0y, fx1, p1y);
                return;
            }
            let dx = p1x - p0x;
            let dy = p1y - p0y;
            let pp = (FIXED_ONE - fx0) * dy;
            var cy = p0y + (pp / dx);
            cell(row_idx, col0, fx0, p0y, FIXED_ONE, cy);
            var idx = col0 + 1;
            if idx != col1 {
                var md = (pp % dx) - dx;
                let p = FIXED_ONE * dy;
                let lift = p / dx;
                let rem = p % dx;
                for (; idx != col1; idx++) {
                    var delta = lift;
                    md += rem;
                    if md >= 0 {
                        md -= dx;
                        delta++;
                    }
                    let ny = cy + delta;
                    cell(row_idx, idx, 0, cy, FIXED_ONE, ny);
                    cy = ny;
                }
            }
            cell(row_idx, col1, 0, cy, fx1, p1y);
        }

        fn row_down_r_v(row_idx: i32, p0x: i32, p0y: i32, p1x: i32, p1y: i32) {
            if p0x < p1x {
                row_down_r(row_idx, p0x, p0y, p1x, p1y);
            } else {
                let ci = (p0x - find_adjustment(p0x)) >> FIXED_SHIFT;
                let x = p0x - (ci << FIXED_SHIFT);
                cell_vertical(ci, row_idx, x, p0y, p1y);
            }
        }

        fn row_up_r(row_idx: i32, p0x: i32, p0y: i32, p1x: i32, p1y: i32) {
            let col0 = p0x >> FIXED_SHIFT;
            let col1 = (p1x - 1) >> FIXED_SHIFT;
            let fx0 = p0x - (col0 << FIXED_SHIFT);
            let fx1 = p1x - (col1 << FIXED_SHIFT);
            if col0 == col1 {
                cell(row_idx, col0, fx0, p0y, fx1, p1y);
                return;
            }
            let dx = p1x - p0x;
            let dy = p0y - p1y;
            let pp = (FIXED_ONE - fx0) * dy;
            var cy = p0y - (pp / dx);
            cell(row_idx, col0, fx0, p0y, FIXED_ONE, cy);
            var idx = col0 + 1;
            if idx != col1 {
                var md = (pp % dx) - dx;
                let p = FIXED_ONE * dy;
                let lift = p / dx;
                let rem = p % dx;
                for (; idx != col1; idx++) {
                    var delta = lift;
                    md += rem;
                    if md >= 0 {
                        md -= dx;
                        delta++;
                    }
                    let ny = cy - delta;
                    cell(row_idx, idx, 0, cy, FIXED_ONE, ny);
                    cy = ny;
                }
            }
            cell(row_idx, col1, 0, cy, fx1, p1y);
        }

        fn row_up_r_v(row_idx: i32, p0x: i32, p0y: i32, p1x: i32, p1y: i32) {
            if p0x < p1x {
                row_up_r(row_idx, p0x, p0y, p1x, p1y);
            } else {
                let ci = (p0x - find_adjustment(p0x)) >> FIXED_SHIFT;
                let x = p0x - (ci << FIXED_SHIFT);
                cell_vertical(ci, row_idx, x, p0y, p1y);
            }
        }

        fn row_down_l(row_idx: i32, p0x: i32, p0y: i32, p1x: i32, p1y: i32) {
            let col0 = (p0x - 1) >> FIXED_SHIFT;
            let col1 = p1x >> FIXED_SHIFT;
            let fx0 = p0x - (col0 << FIXED_SHIFT);
            let fx1 = p1x - (col1 << FIXED_SHIFT);
            if col0 == col1 {
                cell(row_idx, col0, fx0, p0y, fx1, p1y);
                return;
            }
            let dx = p0x - p1x;
            let dy = p1y - p0y;
            let pp = fx0 * dy;
            var cy = p0y + (pp / dx);
            cell(row_idx, col0, fx0, p0y, 0, cy);
            var idx = col0 - 1;
            if idx != col1 {
                var md = (pp % dx) - dx;
                let p = FIXED_ONE * dy;
                let lift = p / dx;
                let rem = p % dx;
                for (; idx != col1; idx--) {
                    var delta = lift;
                    md += rem;
                    if md >= 0 {
                        md -= dx;
                        delta++;
                    }
                    let ny = cy + delta;
                    cell(row_idx, idx, FIXED_ONE, cy, 0, ny);
                    cy = ny;
                }
            }
            cell(row_idx, col1, FIXED_ONE, cy, fx1, p1y);
        }

        fn row_down_l_v(row_idx: i32, p0x: i32, p0y: i32, p1x: i32, p1y: i32) {
            if p0x > p1x {
                row_down_l(row_idx, p0x, p0y, p1x, p1y);
            } else {
                let ci = (p0x - find_adjustment(p0x)) >> FIXED_SHIFT;
                let x = p0x - (ci << FIXED_SHIFT);
                cell_vertical(ci, row_idx, x, p0y, p1y);
            }
        }

        fn row_up_l(row_idx: i32, p0x: i32, p0y: i32, p1x: i32, p1y: i32) {
            let col0 = (p0x - 1) >> FIXED_SHIFT;
            let col1 = p1x >> FIXED_SHIFT;
            let fx0 = p0x - (col0 << FIXED_SHIFT);
            let fx1 = p1x - (col1 << FIXED_SHIFT);
            if col0 == col1 {
                cell(row_idx, col0, fx0, p0y, fx1, p1y);
                return;
            }
            let dx = p0x - p1x;
            let dy = p0y - p1y;
            let pp = fx0 * dy;
            var cy = p0y - (pp / dx);
            cell(row_idx, col0, fx0, p0y, 0, cy);
            var idx = col0 - 1;
            if idx != col1 {
                var md = (pp % dx) - dx;
                let p = FIXED_ONE * dy;
                let lift = p / dx;
                let rem = p % dx;
                for (; idx != col1; idx--) {
                    var delta = lift;
                    md += rem;
                    if md >= 0 {
                        md -= dx;
                        delta++;
                    }
                    let ny = cy - delta;
                    cell(row_idx, idx, FIXED_ONE, cy, 0, ny);
                    cy = ny;
                }
            }
            cell(row_idx, col1, FIXED_ONE, cy, fx1, p1y);
        }

        fn row_up_l_v(row_idx: i32, p0x: i32, p0y: i32, p1x: i32, p1y: i32) {
            if p0x > p1x {
                row_up_l(row_idx, p0x, p0y, p1x, p1y);
            } else {
                let ci = (p0x - find_adjustment(p0x)) >> FIXED_SHIFT;
                let x = p0x - (ci << FIXED_SHIFT);
                cell_vertical(ci, row_idx, x, p0y, p1y);
            }
        }

        fn line_down_r(row0: i32, row1: i32, x0: i32, y0: i32, x1: i32, y1: i32) {
            let dx = x1 - x0;
            let dy = y1 - y0;
            let fy0 = y0 - (row0 << FIXED_SHIFT);
            let fy1 = y1 - (row1 << FIXED_SHIFT);
            let p_init = (FIXED_ONE - fy0) * dx;
            let delta_init = p_init / dy;
            var cx = x0 + delta_init;
            row_down_r_v(row0, x0, fy0, cx, FIXED_ONE);
            var row = row0 + 1;
            if row != row1 {
                var md = (p_init % dy) - dy;
                let p = FIXED_ONE * dx;
                let lift = p / dy;
                let rem = p % dy;
                for (; row != row1; row++) {
                    var delta = lift;
                    md += rem;
                    if md >= 0 {
                        md -= dy;
                        delta++;
                    }
                    let nx = cx + delta;
                    row_down_r_v(row, cx, 0, nx, FIXED_ONE);
                    cx = nx;
                }
            }
            row_down_r_v(row1, cx, 0, x1, fy1);
        }

        fn line_up_r(row0: i32, row1: i32, x0: i32, y0: i32, x1: i32, y1: i32) {
            let dx = x1 - x0;
            let dy = y0 - y1;
            let fy0 = y0 - (row0 << FIXED_SHIFT);
            let fy1 = y1 - (row1 << FIXED_SHIFT);
            let p_init = fy0 * dx;
            let delta_init = p_init / dy;
            var cx = x0 + delta_init;
            row_up_r_v(row0, x0, fy0, cx, 0);
            var row = row0 - 1;
            if row != row1 {
                var md = (p_init % dy) - dy;
                let p = FIXED_ONE * dx;
                let lift = p / dy;
                let rem = p % dy;
                for (; row != row1; row--) {
                    var delta = lift;
                    md += rem;
                    if md >= 0 {
                        md -= dy;
                        delta++;
                    }
                    let nx = cx + delta;
                    row_up_r_v(row, cx, FIXED_ONE, nx, 0);
                    cx = nx;
                }
            }
            row_up_r_v(row1, cx, FIXED_ONE, x1, fy1);
        }

        fn line_down_l(row0: i32, row1: i32, x0: i32, y0: i32, x1: i32, y1: i32) {
            let dx = x0 - x1;
            let dy = y1 - y0;
            let fy0 = y0 - (row0 << FIXED_SHIFT);
            let fy1 = y1 - (row1 << FIXED_SHIFT);
            let p_init = (FIXED_ONE - fy0) * dx;
            let delta_init = p_init / dy;
            var cx = x0 - delta_init;
            row_down_l_v(row0, x0, fy0, cx, FIXED_ONE);
            var row = row0 + 1;
            if row != row1 {
                var md = (p_init % dy) - dy;
                let p = FIXED_ONE * dx;
                let lift = p / dy;
                let rem = p % dy;
                for (; row != row1; row++) {
                    var delta = lift;
                    md += rem;
                    if md >= 0 {
                        md -= dy;
                        delta++;
                    }
                    let nx = cx - delta;
                    row_down_l_v(row, cx, 0, nx, FIXED_ONE);
                    cx = nx;
                }
            }
            row_down_l_v(row1, cx, 0, x1, fy1);
        }

        fn line_up_l(row0: i32, row1: i32, x0: i32, y0: i32, x1: i32, y1: i32) {
            let dx = x0 - x1;
            let dy = y0 - y1;
            let fy0 = y0 - (row0 << FIXED_SHIFT);
            let fy1 = y1 - (row1 << FIXED_SHIFT);
            let p_init = fy0 * dx;
            let delta_init = p_init / dy;
            var cx = x0 - delta_init;
            row_up_l_v(row0, x0, fy0, cx, 0);
            var row = row0 - 1;
            if row != row1 {
                var md = (p_init % dy) - dy;
                let p = FIXED_ONE * dx;
                let lift = p / dy;
                let rem = p % dy;
                for (; row != row1; row--) {
                    var delta = lift;
                    md += rem;
                    if md >= 0 {
                        md -= dy;
                        delta++;
                    }
                    let nx = cx - delta;
                    row_up_l_v(row, cx, FIXED_ONE, nx, 0);
                    cx = nx;
                }
            }
            row_up_l_v(row1, cx, FIXED_ONE, x1, fy1);
        }

        fn rasterize_line(x0: i32, y0: i32, x1: i32, y1: i32) {
            if x0 == x1 {
                let ci = (x0 - find_adjustment(x0)) >> FIXED_SHIFT;
                if y0 < y1 {
                    vertical_down(ci, y0, y1, x0);
                } else {
                    vertical_up(ci, y0, y1, x0);
                }
                return;
            }
            if y0 < y1 {
                let r0 = y0 >> FIXED_SHIFT;
                let r1 = (y1 - 1) >> FIXED_SHIFT;
                if r0 == r1 {
                    let base_y = r0 << FIXED_SHIFT;
                    if x0 < x1 {
                        row_down_r(r0, x0, y0 - base_y, x1, y1 - base_y);
                    } else {
                        row_down_l(r0, x0, y0 - base_y, x1, y1 - base_y);
                    }
                } else if x0 < x1 {
                    line_down_r(r0, r1, x0, y0, x1, y1);
                } else {
                    line_down_l(r0, r1, x0, y0, x1, y1);
                }
                return;
            }
            let r0 = (y0 - 1) >> FIXED_SHIFT;
            let r1 = y1 >> FIXED_SHIFT;
            if r0 == r1 {
                let base_y = r0 << FIXED_SHIFT;
                if x0 < x1 {
                    row_up_r(r0, x0, y0 - base_y, x1, y1 - base_y);
                } else {
                    row_up_l(r0, x0, y0 - base_y, x1, y1 - base_y);
                }
            } else if x0 < x1 {
                line_up_r(r0, r1, x0, y0, x1, y1);
            } else {
                line_up_l(r0, r1, x0, y0, x1, y1);
            }
        }

        fn clip_test(p: f32, q: f32, t0_in: f32, t1_in: f32) -> vec3<f32> {
            // Returns (t0, t1, valid) where valid > 0 means the segment is not rejected.
            if p == 0.0 {
                if q >= 0.0 {
                    return vec3<f32>(t0_in, t1_in, 1.0);
                }
                return vec3<f32>(t0_in, t1_in, -1.0);
            }
            let r = q / p;
            if p < 0.0 {
                if r > t1_in {
                    return vec3<f32>(t0_in, t1_in, -1.0);
                }
                return vec3<f32>(max(t0_in, r), t1_in, 1.0);
            }
            // p > 0
            if r < t0_in {
                return vec3<f32>(t0_in, t1_in, -1.0);
            }
            return vec3<f32>(t0_in, min(t1_in, r), 1.0);
        }

        struct ClippedEdge {
            x0: i32,
            y0: i32,
            x1: i32,
            y1: i32,
            valid: i32,
        }

        fn clip_vertical(ex0: i32, ey0: i32, ex1: i32, ey1: i32, min_y: i32, max_y: i32) -> ClippedEdge {
            var t0 = 0.0;
            var t1 = 1.0;
            let ox = f32(ex0);
            let oy = f32(ey0);
            let dx = f32(ex1 - ex0);
            let dy = f32(ey1 - ey0);
            let res1 = clip_test(-dy, oy - f32(min_y), t0, t1);
            if res1.z < 0.0 {
                return ClippedEdge(ex0, ey0, ex1, ey1, 0);
            }
            t0 = res1.x;
            t1 = res1.y;
            let res2 = clip_test(dy, f32(max_y) - oy, t0, t1);
            if res2.z < 0.0 {
                return ClippedEdge(ex0, ey0, ex1, ey1, 0);
            }
            t0 = res2.x;
            t1 = res2.y;
            var rx0 = ex0;
            var ry0 = ey0;
            var rx1 = ex1;
            var ry1 = ey1;
            if t1 < 1.0 {
                rx1 = i32(round(ox + dx * t1));
                ry1 = i32(round(oy + dy * t1));
            }
            if t0 > 0.0 {
                rx0 = i32(round(ox + dx * t0));
                ry0 = i32(round(oy + dy * t0));
            }
            if ry0 == ry1 {
                return ClippedEdge(rx0, ry0, rx1, ry1, 0);
            }
            return ClippedEdge(rx0, ry0, rx1, ry1, 1);
        }

        fn accumulate_start_cover(ey0: i32, ey1: i32, clip_top: i32, clip_bottom: i32, tile_top_fixed: i32) {
            // Fast path for edges entirely left of the tile.
            // Only start_cover is affected (no area). The total cover delta per row
            // is the signed height of the edge within that row, which telescopes
            // across columns. This avoids the full column-walking overhead.
            var cy0 = clamp(ey0, clip_top, clip_bottom);
            var cy1 = clamp(ey1, clip_top, clip_bottom);
            if cy0 == cy1 { return; }

            let ly0 = cy0 - tile_top_fixed;
            let ly1 = cy1 - tile_top_fixed;

            if ly0 < ly1 {
                // Downward.
                let row0 = ly0 >> FIXED_SHIFT;
                let row1 = (ly1 - 1) >> FIXED_SHIFT;
                let fy0 = ly0 - (row0 << FIXED_SHIFT);
                let fy1 = ly1 - (row1 << FIXED_SHIFT);
                if row0 == row1 {
                    atomicAdd(&tile_start_cover[row0], fy0 - fy1);
                    return;
                }
                atomicAdd(&tile_start_cover[row0], fy0 - FIXED_ONE);
                for (var r = row0 + 1; r < row1; r++) {
                    atomicAdd(&tile_start_cover[r], -FIXED_ONE);
                }
                atomicAdd(&tile_start_cover[row1], -fy1);
            } else {
                // Upward.
                let row0 = (ly0 - 1) >> FIXED_SHIFT;
                let row1 = ly1 >> FIXED_SHIFT;
                let fy0 = ly0 - (row0 << FIXED_SHIFT);
                let fy1 = ly1 - (row1 << FIXED_SHIFT);
                if row0 == row1 {
                    atomicAdd(&tile_start_cover[row0], fy0 - fy1);
                    return;
                }
                atomicAdd(&tile_start_cover[row0], fy0);
                for (var r = row0 - 1; r > row1; r--) {
                    atomicAdd(&tile_start_cover[r], FIXED_ONE);
                }
                atomicAdd(&tile_start_cover[row1], FIXED_ONE - fy1);
            }
        }

        fn rasterize_edge(edge: Edge, band_top: i32, band_left_fixed: i32, clip_top_fixed: i32, clip_bottom_fixed: i32) {
            let band_top_fixed = band_top << FIXED_SHIFT;
            let ex0 = edge.x0 - band_left_fixed;
            let ey0 = edge.y0;
            let ex1 = edge.x1 - band_left_fixed;
            let ey1 = edge.y1;
            if ey0 >= clip_top_fixed && ey0 < clip_bottom_fixed && ey1 >= clip_top_fixed && ey1 < clip_bottom_fixed {
                rasterize_line(ex0, ey0 - band_top_fixed, ex1, ey1 - band_top_fixed);
                return;
            }
            let clipped = clip_vertical(ex0, ey0, ex1, ey1, clip_top_fixed, clip_bottom_fixed);
            if clipped.valid == 0 {
                return;
            }
            rasterize_line(clipped.x0, clipped.y0 - band_top_fixed, clipped.x1, clipped.y1 - band_top_fixed);
        }

        fn area_to_coverage(area_val: i32, fill_rule: u32) -> f32 {
            let signed_area = area_val >> AREA_SHIFT;
            var abs_area: i32;
            if signed_area < 0 {
                abs_area = -signed_area;
            } else {
                abs_area = signed_area;
            }
            var coverage: f32;
            if fill_rule == 0u {
                // Non-zero winding
                if abs_area >= COV_STEPS {
                    coverage = 1.0;
                } else {
                    coverage = f32(abs_area) * COV_SCALE;
                }
            } else {
                // Even-odd
                var wrapped = abs_area & EO_MASK;
                if wrapped > COV_STEPS {
                    wrapped = EO_PERIOD - wrapped;
                }
                if wrapped >= COV_STEPS {
                    coverage = 1.0;
                } else {
                    coverage = f32(wrapped) * COV_SCALE;
                }
            }
            return coverage;
        }

        // -----------------------------------------------------------------------
        // Main entry point
        // -----------------------------------------------------------------------

        @compute @workgroup_size(16, 16, 1)
        fn cs_main(
            @builtin(local_invocation_id) local_id: vec3<u32>,
            @builtin(workgroup_id) wg_id: vec3<u32>
        ) {
            let tile_x = wg_id.x;
            let tile_y = wg_id.y;
            let tile_index = tile_y * dispatch_config.tile_count_x + tile_x;
            if tile_index >= dispatch_config.tile_count {
                return;
            }

            let px = local_id.x;
            let py = local_id.y;
            let thread_id = py * 16u + px;

            let dest_x = tile_x * 16u + px;
            let dest_y = tile_y * 16u + py;

            let in_bounds = dest_x < dispatch_config.target_width && dest_y < dispatch_config.target_height;

            let source_x = i32(dest_x + dispatch_config.source_origin_x);
            let source_y = i32(dest_y + dispatch_config.source_origin_y);
            let output_x_i32 = i32(dest_x + dispatch_config.output_origin_x);
            let output_y_i32 = i32(dest_y + dispatch_config.output_origin_y);

            var destination: vec4<f32>;
            if in_bounds {
                let source = __LOAD_BACKDROP__;
                destination = vec4<f32>(source.rgb * source.a, source.a);
            }

            let dest_x_i32 = i32(dest_x);
            let dest_y_i32 = i32(dest_y);
            let tile_min_x = i32(tile_x * 16u);
            let tile_min_y = i32(tile_y * 16u);
            let tile_max_x = tile_min_x + 16;
            let tile_max_y = tile_min_y + 16;

            for (var command_index = 0u; command_index < dispatch_config.command_count; command_index++) {
                let command = commands[command_index];

                // Tile vs command bounding box check (uniform across workgroup).
                let cmd_min_x = bitcast<i32>(command.destination_x);
                let cmd_min_y = bitcast<i32>(command.destination_y);
                let cmd_max_x = cmd_min_x + i32(command.destination_width);
                let cmd_max_y = cmd_min_y + i32(command.destination_height);
                if tile_max_x <= cmd_min_x || tile_min_x >= cmd_max_x || tile_max_y <= cmd_min_y || tile_min_y >= cmd_max_y {
                    continue;
                }

                // Determine this tile's position in coverage-local space.
                let band_top = tile_min_y - command.edge_origin_y;
                let band_left_fixed = (tile_min_x - command.edge_origin_x) << FIXED_SHIFT;

                // Band lookup: when edge_origin_y is 16-aligned the tile maps to one band;
                // otherwise it can overlap two bands.
                var first_band = band_top / 16;
                if band_top < 0 && (band_top % 16) != 0 {
                    first_band -= 1;
                }
                first_band = max(first_band, 0);
                let last_band = min((band_top + 15) / 16, i32(command.csr_band_count) - 1);

                if first_band > last_band {
                    continue;
                }

                let edge_range_start = band_offsets[command.csr_offsets_start + u32(first_band)];
                let edge_range_end = band_offsets[command.csr_offsets_start + u32(last_band) + 1u];
                if edge_range_start == edge_range_end {
                    continue;
                }

                // Clear shared coverage memory.
                atomicStore(&tile_cover[thread_id], 0);
                atomicStore(&tile_area[thread_id], 0);
                if px == 0u {
                    atomicStore(&tile_start_cover[py], 0);
                }
                workgroupBarrier();

                // Cooperatively rasterize edges from each overlapping band.
                let tile_top_fixed = band_top << FIXED_SHIFT;
                let tile_bottom_fixed = tile_top_fixed + (i32(16) << FIXED_SHIFT);
                let tile_right_fixed = band_left_fixed + (i32(16) << FIXED_SHIFT);

                for (var band = first_band; band <= last_band; band++) {
                    let b_start = band_offsets[command.csr_offsets_start + u32(band)];
                    let b_end = band_offsets[command.csr_offsets_start + u32(band) + 1u];
                    let b_count = b_end - b_start;

                    let csr_band_top_fixed = band * (i32(16) << FIXED_SHIFT);
                    let csr_band_bottom_fixed = csr_band_top_fixed + (i32(16) << FIXED_SHIFT);
                    let clip_top = max(tile_top_fixed, csr_band_top_fixed);
                    let clip_bottom = min(tile_bottom_fixed, csr_band_bottom_fixed);

                    var ei = thread_id;
                    loop {
                        if ei >= b_count {
                            break;
                        }
                        let edge = edges[command.edge_start + b_start + ei];
                        if min(edge.x0, edge.x1) >= tile_right_fixed {
                        } else if max(edge.x0, edge.x1) < band_left_fixed {
                            accumulate_start_cover(edge.y0, edge.y1, clip_top, clip_bottom, tile_top_fixed);
                        } else {
                            rasterize_edge(edge, band_top, band_left_fixed, clip_top, clip_bottom);
                        }
                        ei += 256u;
                    }
                }
                workgroupBarrier();

                // Compute coverage and compose for this pixel.
                if in_bounds {
                    if dest_x_i32 >= cmd_min_x && dest_x_i32 < cmd_max_x && dest_y_i32 >= cmd_min_y && dest_y_i32 < cmd_max_y {
                        var cover = atomicLoad(&tile_start_cover[py]);
                        for (var col = 0u; col < px; col++) {
                            cover += atomicLoad(&tile_cover[py * 16u + col]);
                        }
                        let area_val = atomicLoad(&tile_area[py * 16u + px]) + (cover << AREA_SHIFT);
                        let coverage_value = area_to_coverage(area_val, command.fill_rule_value);

                        if coverage_value > 0.0 {
                            let blend_percentage = u32_to_f32(command.blend_percentage);
                            let effective_coverage = coverage_value * blend_percentage;

                            var brush = vec4<f32>(
                                u32_to_f32(command.solid_r),
                                u32_to_f32(command.solid_g),
                                u32_to_f32(command.solid_b),
                                u32_to_f32(command.solid_a));

                            if command.brush_type == 1u {
                                let origin_x = bitcast<i32>(command.brush_origin_x);
                                let origin_y = bitcast<i32>(command.brush_origin_y);
                                let region_x = i32(command.brush_region_x);
                                let region_y = i32(command.brush_region_y);
                                let region_w = i32(command.brush_region_width);
                                let region_h = i32(command.brush_region_height);
                                let sample_x = positive_mod(dest_x_i32 - origin_x, region_w) + region_x;
                                let sample_y = positive_mod(dest_y_i32 - origin_y, region_h) + region_y;
                                brush = __LOAD_BRUSH__;
                            }

                            let src = vec4<f32>(brush.rgb, brush.a * effective_coverage);
                            destination = compose_pixel(destination, src, command.color_blend_mode, command.alpha_composition_mode);
                        }
                    }
                }
                workgroupBarrier();
            }

            if in_bounds {
                let alpha = destination.a;
                let rgb = unpremultiply(destination.rgb, alpha);
                __STORE_OUTPUT__
            }
        }
        """;

    /// <summary>
    /// Gets the input sample type required for the fine composite shader variant.
    /// </summary>
    public static bool TryGetInputSampleType(TextureFormat textureFormat, out TextureSampleType sampleType)
    {
        if (TryGetTraits(textureFormat, out ShaderTraits traits))
        {
            sampleType = traits.SampleType;
            return true;
        }

        sampleType = default;
        return false;
    }

    /// <summary>
    /// Gets the null-terminated WGSL source for the fine composite shader variant.
    /// </summary>
    public static bool TryGetCode(TextureFormat textureFormat, out byte[] code, out string? error)
    {
        if (!TryGetTraits(textureFormat, out ShaderTraits traits))
        {
            code = [];
            error = $"Prepared composite fine shader does not support texture format '{textureFormat}'.";
            return false;
        }

        lock (CacheSync)
        {
            if (ShaderCache.TryGetValue(textureFormat, out byte[]? cachedCode))
            {
                code = cachedCode;
                error = null;
                return true;
            }

            string source = ShaderTemplate
                .Replace("__BACKDROP_TEXEL_TYPE__", traits.BackdropTexelType, StringComparison.Ordinal)
                .Replace("__OUTPUT_FORMAT__", traits.OutputFormat, StringComparison.Ordinal)
                .Replace("__DECODE_TEXEL_FUNCTION__", traits.DecodeTexelFunction, StringComparison.Ordinal)
                .Replace("__ENCODE_OUTPUT_FUNCTION__", traits.EncodeOutputFunction, StringComparison.Ordinal)
                .Replace("__LOAD_BACKDROP__", traits.LoadBackdropExpression, StringComparison.Ordinal)
                .Replace("__LOAD_BRUSH__", traits.LoadBrushExpression, StringComparison.Ordinal)
                .Replace("__STORE_OUTPUT__", traits.StoreOutputStatement, StringComparison.Ordinal);

            byte[] sourceBytes = Encoding.UTF8.GetBytes(source);
            code = new byte[sourceBytes.Length + 1];
            sourceBytes.CopyTo(code, 0);
            code[^1] = 0;
            ShaderCache[textureFormat] = code;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Resolves shader traits for the provided texture format.
    /// </summary>
    private static bool TryGetTraits(TextureFormat textureFormat, out ShaderTraits traits)
    {
        switch (textureFormat)
        {
            case TextureFormat.R8Unorm:
                traits = CreateFloatTraits("r8unorm");
                return true;
            case TextureFormat.RG8Unorm:
                traits = CreateFloatTraits("rg8unorm");
                return true;
            case TextureFormat.Rgba8Unorm:
                traits = CreateFloatTraits("rgba8unorm");
                return true;
            case TextureFormat.Bgra8Unorm:
                traits = CreateFloatTraits("bgra8unorm");
                return true;
            case TextureFormat.Rgb10A2Unorm:
                traits = CreateFloatTraits("rgb10a2unorm");
                return true;
            case TextureFormat.R16float:
                traits = CreateFloatTraits("r16float");
                return true;
            case TextureFormat.RG16float:
                traits = CreateFloatTraits("rg16float");
                return true;
            case TextureFormat.Rgba16float:
                traits = CreateFloatTraits("rgba16float");
                return true;
            case TextureFormat.Rgba32float:
                traits = CreateFloatTraits("rgba32float");
                return true;
            case TextureFormat.RG8Snorm:
                traits = CreateSnormTraits("rg8snorm");
                return true;
            case TextureFormat.Rgba8Snorm:
                traits = CreateSnormTraits("rgba8snorm");
                return true;
            case TextureFormat.Rgba8Uint:
                traits = CreateUintTraits("rgba8uint", 255F);
                return true;
            case TextureFormat.R16Uint:
                traits = CreateUintTraits("r16uint", 65535F);
                return true;
            case TextureFormat.RG16Uint:
                traits = CreateUintTraits("rg16uint", 65535F);
                return true;
            case TextureFormat.Rgba16Uint:
                traits = CreateUintTraits("rgba16uint", 65535F);
                return true;
            case TextureFormat.RG16Sint:
                traits = CreateSintTraits("rg16sint", -32768F, 32767F);
                return true;
            case TextureFormat.Rgba16Sint:
                traits = CreateSintTraits("rgba16sint", -32768F, 32767F);
                return true;
            default:
                traits = default;
                return false;
        }
    }

    private static ShaderTraits CreateFloatTraits(string outputFormat)
    {
        const string decodeTexel =
            """
            fn decode_texel(texel: vec4<f32>) -> vec4<f32> {
                return texel;
            }
            """;

        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<f32> {
                return color;
            }
            """;

        return new ShaderTraits(
            outputFormat,
            "f32",
            TextureSampleType.Float,
            decodeTexel,
            encodeOutput,
            "decode_texel(textureLoad(backdrop_texture, vec2<i32>(source_x, source_y), 0))",
            "decode_texel(textureLoad(brush_texture, vec2<i32>(sample_x, sample_y), 0))",
            "textureStore(output_texture, vec2<i32>(output_x_i32, output_y_i32), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static ShaderTraits CreateSnormTraits(string outputFormat)
    {
        const string decodeTexel =
            """
            fn decode_texel(texel: vec4<f32>) -> vec4<f32> {
                return (texel * 0.5) + vec4<f32>(0.5);
            }
            """;

        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<f32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return (clamped * 2.0) - vec4<f32>(1.0);
            }
            """;

        return new ShaderTraits(
            outputFormat,
            "f32",
            TextureSampleType.Float,
            decodeTexel,
            encodeOutput,
            "decode_texel(textureLoad(backdrop_texture, vec2<i32>(source_x, source_y), 0))",
            "decode_texel(textureLoad(brush_texture, vec2<i32>(sample_x, sample_y), 0))",
            "textureStore(output_texture, vec2<i32>(output_x_i32, output_y_i32), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static ShaderTraits CreateUintTraits(string outputFormat, float maxValue)
    {
        string maxVector = $"vec4<f32>({maxValue:F1}, {maxValue:F1}, {maxValue:F1}, {maxValue:F1})";
        string decodeTexel = $@"const UINT_TEXEL_MAX: vec4<f32> = {maxVector};
fn decode_texel(texel: vec4<u32>) -> vec4<f32> {{
    return vec4<f32>(texel) / UINT_TEXEL_MAX;
}}";
        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<u32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return vec4<u32>(round(clamped * UINT_TEXEL_MAX));
            }
            """;

        return new ShaderTraits(
            outputFormat,
            "u32",
            TextureSampleType.Uint,
            decodeTexel,
            encodeOutput,
            "decode_texel(textureLoad(backdrop_texture, vec2<i32>(source_x, source_y), 0))",
            "decode_texel(textureLoad(brush_texture, vec2<i32>(sample_x, sample_y), 0))",
            "textureStore(output_texture, vec2<i32>(output_x_i32, output_y_i32), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private static ShaderTraits CreateSintTraits(string outputFormat, float minValue, float maxValue)
    {
        string minVector = $"vec4<f32>({minValue:F1}, {minValue:F1}, {minValue:F1}, {minValue:F1})";
        string maxVector = $"vec4<f32>({maxValue:F1}, {maxValue:F1}, {maxValue:F1}, {maxValue:F1})";
        string decodeTexel = $@"const SINT_TEXEL_MIN: vec4<f32> = {minVector};
const SINT_TEXEL_MAX: vec4<f32> = {maxVector};
const SINT_TEXEL_RANGE: vec4<f32> = SINT_TEXEL_MAX - SINT_TEXEL_MIN;
fn decode_texel(texel: vec4<i32>) -> vec4<f32> {{
    return (vec4<f32>(texel) - SINT_TEXEL_MIN) / SINT_TEXEL_RANGE;
}}";
        const string encodeOutput =
            """
            fn encode_output(color: vec4<f32>) -> vec4<i32> {
                let clamped = clamp(color, vec4<f32>(0.0), vec4<f32>(1.0));
                return vec4<i32>(round((clamped * SINT_TEXEL_RANGE) + SINT_TEXEL_MIN));
            }
            """;

        return new ShaderTraits(
            outputFormat,
            "i32",
            TextureSampleType.Sint,
            decodeTexel,
            encodeOutput,
            "decode_texel(textureLoad(backdrop_texture, vec2<i32>(source_x, source_y), 0))",
            "decode_texel(textureLoad(brush_texture, vec2<i32>(sample_x, sample_y), 0))",
            "textureStore(output_texture, vec2<i32>(output_x_i32, output_y_i32), encode_output(vec4<f32>(rgb, alpha)));");
    }

    private readonly struct ShaderTraits(
        string outputFormat,
        string backdropTexelType,
        TextureSampleType sampleType,
        string decodeTexelFunction,
        string encodeOutputFunction,
        string loadBackdropExpression,
        string loadBrushExpression,
        string storeOutputStatement)
    {
        public string OutputFormat { get; } = outputFormat;

        public string BackdropTexelType { get; } = backdropTexelType;

        public TextureSampleType SampleType { get; } = sampleType;

        public string DecodeTexelFunction { get; } = decodeTexelFunction;

        public string EncodeOutputFunction { get; } = encodeOutputFunction;

        public string LoadBackdropExpression { get; } = loadBackdropExpression;

        public string LoadBrushExpression { get; } = loadBrushExpression;

        public string StoreOutputStatement { get; } = storeOutputStatement;
    }
}
