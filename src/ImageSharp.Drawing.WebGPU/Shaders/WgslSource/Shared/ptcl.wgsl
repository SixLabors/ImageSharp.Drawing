// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Layout of the per-tile command list (PTCL).
//
// The PTCL is the intermediate representation between the coarse and fine
// stages: coarse.wgsl appends a command stream per 16x16 tile into the ptcl
// buffer, and fine.wgsl walks that stream to shade each pixel. Imported by
// coarse.wgsl (writer) and fine.wgsl (reader).
//
// Ported from Vello's shader/shared/ptcl.wgsl (linebender/vello) with
// ImageSharp additions (recolor, elliptic and path gradients, per-fill
// aliased coverage, clip mask bits).

// PTCL allocation sizes, in u32 units. Each tile starts with a fixed
// PTCL_INITIAL_ALLOC slice; when a stream outgrows its slice, coarse bump
// allocates another PTCL_INCREMENT and links it with a CMD_JUMP.
const PTCL_INITIAL_ALLOC = 64u;
const PTCL_INCREMENT = 256u;

// Amount of space reserved at the end of each slice for the CMD_JUMP
// (tag word plus destination index).
const PTCL_HEADROOM = 2u;

// Tags for PTCL commands. Slots 2u and 4u are retired Vello opcodes and stay unassigned.
const CMD_END = 0u;
const CMD_FILL = 1u;
const CMD_SOLID = 3u;
const CMD_COLOR = 5u;
const CMD_RECOLOR = 14u;
const CMD_LIN_GRAD = 6u;
const CMD_RAD_GRAD = 7u;
const CMD_ELLIPTIC_GRAD = 15u;
const CMD_SWEEP_GRAD = 8u;
const CMD_IMAGE = 9u;
const CMD_BEGIN_CLIP = 10u;
const CMD_END_CLIP = 11u;
const CMD_JUMP = 12u;
const CMD_PATH_GRAD = 16u;

// The individual PTCL structs are written here, but read/write is by
// hand in the relevant shaders

// Fill coverage command: rasterize the tile's segment slice into coverage.
// Written by coarse write_path, read by fine read_fill.
struct CmdFill {
    size_and_rule: u32, // bit 0 = even-odd, bit 1 = aliased coverage, bits 2.. = segment count
    seg_data: u32, // index of the tile's first Segment in segment storage
    backdrop: i32, // winding number at the tile's left edge
    // Per-fill coverage parameter, mode-dependent because the two uses are mutually
    // exclusive: the quantization threshold when the aliased bit in size_and_rule is set,
    // otherwise the perceptual coverage boost for antialiased text (zero when disabled).
    coverage_threshold: f32,
    interest: vec4<f32>, // pixel rect (x0, y0, x1, y1); coverage outside it is zeroed
}

// Continue reading the command stream at new_ix; links bump-allocated
// PTCL slices together.
struct CmdJump {
    new_ix: u32,
}

// Solid color paint.
struct CmdColor {
    color_rg: u32, // associated RG packed as binary16
    color_ba: u32, // associated BA packed as binary16
    draw_flags: u32, // blend mode and alpha, see DRAW_FLAGS_* in drawtag.wgsl
}

// Recolor paint. The target-specialized source, target and threshold live in
// the shared auxiliary brush-data region so their f32 precision survives PTCL lowering.
struct CmdRecolor {
    data_offset: u32,
    draw_flags: u32,
}

// Linear gradient paint. line_x/line_y/line_c define the implicit line
// equation whose signed value is the gradient parameter t.
struct CmdLinGrad {
    index: u32, // ramp row in the gradients texture
    extend_mode: u32, // pad, repeat, or reflect
    line_x: f32,
    line_y: f32,
    line_c: f32,
}

// Radial gradient paint using the two-circle formulation computed in
// draw_leaf (see RAD_GRAD_KIND_* in config.wgsl).
struct CmdRadGrad {
    index: u32,
    extend_mode: u32,
    matrx: vec4<f32>, // inverse transform, row-pair packed like Transform.matrx
    xlat: vec2<f32>, // inverse transform translation
    focal_x: f32,
    radius: f32,
    kind: u32, // RAD_GRAD_KIND_* selector
    flags: u32, // RAD_GRAD_SWAPPED when the circle order was flipped
}

// Elliptic gradient paint. Normal ellipses map pixels into normalized unit
// space; point and line kinds retain finite local coordinates so fine can
// reproduce the CPU brush's zero-radius division semantics.
struct CmdEllipticGrad {
    index: u32,
    extend_mode: u32,
    matrx: vec4<f32>,
    xlat: vec2<f32>,
    kind: u32, // ELLIPTIC_GRAD_KIND_* selector
}

// Sweep (conic) gradient paint sweeping angles t0..t1 about the origin of
// the inverse-transformed space.
struct CmdSweepGrad {
    index: u32,
    extend_mode: u32,
    matrx: vec4<f32>,
    xlat: vec2<f32>,
    t0: f32,
    t1: f32,
}

// Path gradient paint: t is derived from edge distances of the source path,
// whose edge list lives in the shared info/bin-data buffer.
struct CmdPathGrad {
    // Absolute offset of the gradient data in the shared info/bin-data
    // buffer; coarse rebases the scene-relative offset by
    // config.brush_data_base before writing the command.
    data_offset: u32,
    edge_count: u32,
    flags: u32,
    draw_flags: u32,
}

// Image paint sampled from the atlas texture.
struct CmdImage {
    matrx: vec4<f32>, // inverse transform mapping device to image space
    xlat: vec2<f32>,
    atlas_offset: vec2<f32>, // top-left of the image within the atlas
    extents: vec2<f32>, // image size in pixels
    format: u32,
    x_extend_mode: u32,
    y_extend_mode: u32,
    alpha: f32,
    alpha_type: u32,
    signed_unit: u32,
}

// Pops a clip/layer: blends the layer against its backdrop and, for clips,
// applies the mask coverage.
struct CmdEndClip {
    blend: u32, // packed blend word plus CLIP_*_MASK_BIT flags
    alpha: f32, // layer opacity
}
