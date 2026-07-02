// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// Draw tag stream decoding: the draw monoid, tag values, and draw-flag bits.
//
// Imported by binning.wgsl, clip_leaf.wgsl, coarse.wgsl, draw_leaf.wgsl,
// draw_reduce.wgsl, fine.wgsl, flatten.wgsl, and path_row_alloc.wgsl.
// Ported from Vello's shader/shared/drawtag.wgsl (linebender/vello) with
// ImageSharp additions (recolor, elliptic and path gradient tags, the
// aliased-coverage bit, and the blend mode/alpha draw-flag fields).
// Tag words are produced by the C# encoder (WebGPUSceneEncoder.cs), so the
// constants below document the shared wire format even where WGSL does not
// reference them all.

// The DrawMonoid is computed as a prefix sum to aid in decoding
// the variable-length encoding of draw objects.
struct DrawMonoid {
    // The number of paths preceding this draw object.
    path_ix: u32,
    // The number of clip operations preceding this draw object.
    clip_ix: u32,
    // The offset of the encoded draw object in the scene (u32s).
    scene_offset: u32,
    // The offset of the associated info.
    info_offset: u32,
}

// Each draw object has a 32-bit draw tag, which is a bit-packed
// version of the draw monoid: bit 0 = clip count, bits 2..4 = scene words,
// bits 6..9 = info words (see map_draw_tag).
// Visible-fill draw tags carry five extra info words: coverage threshold plus raster interest.
const DRAWTAG_NOP = 0u;
const DRAWTAG_FILL_COLOR = 0x184u;
const DRAWTAG_FILL_RECOLOR = 0x18cu;
const DRAWTAG_FILL_LIN_GRADIENT = 0x254u;
const DRAWTAG_FILL_RAD_GRADIENT = 0x3dcu;
const DRAWTAG_FILL_ELLIPTIC_GRADIENT = 0x31cu;
const DRAWTAG_FILL_SWEEP_GRADIENT = 0x394u;
const DRAWTAG_FILL_PATH_GRADIENT = 0x190u;
const DRAWTAG_FILL_IMAGE = 0x3d4u;
const DRAWTAG_BEGIN_CLIP = 0x49u;
const DRAWTAG_END_CLIP = 0x21u;

// The first word of each draw info stream entry contains the flags. This is not part of the
// draw object stream but is used after the draw objects have been reduced on the GPU.
// 0 represents a non-zero fill. 1 represents an even-odd fill.
const DRAW_INFO_FLAGS_FILL_RULE_BIT = 1u;
// Per-fill coverage rule. When set, the fill is rasterized aliased (coverage quantized against
// config.fine_coverage_threshold) instead of using analytic area coverage. Carried in a free
// high bit of the draw-flags word alongside the fill-rule bit.
const DRAW_INFO_FLAGS_ALIASED_BIT = 0x40000000u;
// Blend state packed into the draw-flags word by the C# encoder
// (WebGPUSceneEncoder.PackStyleDrawFlags): bits 1..13 hold the (mix << 8) | compose
// blend word and bits 14..29 hold the blend percentage quantized to 16 bits.
const DRAW_FLAGS_BLEND_MODE_SHIFT = 1u;
const DRAW_FLAGS_BLEND_MODE_MASK = 0x3ffeu;
const DRAW_FLAGS_BLEND_ALPHA_SHIFT = 14u;
const DRAW_FLAGS_BLEND_ALPHA_MASK = 0x3fffc000u;

// Flag bits carried in the high bits of the clip blend word, set by the C# encoder
// (WebGPUSceneEncoder.AppendClipBeginData). DIFFERENCE marks an ImageSharp Difference
// clip so fine inverts the mask; HARD marks a hard-edge (aliased) clip mask. Declared
// here because every consumer (draw_leaf, coarse, fine) imports this module.
const CLIP_DIFFERENCE_MASK_BIT = 0x80000000u;
const CLIP_HARD_MASK_BIT = 0x40000000u;

// The scan identity: all counters zero.
fn draw_monoid_identity() -> DrawMonoid {
    return DrawMonoid();
}

// The monoid combine operator: plain component-wise addition.
fn combine_draw_monoid(a: DrawMonoid, b: DrawMonoid) -> DrawMonoid {
    var c: DrawMonoid;
    c.path_ix = a.path_ix + b.path_ix;
    c.clip_ix = a.clip_ix + b.clip_ix;
    c.scene_offset = a.scene_offset + b.scene_offset;
    c.info_offset = a.info_offset + b.info_offset;
    return c;
}

// Maps a draw tag word to its DrawMonoid contribution by unpacking the
// per-object counts from the tag's bit fields.
fn map_draw_tag(tag_word: u32) -> DrawMonoid {
    var c: DrawMonoid;
    c.path_ix = u32(tag_word != DRAWTAG_NOP);
    c.clip_ix = tag_word & 1u;
    c.scene_offset = (tag_word >> 2u) & 0x07u;
    c.info_offset = (tag_word >> 6u) & 0x0fu;
    return c;
}
