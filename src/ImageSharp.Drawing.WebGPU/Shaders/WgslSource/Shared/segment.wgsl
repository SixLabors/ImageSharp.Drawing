// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Line segment records used after path lowering and before fine rasterization.
//
// Imported by fine.wgsl, path_lowering.wgsl, path_count.wgsl, path_row_span.wgsl,
// and path_tiling.wgsl. Ported from Vello's shader/shared/segment.wgsl
// (linebender/vello).

// Segments laid out for contiguous storage: path_tiling clips each LineSoup
// line against its tiles and writes one Segment per (tile, line) crossing
// for fine to integrate.
struct Segment {
    // Points are relative to tile origin
    point0: vec2<f32>,
    point1: vec2<f32>,
    // Tile-relative y at which the segment meets the tile's left edge, or
    // 1e9 if it does not; fine accumulates the implied vertical edge there
    // to keep winding consistent after clipping.
    y_edge: f32,
    // Profile identifiers used by aliased thin-feature recovery. Bits 0..15 select the profile
    // based on left and right movement. Bits 16..31 select the profile based on up and down
    // movement. An all-ones field means that no profile is available for that axis.
    tag: u32,
}

// Marks a segment retained only so the aliased row walk in the tile on its left can read the
// exact crossing. The segment's owning tile skips it, preserving the original edge ownership.
const HALO_ONLY_Y_EDGE = -1e9;

// Profile identifiers occupy 16 bits in a segment tag. The largest value means that the segment
// has no profile for that axis. A tag with both fields set to the sentinel has no profile data.
const PROFILE_ID_MASK = 0xffffu;
const PROFILE_ID_SENTINEL = PROFILE_ID_MASK;
const PROFILE_TAG_SENTINEL = 0xffffffffu;

// A final line segment produced by the path-lowering stage.
//
// The name is perhaps too playful, but reflects the fact that these
// lines are completely unordered. They will flow through coarse path
// rasterization, then the per-tile segments will be scatter-written into
// segment storage so that each (tile, path) tuple gets a contiguous
// slice of segments.
struct LineSoup {
    // Index of the path that owns this line.
    path_ix: u32,
    // Profile tag copied from the encoded fill segment to every per-tile Segment record.
    tag: u32,
    p0: vec2<f32>,
    p1: vec2<f32>,
}

// An intermediate data structure for sorting tile segments.
struct SegmentCount {
    // Reference to element of LineSoup array
    line_ix: u32,
    // Two count values packed into a single u32
    // Lower 16 bits: index of segment within line
    // Upper 16 bits: index of segment within segment slice
    counts: u32,
}
