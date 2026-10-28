// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

// The scene segment table: one dispatch reads retained scenes in place.
//
// A retained scene drawn inside a layer does not copy its streams into the outer scene. The
// scene buffer holds the outer scene data, then this table, then one resident block for each
// retained scene. Each stream is a virtual stream: the outer elements before the first retained
// scene, then the retained elements, then the next outer elements, and so on. The monoid scans
// run over the virtual streams, so every monoid offset is a virtual offset.
//
// One table entry describes one segment, that is one contiguous piece of every stream. A shader
// finds the segment of an element one time, from the virtual index of its path-tag word or of
// its draw object. Then it reads each stream as scene[bias + virtual offset]. A bias is the
// physical start minus the virtual start, in wrapping u32 arithmetic. The segments are in stream
// order, and the first segment starts at virtual index zero in every stream.
//
// When config.segment_count is zero, the scene has no table and every helper returns the config
// bases. Then the cost for each element is one compare of a uniform value.
//
// Like util.wgsl, this module references the `scene` and `config` bindings by name, so the root
// shader must declare them. WGSL module-scope declarations do not depend on order, so the import
// can be at the top of the root shader. The C# writer of the table is WebGPUSceneEncoder.cs.

// Words in one table entry.
const SCENE_SEGMENT_WORDS = 13u;

// Virtual index of the first path-tag word of the segment. This is the key for path-tag elements.
const SCENE_SEGMENT_PATHTAG_START = 0u;
// Bias of the path-tag word stream.
const SCENE_SEGMENT_PATHTAG_BIAS = 1u;
// Bias of the path-data word stream.
const SCENE_SEGMENT_PATHDATA_BIAS = 2u;
// Bias of the style word stream.
const SCENE_SEGMENT_STYLE_BIAS = 3u;
// Bias of the transform stream: the physical word start minus 9 times the virtual transform index.
const SCENE_SEGMENT_TRANSFORM_BIAS = 4u;
// Virtual index of the first draw object of the segment. This is the key for draw objects.
const SCENE_SEGMENT_DRAWTAG_START = 5u;
// Bias of the draw-tag stream.
const SCENE_SEGMENT_DRAWTAG_BIAS = 6u;
// Bias of the draw-data word stream.
const SCENE_SEGMENT_DRAWDATA_BIAS = 7u;
// Word offset of the auxiliary brush data of the segment in the combined info buffer.
const SCENE_SEGMENT_BRUSH_BASE = 8u;
// First gradient-ramp row of the segment.
const SCENE_SEGMENT_GRADIENT_ROW_BASE = 9u;
// First image-atlas row of the segment.
const SCENE_SEGMENT_ATLAS_Y_BASE = 10u;
// Profile slot origin: the slot of a fill line is this value plus half the physical path-data
// word of the line start. Zero when the segment has no profile table.
const SCENE_SEGMENT_PROFILE_SLOTS = 11u;
// Word offset of the profile record table of the segment, or zero when it has none.
const SCENE_SEGMENT_PROFILE_RECORDS = 12u;

// Returned in place of a table entry when the scene has no segment table.
const SCENE_SEGMENT_NONE = 0xffffffffu;

// Returns the word offset of the table entry for the last segment whose key at `key_field` is
// less than or equal to `element_ix`. Empty segments share their key with the next segment, so
// the search selects the segment that holds the element. Call this only when
// config.segment_count is not zero.
fn find_scene_segment(key_field: u32, element_ix: u32) -> u32 {
    var lo = 0u;
    var hi = config.segment_count;
    while lo + 1u < hi {
        let mid = (lo + hi) >> 1u;
        if scene[config.segment_table_base + mid * SCENE_SEGMENT_WORDS + key_field] <= element_ix {
            lo = mid;
        } else {
            hi = mid;
        }
    }

    return config.segment_table_base + lo * SCENE_SEGMENT_WORDS;
}

// Returns the bias of the path-tag word stream for the path-tag word `word_ix`.
fn resolve_path_tag_bias(word_ix: u32) -> u32 {
    if config.segment_count == 0u {
        return config.pathtag_base;
    }

    return scene[find_scene_segment(SCENE_SEGMENT_PATHTAG_START, word_ix) + SCENE_SEGMENT_PATHTAG_BIAS];
}

// Returns the table entry of the segment that holds draw object `ix`, or SCENE_SEGMENT_NONE
// when the scene has no segment table.
fn resolve_draw_segment(ix: u32) -> u32 {
    if config.segment_count == 0u {
        return SCENE_SEGMENT_NONE;
    }

    return find_scene_segment(SCENE_SEGMENT_DRAWTAG_START, ix);
}

// Returns the bias of the draw-tag stream for a segment entry from resolve_draw_segment.
fn draw_tag_bias(entry: u32) -> u32 {
    if entry == SCENE_SEGMENT_NONE {
        return config.drawtag_base;
    }

    return scene[entry + SCENE_SEGMENT_DRAWTAG_BIAS];
}

// Returns the bias of the draw-data stream for a segment entry from resolve_draw_segment.
fn draw_data_bias(entry: u32) -> u32 {
    if entry == SCENE_SEGMENT_NONE {
        return config.drawdata_base;
    }

    return scene[entry + SCENE_SEGMENT_DRAWDATA_BIAS];
}
