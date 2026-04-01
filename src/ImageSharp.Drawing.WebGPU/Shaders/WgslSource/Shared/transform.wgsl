// Copyright 2023 the Vello Authors
// SPDX-License-Identifier: Apache-2.0 OR MIT OR Unlicense

// Helpers for working with projective 2D transforms.
//
// Each transform stores the 9 elements of a 3x3 projective matrix extracted
// from a Matrix4x4 with z=0:
//   X = x*M11 + y*M21 + M41
//   Y = x*M12 + y*M22 + M42
//   W = x*M14 + y*M24 + M44
//
// For affine matrices M14=M24=0, M44=1 so W is always 1 and the perspective
// divide is a no-op.

struct Transform {
    matrx: vec4<f32>,       // [M11, M12, M21, M22]
    translate: vec2<f32>,   // [M41, M42]
    perspective: vec3<f32>, // [M14, M24, M44]
}

// Matches TransformUtilities.ProjectiveTransform2D from ImageSharp:
// Vector4.Transform(new Vector4(x, y, 0, 1), matrix) then divide by W.
fn transform_apply(transform: Transform, p: vec2<f32>) -> vec2<f32> {
    let x = fma(transform.matrx.x, p.x, fma(transform.matrx.z, p.y, transform.translate.x));
    let y = fma(transform.matrx.y, p.x, fma(transform.matrx.w, p.y, transform.translate.y));
    let w = fma(transform.perspective.x, p.x, fma(transform.perspective.y, p.y, transform.perspective.z));
    return vec2(x, y) / max(w, 0.0000001);
}

fn transform_identity() -> Transform {
    return Transform(vec4(1.0, 0.0, 0.0, 1.0), vec2(0.0), vec3(0.0, 0.0, 1.0));
}

// 3x3 projective inverse via cofactor expansion.
fn transform_inverse(transform: Transform) -> Transform {
    let a = transform.matrx.x;  // M11
    let b = transform.matrx.y;  // M12
    let c = transform.perspective.x; // M14
    let d = transform.matrx.z;  // M21
    let e = transform.matrx.w;  // M22
    let f = transform.perspective.y; // M24
    let g = transform.translate.x; // M41
    let h = transform.translate.y; // M42
    let i = transform.perspective.z; // M44

    let det = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    let inv_det = 1.0 / det;

    // The packed fields represent the matrix:
    // [ a d g ]
    // [ b e h ]
    // [ c f i ]
    // so the cofactors must be placed back into that same layout.
    return Transform(
        inv_det * vec4(e * i - f * h, c * h - b * i, f * g - d * i, a * i - c * g),
        inv_det * vec2(d * h - e * g, b * g - a * h),
        inv_det * vec3(b * f - c * e, c * d - a * f, a * e - b * d)
    );
}

// 3x3 projective multiplication.
fn transform_mul(a: Transform, b: Transform) -> Transform {
    let a11 = a.matrx.x; let a12 = a.matrx.y; let a14 = a.perspective.x;
    let a21 = a.matrx.z; let a22 = a.matrx.w; let a24 = a.perspective.y;
    let a41 = a.translate.x; let a42 = a.translate.y; let a44 = a.perspective.z;

    let b11 = b.matrx.x; let b12 = b.matrx.y; let b14 = b.perspective.x;
    let b21 = b.matrx.z; let b22 = b.matrx.w; let b24 = b.perspective.y;
    let b41 = b.translate.x; let b42 = b.translate.y; let b44 = b.perspective.z;

    return Transform(
        vec4(
            a11 * b11 + a21 * b12 + a41 * b14,
            a12 * b11 + a22 * b12 + a42 * b14,
            a11 * b21 + a21 * b22 + a41 * b24,
            a12 * b21 + a22 * b22 + a42 * b24
        ),
        vec2(
            a11 * b41 + a21 * b42 + a41 * b44,
            a12 * b41 + a22 * b42 + a42 * b44
        ),
        vec3(
            a14 * b11 + a24 * b12 + a44 * b14,
            a14 * b21 + a24 * b22 + a44 * b24,
            a14 * b41 + a24 * b42 + a44 * b44
        )
    );
}
