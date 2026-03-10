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
            flags: i32,
            adj_x: i32,
            adj_y: i32,
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
            gp0: u32,
            gp1: u32,
            gp2: u32,
            gp3: u32,
            rasterization_mode: u32,
            antialias_threshold: u32,
            gp4: u32,
            gp5: u32,
            gp6: u32,
            gp7: u32,
            stops_offset: u32,
            stop_count: u32,
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

        struct ColorStop {
            ratio: f32,
            r: f32,
            g: f32,
            b: f32,
            a: f32,
        };

        @group(0) @binding(7) var<storage, read> color_stops: array<ColorStop>;

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

        // Brush type constants. Must match PreparedBrushType in WebGPUDrawingBackend.cs.
        const BRUSH_SOLID: u32 = 0u;
        const BRUSH_IMAGE: u32 = 1u;
        const BRUSH_LINEAR_GRADIENT: u32 = 2u;
        const BRUSH_RADIAL_GRADIENT: u32 = 3u;
        const BRUSH_RADIAL_GRADIENT_TWO_CIRCLE: u32 = 4u;
        const BRUSH_ELLIPTIC_GRADIENT: u32 = 5u;
        const BRUSH_SWEEP_GRADIENT: u32 = 6u;
        const BRUSH_PATTERN: u32 = 7u;
        const BRUSH_RECOLOR: u32 = 8u;

        fn u32_to_f32(bits: u32) -> f32 {
            return bitcast<f32>(bits);
        }

        // Exact copy of C# GradientBrushApplicator.this[x, y] color sampling.
        // Combines repetition mode + GetGradientSegment + lerp into one function.
        // Returns vec4(0) with alpha=0 for DontFill outside [0,1].
        fn sample_brush_gradient(raw_t: f32, mode: u32, offset: u32, count: u32) -> vec4<f32> {
            if count == 0u { return vec4<f32>(0.0); }

            var t = raw_t;

            // C# switch (this.repetitionMode)
            if mode == 1u {
                // Repeat: positionOnCompleteGradient %= 1;
                t = t % 1.0;
            } else if mode == 2u {
                // Reflect: positionOnCompleteGradient %= 2;
                // if (positionOnCompleteGradient > 1) { positionOnCompleteGradient = 2 - positionOnCompleteGradient; }
                t = t % 2.0;
                if t > 1.0 { t = 2.0 - t; }
            } else if mode == 3u {
                // DontFill: if (positionOnCompleteGradient is > 1 or < 0) { return Transparent; }
                if t < 0.0 || t > 1.0 { return vec4<f32>(0.0); }
            }
            // mode 0 (None): do nothing

            if count == 1u {
                let s = color_stops[offset];
                return vec4<f32>(s.r, s.g, s.b, s.a);
            }

            // C# GetGradientSegment
            // ColorStop localGradientFrom = this.colorStops[0];
            // ColorStop localGradientTo = default;
            // foreach (ColorStop colorStop in this.colorStops)
            // {
            //     localGradientTo = colorStop;
            //     if (colorStop.Ratio > positionOnCompleteGradient) { break; }
            //     localGradientFrom = localGradientTo;
            // }
            var from_idx = 0u;
            var to_idx = 0u;
            for (var i = 0u; i < count; i++) {
                to_idx = i;
                if color_stops[offset + i].ratio > t {
                    break;
                }
                from_idx = i;
            }

            let from_stop = color_stops[offset + from_idx];
            let to_stop = color_stops[offset + to_idx];

            // C#: if (from.Color.Equals(to.Color)) { return from.Color.ToPixel<TPixel>(); }
            let from_color = vec4<f32>(from_stop.r, from_stop.g, from_stop.b, from_stop.a);
            let to_color = vec4<f32>(to_stop.r, to_stop.g, to_stop.b, to_stop.a);
            if all(from_color == to_color) {
                return from_color;
            }

            // C#: float onLocalGradient = (positionOnCompleteGradient - from.Ratio) / (to.Ratio - from.Ratio);
            let range = to_stop.ratio - from_stop.ratio;
            let local_t = (t - from_stop.ratio) / range;

            // C#: Vector4.Lerp(from.Color.ToScaledVector4(), to.Color.ToScaledVector4(), onLocalGradient)
            return mix(from_color, to_color, local_t);
        }

        // Linear gradient: project pixel onto gradient axis.
        fn linear_gradient_t(x: f32, y: f32, cmd: Params) -> f32 {
            let start_x = u32_to_f32(cmd.gp0);
            let start_y = u32_to_f32(cmd.gp1);
            let end_x = u32_to_f32(cmd.gp2);
            let end_y = u32_to_f32(cmd.gp3);
            let along_x = end_x - start_x;
            let along_y = end_y - start_y;
            let along_sq = along_x * along_x + along_y * along_y;
            if along_sq < 1e-12 { return 0.0; }
            let dx = x - start_x;
            let dy = y - start_y;
            return (dx * along_x + dy * along_y) / along_sq;
        }

        // Single-circle radial gradient.
        // gp0=cx, gp1=cy, gp2=radius, gp3=repetition_mode
        fn radial_gradient_t(x: f32, y: f32, cmd: Params) -> f32 {
            let cx = u32_to_f32(cmd.gp0);
            let cy = u32_to_f32(cmd.gp1);
            let radius = u32_to_f32(cmd.gp2);
            if radius < 1e-20 { return 0.0; }
            return length(vec2<f32>(x - cx, y - cy)) / radius;
        }

        // Two-circle radial gradient.
        // gp0=c0.x, gp1=c0.y, gp2=c1.x, gp3=c1.y, gp4=r0, gp5=r1
        fn radial_gradient_two_t(x: f32, y: f32, cmd: Params) -> f32 {
            let c0x = u32_to_f32(cmd.gp0);
            let c0y = u32_to_f32(cmd.gp1);
            let c1x = u32_to_f32(cmd.gp2);
            let c1y = u32_to_f32(cmd.gp3);
            let r0 = u32_to_f32(cmd.gp4);
            let r1 = u32_to_f32(cmd.gp5);

            let dx_c = c1x - c0x;
            let dy_c = c1y - c0y;
            let dr = r1 - r0;
            let dd = dx_c * dx_c + dy_c * dy_c;
            let denom = dd - dr * dr;

            let qx = x - c0x;
            let qy = y - c0y;

            // Concentric case (centers equal) or degenerate (denom == 0).
            if dd < 1e-10 || abs(denom) < 1e-10 {
                let dist = length(vec2<f32>(qx, qy));
                let abs_dr = max(abs(dr), 1e-20);
                return (dist - r0) / abs_dr;
            }

            // General case: t = (q·d - r0*dr) / denom.
            let num = qx * dx_c + qy * dy_c - r0 * dr;
            return num / denom;
        }

        // Elliptic gradient. Computes rotation and radii from raw brush properties.
        // gp0=center.x, gp1=center.y, gp2=refEnd.x, gp3=refEnd.y, gp4=axisRatio
        fn elliptic_gradient_t(x: f32, y: f32, cmd: Params) -> f32 {
            let cx = u32_to_f32(cmd.gp0);
            let cy = u32_to_f32(cmd.gp1);
            let ref_x = u32_to_f32(cmd.gp2);
            let ref_y = u32_to_f32(cmd.gp3);
            let axis_ratio = u32_to_f32(cmd.gp4);

            let ref_dx = ref_x - cx;
            let ref_dy = ref_y - cy;
            let rotation = atan2(ref_dy, ref_dx);
            let cos_r = cos(rotation);
            let sin_r = sin(rotation);
            let rx_sq = ref_dx * ref_dx + ref_dy * ref_dy;
            let ry_sq = rx_sq * axis_ratio * axis_ratio;

            let px = x - cx;
            let py = y - cy;
            let rotated_x = px * cos_r - py * sin_r;
            let rotated_y = px * sin_r + py * cos_r;

            if rx_sq < 1e-20 { return 0.0; }
            if ry_sq < 1e-20 { return 0.0; }
            return sqrt(rotated_x * rotated_x / rx_sq + rotated_y * rotated_y / ry_sq);
        }

        // Sweep (angular) gradient. Computes radians and sweep from raw degrees.
        // gp0=center.x, gp1=center.y, gp2=startAngleDegrees, gp3=endAngleDegrees
        fn sweep_gradient_t(x: f32, y: f32, cmd: Params) -> f32 {
            let cx = u32_to_f32(cmd.gp0);
            let cy = u32_to_f32(cmd.gp1);
            let start_deg = u32_to_f32(cmd.gp2);
            let end_deg = u32_to_f32(cmd.gp3);

            let start_rad = start_deg * 0.017453292;  // PI / 180
            let end_rad = end_deg * 0.017453292;

            // Compute sweep, normalizing to (0, 2PI].
            var sweep = (end_rad - start_rad) % 6.283185307;
            if sweep <= 0.0 { sweep += 6.283185307; }
            if abs(sweep) < 1e-6 { sweep = 6.283185307; }
            let is_full = abs(sweep - 6.283185307) < 1e-6;
            let inv_sweep = 1.0 / sweep;

            let dx = x - cx;
            let dy = y - cy;

            // atan2(-dy, dx) gives clockwise angles in y-down space.
            var angle = atan2(-dy, dx);
            if angle < 0.0 { angle += 6.283185307; }

            // Rotate basis by 180 degrees.
            angle += 3.141592653;
            if angle >= 6.283185307 { angle -= 6.283185307; }

            // Phase measured clockwise from start.
            var phase = angle - start_rad;
            if phase < 0.0 { phase += 6.283185307; }

            if is_full {
                return phase / 6.283185307;
            }
            return phase * inv_sweep;
        }

        __DECODE_TEXEL_FUNCTION__

        __ENCODE_OUTPUT_FUNCTION__

        __BLEND_AND_COMPOSE__

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

        fn area_to_coverage(area_val: i32, fill_rule: u32, rasterization_mode: u32, antialias_threshold: f32) -> f32 {
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
            // Aliased mode: snap to binary coverage using threshold
            if rasterization_mode == 1u {
                if coverage >= antialias_threshold {
                    coverage = 1.0;
                } else {
                    coverage = 0.0;
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

                var coverage_value = 0.0;

                // Tile position in edge-local (coverage-local) space.
                let band_top = tile_min_y - command.edge_origin_y;
                let band_left_fixed = (tile_min_x - command.edge_origin_x) << FIXED_SHIFT;

                // Multi-band lookup: tile may overlap one or two bands.
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
                        if edge.y0 == edge.y1 {
                            // Skip degenerate edges (sentinel slots from stroke expand).
                        } else if min(edge.x0, edge.x1) >= tile_right_fixed {
                        } else if max(edge.x0, edge.x1) < band_left_fixed {
                            accumulate_start_cover(edge.y0, edge.y1, clip_top, clip_bottom, tile_top_fixed);
                        } else {
                            rasterize_edge(edge, band_top, band_left_fixed, clip_top, clip_bottom);
                        }
                        ei += 256u;
                    }
                }
                workgroupBarrier();

                // Compute coverage.
                if in_bounds {
                    if dest_x_i32 >= cmd_min_x && dest_x_i32 < cmd_max_x && dest_y_i32 >= cmd_min_y && dest_y_i32 < cmd_max_y {
                        var cover = atomicLoad(&tile_start_cover[py]);
                        for (var col = 0u; col < px; col++) {
                            cover += atomicLoad(&tile_cover[py * 16u + col]);
                        }
                        let area_val = atomicLoad(&tile_area[py * 16u + px]) + (cover << AREA_SHIFT);
                        coverage_value = area_to_coverage(area_val, command.fill_rule_value, command.rasterization_mode, u32_to_f32(command.antialias_threshold));
                    }
                }

                // Compose coverage result (shared by fill and stroke paths).
                if in_bounds && coverage_value > 0.0 {
                    if dest_x_i32 >= cmd_min_x && dest_x_i32 < cmd_max_x && dest_y_i32 >= cmd_min_y && dest_y_i32 < cmd_max_y {
                        let blend_percentage = u32_to_f32(command.blend_percentage);
                        let effective_coverage = coverage_value * blend_percentage;

                        var brush = vec4<f32>(
                            u32_to_f32(command.gp0),
                            u32_to_f32(command.gp1),
                            u32_to_f32(command.gp2),
                            u32_to_f32(command.gp3));

                        if command.brush_type == BRUSH_IMAGE {
                            let origin_x = bitcast<i32>(command.brush_origin_x);
                            let origin_y = bitcast<i32>(command.brush_origin_y);
                            let region_x = i32(command.brush_region_x);
                            let region_y = i32(command.brush_region_y);
                            let region_w = i32(command.brush_region_width);
                            let region_h = i32(command.brush_region_height);
                            let sample_x = positive_mod(dest_x_i32 - origin_x, region_w) + region_x;
                            let sample_y = positive_mod(dest_y_i32 - origin_y, region_h) + region_y;
                            brush = __LOAD_BRUSH__;
                        } else if command.brush_type == BRUSH_LINEAR_GRADIENT {
                            let px = f32(source_x) + 0.5;
                            let py = f32(source_y) + 0.5;
                            let raw_t = linear_gradient_t(px, py, command);
                            brush = sample_brush_gradient(raw_t, command.gp4, command.stops_offset, command.stop_count);
                        } else if command.brush_type == BRUSH_RADIAL_GRADIENT {
                            let px = f32(source_x) + 0.5;
                            let py = f32(source_y) + 0.5;
                            let raw_t = radial_gradient_t(px, py, command);
                            brush = sample_brush_gradient(raw_t, command.gp4, command.stops_offset, command.stop_count);
                        } else if command.brush_type == BRUSH_RADIAL_GRADIENT_TWO_CIRCLE {
                            let px = f32(source_x) + 0.5;
                            let py = f32(source_y) + 0.5;
                            let raw_t = radial_gradient_two_t(px, py, command);
                            brush = sample_brush_gradient(raw_t, command.gp6, command.stops_offset, command.stop_count);
                        } else if command.brush_type == BRUSH_ELLIPTIC_GRADIENT {
                            let px = f32(source_x) + 0.5;
                            let py = f32(source_y) + 0.5;
                            let raw_t = elliptic_gradient_t(px, py, command);
                            brush = sample_brush_gradient(raw_t, command.gp5, command.stops_offset, command.stop_count);
                        } else if command.brush_type == BRUSH_SWEEP_GRADIENT {
                            let px = f32(source_x) + 0.5;
                            let py = f32(source_y) + 0.5;
                            let raw_t = sweep_gradient_t(px, py, command);
                            brush = sample_brush_gradient(raw_t, command.gp4, command.stops_offset, command.stop_count);
                        } else if command.brush_type == BRUSH_PATTERN {
                            let pw = u32_to_f32(command.gp0);
                            let ph = u32_to_f32(command.gp1);
                            let ox = u32_to_f32(command.gp2);
                            let oy = u32_to_f32(command.gp3);
                            let fx = f32(source_x) - ox;
                            let fy = f32(source_y) - oy;
                            let pw_i = i32(pw);
                            let ph_i = i32(ph);
                            let pxi = ((i32(fx) % pw_i) + pw_i) % pw_i;
                            let pyi = ((i32(fy) % ph_i) + ph_i) % ph_i;
                            let idx = command.stops_offset + u32(pyi) * u32(pw_i) + u32(pxi);
                            let c = color_stops[idx];
                            brush = vec4<f32>(c.r, c.g, c.b, c.a);
                        } else if command.brush_type == BRUSH_RECOLOR {
                            let src_r = u32_to_f32(command.gp0);
                            let src_g = u32_to_f32(command.gp1);
                            let src_b = u32_to_f32(command.gp2);
                            let src_a = u32_to_f32(command.gp3);
                            let tgt_r = u32_to_f32(command.gp4);
                            let tgt_g = u32_to_f32(command.gp5);
                            let tgt_b = u32_to_f32(command.gp6);
                            let tgt_a = u32_to_f32(command.gp7);
                            let threshold = bitcast<f32>(command.stops_offset);
                            let dr = destination.r - src_r;
                            let dg = destination.g - src_g;
                            let db = destination.b - src_b;
                            let da = destination.a - src_a;
                            let dist_sq = dr * dr + dg * dg + db * db + da * da;
                            if dist_sq <= threshold * threshold {
                                brush = vec4<f32>(tgt_r, tgt_g, tgt_b, tgt_a);
                            } else {
                                brush = destination;
                            }
                        }

                        let src = vec4<f32>(brush.rgb, brush.a * effective_coverage);
                        destination = compose_pixel(destination, src, command.color_blend_mode, command.alpha_composition_mode);
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
                .Replace("__BLEND_AND_COMPOSE__", CompositionShaderSnippets.BlendAndCompose, StringComparison.Ordinal)
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
