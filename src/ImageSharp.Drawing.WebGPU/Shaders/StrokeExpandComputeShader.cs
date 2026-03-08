// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// GPU compute shader that expands stroke centerline edges into outline polygon edges.
/// Each thread processes one centerline edge and writes outline edges (side/join/cap)
/// to the output region of the edge buffer using an atomic counter for slot allocation.
/// The generated outline edges are then rasterized by the composite shader's fill path.
/// </summary>
internal static class StrokeExpandComputeShader
{
    private static readonly byte[] CodeBytes =
    [
        .. """
        struct Edge {
            x0: i32,
            y0: i32,
            x1: i32,
            y1: i32,
            flags: i32,
            adj_x: i32,
            adj_y: i32,
        }

        struct StrokeExpandCommand {
            input_start: u32,
            input_count: u32,
            output_start: u32,
            output_max: u32,
            half_width: f32,
            line_cap: u32,
            line_join: u32,
            miter_limit: f32,
        }

        struct StrokeExpandConfig {
            total_input_edges: u32,
            command_count: u32,
        }

        @group(0) @binding(0) var<storage, read_write> edges: array<Edge>;
        @group(0) @binding(1) var<storage, read> commands: array<StrokeExpandCommand>;
        @group(0) @binding(2) var<uniform> config: StrokeExpandConfig;
        @group(0) @binding(3) var<storage, read_write> output_counters: array<atomic<u32>>;

        const FIXED_ONE: i32 = 256;

        // Edge descriptor flags (matches C# GpuEdge.Flags bit layout).
        const EDGE_JOIN: i32 = 32;
        const EDGE_CAP_START: i32 = 64;
        const EDGE_CAP_END: i32 = 128;

        // LineCap enum values.
        const CAP_BUTT: u32 = 0u;
        const CAP_SQUARE: u32 = 1u;
        const CAP_ROUND: u32 = 2u;

        // LineJoin enum values.
        const JOIN_MITER: u32 = 0u;
        const JOIN_MITER_REVERT: u32 = 1u;
        const JOIN_ROUND: u32 = 2u;
        const JOIN_BEVEL: u32 = 3u;
        const JOIN_MITER_ROUND: u32 = 4u;

        var<private> p_cmd: StrokeExpandCommand;
        var<private> p_cmd_idx: u32;

        fn emit_outline_edge(ex0: i32, ey0: i32, ex1: i32, ey1: i32) {
            if ey0 == ey1 { return; }
            let slot = atomicAdd(&output_counters[p_cmd_idx], 1u);
            let idx = p_cmd.output_start + slot;
            if idx >= p_cmd.output_max { return; }
            var out_edge: Edge;
            out_edge.x0 = ex0;
            out_edge.y0 = ey0;
            out_edge.x1 = ex1;
            out_edge.y1 = ey1;
            out_edge.flags = 0;
            out_edge.adj_x = 0;
            out_edge.adj_y = 0;
            edges[idx] = out_edge;
        }

        fn generate_side_edges(edge: Edge, hw_fp: f32) {
            let fdx = f32(edge.x1 - edge.x0);
            let fdy = f32(edge.y1 - edge.y0);
            let flen = sqrt(fdx * fdx + fdy * fdy);
            if flen < 1.0 { return; }
            let nxf = -fdy / flen * hw_fp;
            let nyf = fdx / flen * hw_fp;
            let x0f = f32(edge.x0);
            let y0f = f32(edge.y0);
            let x1f = f32(edge.x1);
            let y1f = f32(edge.y1);
            emit_outline_edge(
                i32(round(x0f + nxf)), i32(round(y0f + nyf)),
                i32(round(x1f + nxf)), i32(round(y1f + nyf)));
            emit_outline_edge(
                i32(round(x1f - nxf)), i32(round(y1f - nyf)),
                i32(round(x0f - nxf)), i32(round(y0f - nyf)));
        }

        fn generate_join_edges(edge: Edge, hw_fp: f32, line_join: u32, miter_limit: f32) {
            let vx = f32(edge.x0);
            let vy = f32(edge.y0);
            let dx1 = vx - f32(edge.x1);
            let dy1 = vy - f32(edge.y1);
            let len1 = sqrt(dx1 * dx1 + dy1 * dy1);
            if len1 < 1.0 { return; }
            let dx2 = f32(edge.adj_x) - vx;
            let dy2 = f32(edge.adj_y) - vy;
            let len2 = sqrt(dx2 * dx2 + dy2 * dy2);
            if len2 < 1.0 { return; }

            let nx1 = -dy1 / len1; let ny1 = dx1 / len1;
            let nx2 = -dy2 / len2; let ny2 = dx2 / len2;
            let cross = dx1 * dy2 - dy1 * dx2;

            var oax: f32; var oay: f32; var obx: f32; var oby: f32;
            var iax: f32; var iay: f32; var ibx: f32; var iby: f32;
            if cross > 0.0 {
                oax = vx - nx1 * hw_fp; oay = vy - ny1 * hw_fp;
                obx = vx - nx2 * hw_fp; oby = vy - ny2 * hw_fp;
                iax = vx + nx1 * hw_fp; iay = vy + ny1 * hw_fp;
                ibx = vx + nx2 * hw_fp; iby = vy + ny2 * hw_fp;
            } else {
                oax = vx + nx1 * hw_fp; oay = vy + ny1 * hw_fp;
                obx = vx + nx2 * hw_fp; oby = vy + ny2 * hw_fp;
                iax = vx - nx1 * hw_fp; iay = vy - ny1 * hw_fp;
                ibx = vx - nx2 * hw_fp; iby = vy - ny2 * hw_fp;
            }

            var ofx: f32; var ofy: f32; var otx: f32; var oty: f32;
            var ifx: f32; var ify: f32; var itx: f32; var ity: f32;
            if cross > 0.0 {
                ofx = obx; ofy = oby; otx = oax; oty = oay;
                ifx = iax; ify = iay; itx = ibx; ity = iby;
            } else {
                ofx = oax; ofy = oay; otx = obx; oty = oby;
                ifx = ibx; ify = iby; itx = iax; ity = iay;
            }

            // Inner join: always bevel.
            emit_outline_edge(i32(round(ifx)), i32(round(ify)), i32(round(itx)), i32(round(ity)));

            // Outer join.
            var miter_handled = false;
            if line_join == JOIN_MITER || line_join == JOIN_MITER_REVERT || line_join == JOIN_MITER_ROUND {
                let ux1 = dx1 / len1; let uy1 = dy1 / len1;
                let ux2 = dx2 / len2; let uy2 = dy2 / len2;
                let denom = ux1 * uy2 - uy1 * ux2;
                if abs(denom) > 1e-4 {
                    let dpx = obx - oax; let dpy = oby - oay;
                    let t = (dpx * uy2 - dpy * ux2) / denom;
                    let mx = oax + t * ux1; let my = oay + t * uy1;
                    let mdx = mx - vx; let mdy = my - vy;
                    let miter_dist = sqrt(mdx * mdx + mdy * mdy);
                    let limit = hw_fp * miter_limit;
                    if miter_dist <= limit {
                        emit_outline_edge(i32(round(ofx)), i32(round(ofy)), i32(round(mx)), i32(round(my)));
                        emit_outline_edge(i32(round(mx)), i32(round(my)), i32(round(otx)), i32(round(oty)));
                        miter_handled = true;
                    } else if line_join == JOIN_MITER {
                        let bdx = (oax + obx) * 0.5 - vx;
                        let bdy = (oay + oby) * 0.5 - vy;
                        let bdist = sqrt(bdx * bdx + bdy * bdy);
                        let blend = clamp((limit - bdist) / (miter_dist - bdist), 0.0, 1.0);
                        let cx1 = ofx + (mx - ofx) * blend; let cy1 = ofy + (my - ofy) * blend;
                        let cx2 = otx + (mx - otx) * blend; let cy2 = oty + (my - oty) * blend;
                        emit_outline_edge(i32(round(ofx)), i32(round(ofy)), i32(round(cx1)), i32(round(cy1)));
                        emit_outline_edge(i32(round(cx1)), i32(round(cy1)), i32(round(cx2)), i32(round(cy2)));
                        emit_outline_edge(i32(round(cx2)), i32(round(cy2)), i32(round(otx)), i32(round(oty)));
                        miter_handled = true;
                    }
                }
            }
            if !miter_handled {
                if line_join == JOIN_ROUND || line_join == JOIN_MITER_ROUND {
                    let sa = atan2(ofy - vy, ofx - vx);
                    let ea = atan2(oty - vy, otx - vx);
                    var sweep = ea - sa;
                    if sweep > 3.14159265 { sweep -= 2.0 * 3.14159265; }
                    if sweep < -3.14159265 { sweep += 2.0 * 3.14159265; }
                    let rpx = hw_fp / f32(FIXED_ONE);
                    let steps = max(4u, u32(ceil(abs(sweep) * rpx * 0.5)));
                    let da = sweep / f32(steps);
                    var pax = ofx; var pay = ofy;
                    for (var s = 1u; s <= steps; s++) {
                        var cax: f32; var cay: f32;
                        if s == steps { cax = otx; cay = oty; }
                        else {
                            let a = sa + da * f32(s);
                            cax = vx + cos(a) * hw_fp;
                            cay = vy + sin(a) * hw_fp;
                        }
                        emit_outline_edge(i32(round(pax)), i32(round(pay)), i32(round(cax)), i32(round(cay)));
                        pax = cax; pay = cay;
                    }
                } else {
                    emit_outline_edge(i32(round(ofx)), i32(round(ofy)), i32(round(otx)), i32(round(oty)));
                }
            }
        }

        fn generate_cap_edges(edge: Edge, hw_fp: f32, line_cap: u32, is_start: bool) {
            let cx = f32(edge.x0); let cy = f32(edge.y0);
            let ax = f32(edge.x1); let ay = f32(edge.y1);
            var dx: f32; var dy: f32;
            if is_start { dx = ax - cx; dy = ay - cy; }
            else { dx = cx - ax; dy = cy - ay; }
            let len = sqrt(dx * dx + dy * dy);
            if len < 1.0 { return; }
            let dir_x = dx / len; let dir_y = dy / len;
            let nx = -dir_y * hw_fp; let ny = dir_x * hw_fp;
            let lx = cx + nx; let ly = cy + ny;
            let rx = cx - nx; let ry = cy - ny;

            if line_cap == CAP_BUTT {
                if is_start {
                    emit_outline_edge(i32(round(rx)), i32(round(ry)), i32(round(lx)), i32(round(ly)));
                } else {
                    emit_outline_edge(i32(round(lx)), i32(round(ly)), i32(round(rx)), i32(round(ry)));
                }
            } else if line_cap == CAP_SQUARE {
                var ox: f32; var oy: f32;
                if is_start { ox = -dir_x * hw_fp; oy = -dir_y * hw_fp; }
                else { ox = dir_x * hw_fp; oy = dir_y * hw_fp; }
                let lxe = lx + ox; let lye = ly + oy;
                let rxe = rx + ox; let rye = ry + oy;
                if is_start {
                    emit_outline_edge(i32(round(rx)), i32(round(ry)), i32(round(rxe)), i32(round(rye)));
                    emit_outline_edge(i32(round(rxe)), i32(round(rye)), i32(round(lxe)), i32(round(lye)));
                    emit_outline_edge(i32(round(lxe)), i32(round(lye)), i32(round(lx)), i32(round(ly)));
                } else {
                    emit_outline_edge(i32(round(lx)), i32(round(ly)), i32(round(lxe)), i32(round(lye)));
                    emit_outline_edge(i32(round(lxe)), i32(round(lye)), i32(round(rxe)), i32(round(rye)));
                    emit_outline_edge(i32(round(rxe)), i32(round(rye)), i32(round(rx)), i32(round(ry)));
                }
            } else if line_cap == CAP_ROUND {
                var sa: f32; var sx: f32; var sy: f32; var ex: f32; var ey: f32;
                if is_start {
                    sa = atan2(ry - cy, rx - cx);
                    sx = rx; sy = ry; ex = lx; ey = ly;
                } else {
                    sa = atan2(ly - cy, lx - cx);
                    sx = lx; sy = ly; ex = rx; ey = ry;
                }
                var sweep = atan2(ey - cy, ex - cx) - sa;
                if sweep > 0.0 { sweep -= 2.0 * 3.14159265; }
                let rpx = hw_fp / f32(FIXED_ONE);
                let steps = max(4u, u32(ceil(abs(sweep) * rpx * 0.5)));
                let da = sweep / f32(steps);
                var pax = sx; var pay = sy;
                for (var s = 1u; s <= steps; s++) {
                    var cax: f32; var cay: f32;
                    if s == steps { cax = ex; cay = ey; }
                    else {
                        let a = sa + da * f32(s);
                        cax = cx + cos(a) * hw_fp;
                        cay = cy + sin(a) * hw_fp;
                    }
                    emit_outline_edge(i32(round(pax)), i32(round(pay)), i32(round(cax)), i32(round(cay)));
                    pax = cax; pay = cay;
                }
            }
        }

        @compute @workgroup_size(256, 1, 1)
        fn cs_main(@builtin(global_invocation_id) gid: vec3<u32>) {
            let thread_idx = gid.x;
            if thread_idx >= config.total_input_edges { return; }

            // Find which command this thread's edge belongs to.
            var running = 0u;
            var cmd_idx = 0u;
            for (var c = 0u; c < config.command_count; c++) {
                let cmd = commands[c];
                if thread_idx < running + cmd.input_count {
                    cmd_idx = c;
                    break;
                }
                running += cmd.input_count;
            }

            let cmd = commands[cmd_idx];
            p_cmd = cmd;
            p_cmd_idx = cmd_idx;
            let local_idx = thread_idx - running;
            let edge = edges[cmd.input_start + local_idx];
            let hw_fp = cmd.half_width * f32(FIXED_ONE);
            let flags = edge.flags;

            if (flags & EDGE_JOIN) != 0 {
                generate_join_edges(edge, hw_fp, cmd.line_join, cmd.miter_limit);
            } else if (flags & EDGE_CAP_START) != 0 {
                generate_cap_edges(edge, hw_fp, cmd.line_cap, true);
            } else if (flags & EDGE_CAP_END) != 0 {
                generate_cap_edges(edge, hw_fp, cmd.line_cap, false);
            } else {
                generate_side_edges(edge, hw_fp);
            }
        }
        """u8,
        0
    ];

    /// <summary>Gets the WGSL source for this shader as a null-terminated UTF-8 span.</summary>
    public static ReadOnlySpan<byte> Code => CodeBytes;
}
