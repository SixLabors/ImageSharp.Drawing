// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Resets the chunk-local bump allocator counters between chunk dispatches
// while preserving the full-scene state produced by the shared scheduling
// stages. The scene is rendered in vertical tile-row chunks; the shared
// stages (flatten, binning, ...) run once, but the chunk-local stages
// (path_row_alloc through path_tiling) rerun per chunk and must start
// from zeroed counters.
//
// Inputs/outputs: bump. Retained across chunks: the binning and lines
// counters plus the STAGE_BINNING and STAGE_FLATTEN failure bits, since
// those belong to the shared stages that are not rerun. Everything else
// (ptcl, path_rows, tile, seg_counts, segments, blend_spill and the
// chunk-local failure bits) is cleared.
//
// Local addition; no Vello shader of this name exists.

#import bump

@group(0) @binding(0)
var<storage, read_write> bump: BumpAllocators;

// Single-thread stage: reads the retained values first, then rewrites the
// whole struct so chunk-local counters start at zero.
@compute @workgroup_size(1)
fn main() {
    let retained_failed = atomicLoad(&bump.failed) & (STAGE_BINNING | STAGE_FLATTEN);
    let retained_binning = atomicLoad(&bump.binning);
    let retained_lines = atomicLoad(&bump.lines);

    atomicStore(&bump.failed, retained_failed);
    atomicStore(&bump.binning, retained_binning);
    atomicStore(&bump.ptcl, 0u);
    atomicStore(&bump.path_rows, 0u);
    atomicStore(&bump.tile, 0u);
    atomicStore(&bump.seg_counts, 0u);
    atomicStore(&bump.segments, 0u);
    atomicStore(&bump.blend_spill, 0u);
    atomicStore(&bump.lines, retained_lines);
}
