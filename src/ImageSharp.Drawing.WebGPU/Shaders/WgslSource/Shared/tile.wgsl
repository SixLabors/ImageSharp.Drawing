// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Common datatypes for path and tile intermediate info.
//
// Imported by backdrop_dyn.wgsl, coarse.wgsl, path_count.wgsl,
// path_row_alloc.wgsl, path_row_span.wgsl, path_tiling.wgsl, and
// tile_alloc.wgsl. Derived from Vello's shader/shared/tile.wgsl
// (linebender/vello); the sparse PathRow row records are an ImageSharp
// addition that lets tile storage be allocated per touched row span rather
// than for the whole path bounding box.

// Per-path tiling record: written by path_row_alloc, consumed by the
// stages that walk a path's tiles.
struct Path {
    // bounding box in tiles
    bbox: vec4<u32>,
    // offset to the first sparse row record owned by this path
    rows: u32,
    // Facts about the original path that remain necessary after clipping its drawing bounds.
    flags: u32,
}

// The original path begins left of its drawing bounds. The aliased row walk uses this flag when an
// interval enters through the clipped left edge and closes inside the first half pixel.
const PATH_FLAGS_CLIPPED_LEFT = 0x1u;

// Coarse packs one path-tile index and three aliased row-walk flags into CmdFill.coverage_data.
// The index occupies bits 0..28. The remaining bits describe the original left clip and the two
// neighboring tiles that contain segments for the same path row.
const ALIASED_TILE_INDEX_MASK = 0x1fffffffu;
const ALIASED_CLIPPED_LEFT_BIT = 0x20000000u;
const ALIASED_LEFT_NEIGHBOR_BIT = 0x40000000u;
const ALIASED_RIGHT_NEIGHBOR_BIT = 0x80000000u;
const INVALID_TILE_INDEX = 0xffffffffu;

// One tile row of a path's bounding box. path_row_span narrows x0/x1 to the
// columns actually touched, then tile_alloc allocates that span and rewrites
// `tiles` from flags to the base tile index.
struct PathRow {
    x0: u32, // leftmost touched tile column (min-reduced from 0xffffffff)
    x1: u32, // exclusive right bound of touched tile columns (max-reduced from 0)
    backdrop: i32, // accumulated winding delta carried into the row's left edge
    tiles: u32, // PATH_ROW_FLAG_* bits before tile_alloc, base tile index afterwards
}

// Atomic view of PathRow for the stages that reduce into it concurrently.
// Must mirror PathRow field-for-field: both views alias the same buffer.
struct AtomicPathRow {
    x0: atomic<u32>,
    x1: atomic<u32>,
    backdrop: atomic<i32>,
    tiles: atomic<u32>,
}

// Set on PathRow.tiles while it still holds flags: the row has geometry
// crossing its right edge, so the span must extend to the bbox edge for
// backdrop propagation.
const PATH_ROW_FLAG_TOUCHES_RIGHT = 0x1u;

// Per-tile accumulator filled in between tile_alloc and coarse.
struct Tile {
    backdrop: i32, // winding number carried into the tile's left edge
    // This is used for the count of the number of segments in the
    // tile up to coarse rasterization, and the index afterwards.
    // In the latter variant, the bits are inverted so that tiling
    // can detect whether the tile was allocated; it's best to
    // consider this an enum packed into a u32.
    segment_count_or_ix: u32,
}
