// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Utility functions that interact with host-shareable buffer objects.
//
// Unlike the other shared modules, this one references bindings by name, so
// it must be imported once, after the resource binding declarations, in the
// shader module that accesses them. Imported by draw_leaf.wgsl and
// draw_reduce.wgsl. Ported from Vello's shader/shared/util.wgsl
// (linebender/vello).

// Reads a draw tag from the scene buffer, defaulting to DRAWTAG_NOP if the given `ix` is beyond the
// range of valid draw objects (e.g this can happen if `ix` is derived from an invocation ID in a
// workgroup that partially spans valid range).
//
// This function depends on the following global declarations:
//    * `scene`: array<u32>
//    * `config`: Config (see config.wgsl)
fn read_draw_tag_from_scene(ix: u32) -> u32 {
    var tag_word: u32;
    if ix < config.n_drawobj {
        let tag_ix = config.drawtag_base + ix;
        tag_word = scene[tag_ix];
    } else {
        tag_word = DRAWTAG_NOP;
    }
    return tag_word;
}
