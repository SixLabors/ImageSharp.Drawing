// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// The shared bump allocator block for dynamically sized intermediate buffers.
//
// Stages reserve space with atomicAdd on the counter for their buffer and
// compare the result against the capacity in Config; on overflow they set
// their STAGE_* bit in `failed` and the C# side reads the block back, grows
// the buffers, and retries the render. Imported by most compute stages
// (binning, flatten, tile/row allocation, counting, coarse, chunk_reset,
// prepare, and the indirect-dispatch setup shaders).
//
// Ported from Vello's shader/shared/bump.wgsl (linebender/vello), where the
// struct was synced with the encoding crate's config.rs.

// Bitflags for each stage that can fail allocation.
const STAGE_BINNING: u32 = 0x1u;
const STAGE_TILE_ALLOC: u32 = 0x2u;
const STAGE_FLATTEN: u32 = 0x4u;
const STAGE_PATH_COUNT: u32 = 0x8u;
const STAGE_COARSE: u32 = 0x10u;

// Bump counters, one per dynamically sized buffer. Field order must be kept
// in sync with GpuSceneBumpAllocators in WebGPUSceneResources.cs, which the
// C# side reads back to detect overflow.
struct BumpAllocators {
    // Bitmask of stages that have failed allocation.
    failed: atomic<u32>,
    binning: atomic<u32>, // bin data words (info/bin-data buffer)
    ptcl: atomic<u32>, // per-tile command list words
    path_rows: atomic<u32>, // sparse PathRow records
    tile: atomic<u32>, // Tile records
    seg_counts: atomic<u32>, // SegmentCount records
    segments: atomic<u32>, // Segment records
    blend_spill: atomic<u32>, // blend stack slots spilled past BLEND_STACK_SPLIT
    lines: atomic<u32>, // LineSoup records from flattening
}

// Workgroup counts for dispatchWorkgroupsIndirect, written by the setup
// shaders (path_count_setup, path_tiling_setup) from the bump counters.
// Mirrors GpuSceneIndirectCount in WebGPUSceneResources.cs.
struct IndirectCount {
    count_x: u32,
    count_y: u32,
    count_z: u32,
}
