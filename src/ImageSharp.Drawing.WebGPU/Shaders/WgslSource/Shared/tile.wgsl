// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Common datatypes for path and tile intermediate info.

struct Path {
    // bounding box in tiles
    bbox: vec4<u32>,
    // offset to the first sparse row record owned by this path
    rows: u32,
}

struct PathRow {
    x0: u32,
    x1: u32,
    backdrop: i32,
    tiles: u32,
}

struct AtomicPathRow {
    x0: atomic<u32>,
    x1: atomic<u32>,
    backdrop: atomic<i32>,
    tiles: atomic<u32>,
}

const PATH_ROW_FLAG_TOUCHES_RIGHT = 0x1u;

struct Tile {
    backdrop: i32,
    // This is used for the count of the number of segments in the
    // tile up to coarse rasterization, and the index afterwards.
    // In the latter variant, the bits are inverted so that tiling
    // can detect whether the tile was allocated; it's best to
    // consider this an enum packed into a u32.
    segment_count_or_ix: u32,
}
