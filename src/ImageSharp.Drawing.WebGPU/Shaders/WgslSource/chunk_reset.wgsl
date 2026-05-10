// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#import bump

@group(0) @binding(0)
var<storage, read_write> bump: BumpAllocators;

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
