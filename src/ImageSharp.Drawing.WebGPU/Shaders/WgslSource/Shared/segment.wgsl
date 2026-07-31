// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Line segment records flowing between flattening and fine rasterization.
//
// Imported by fine.wgsl, flatten.wgsl, path_count.wgsl, path_row_span.wgsl,
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
}

// A line segment produced by flattening and ready for rasterization.
//
// The name is perhaps too playful, but reflects the fact that these
// lines are completely unordered. They will flow through coarse path
// rasterization, then the per-tile segments will be scatter-written into
// segment storage so that each (tile, path) tuple gets a contiguous
// slice of segments.
struct LineSoup {
    path_ix: u32,
    // Note: this creates an alignment gap. Don't worry about
    // this now, but maybe move to scalars.
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
