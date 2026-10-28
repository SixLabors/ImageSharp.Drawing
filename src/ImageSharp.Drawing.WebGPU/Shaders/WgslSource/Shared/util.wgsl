// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Utility functions that interact with host-shareable buffer objects.
//
// Like scene_segment.wgsl, and unlike the other shared modules, this one
// references bindings by name, so the root shader must declare them. It calls
// the scene_segment.wgsl helpers, so the root shader imports scene_segment too.
// Imported by draw_leaf.wgsl and draw_reduce.wgsl. Ported from Vello's
// shader/shared/util.wgsl (linebender/vello).

// Reads a draw tag from the scene buffer, defaulting to DRAWTAG_NOP if the given `ix` is beyond the
// range of valid draw objects (e.g this can happen if `ix` is derived from an invocation ID in a
// workgroup that partially spans valid range).
//
// This function depends on the following global declarations:
//    * `scene`: array<u32>
//    * `config`: Config (see config.wgsl)
//    * the scene_segment.wgsl module, imported before this module
fn read_draw_tag_from_scene(ix: u32) -> u32 {
    return read_draw_tag_from_segment(resolve_draw_segment(ix), ix);
}

// Reads the draw tag of draw object `ix` through the segment entry that resolve_draw_segment
// returned for it, defaulting to DRAWTAG_NOP beyond the valid draw objects.
fn read_draw_tag_from_segment(entry: u32, ix: u32) -> u32 {
    var tag_word: u32;
    if ix < config.n_drawobj {
        tag_word = scene[draw_tag_bias(entry) + ix];
    } else {
        tag_word = DRAWTAG_NOP;
    }
    return tag_word;
}
