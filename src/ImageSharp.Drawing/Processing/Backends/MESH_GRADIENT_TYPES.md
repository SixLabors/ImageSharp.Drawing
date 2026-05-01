# Mesh Gradient Type Sketch

This note sketches the small public surface needed to express mesh-style gradients with the existing canvas and backend pipeline.

## Design Decision

Do not add a single `MeshGradientBrush` for the triangulated case.

A triangulated mesh gradient can be expressed as multiple existing fills:

- `Polygon` provides each triangle's geometry.
- `PathGradientBrush` provides each triangle's three point/color pairs.
- `Path.ToLinearGeometry(Vector2 scale)` keeps geometry memoization through the existing `LinearGeometryCache`.
- CPU and WebGPU already render triangle-shaped `PathGradientBrush` fills.

This keeps the feature as a canvas convenience over existing primitives rather than a new backend concept.

## Public Types

```csharp
namespace SixLabors.ImageSharp.Drawing.Processing;

public readonly struct CubicGradientPatch
{
    public CubicGradientPatch(
        ReadOnlySpan<PointF> cubicControlPoints,
        ReadOnlySpan<Color> cornerColors);

    public PointF[] CubicControlPoints { get; }

    public Color[] CornerColors { get; }
}

public enum MeshPrimitiveMode
{
    Triangles,
    TriangleStrip
}

public readonly struct MeshVertex
{
    public MeshVertex(PointF position, Color color);

    public PointF Position { get; }

    public Color Color { get; }
}

public sealed class DrawingMesh
{
    private readonly MeshVertex[] vertices;
    private readonly ushort[] indices;

    public DrawingMesh(
        ReadOnlySpan<MeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        MeshPrimitiveMode primitiveMode);

    public ReadOnlySpan<MeshVertex> Vertices => this.vertices;

    public ReadOnlySpan<ushort> Indices => this.indices;

    public MeshPrimitiveMode PrimitiveMode { get; }
}
```

## Canvas Surface

These should be first-class canvas methods on `DrawingCanvas` and implemented by `DrawingCanvas<TPixel>`.

```csharp
namespace SixLabors.ImageSharp.Drawing.Processing;

public abstract class DrawingCanvas : IDisposable
{
    public abstract void FillPatch(
        CubicGradientPatch patch,
        GraphicsOptions? options = null);

    public abstract void DrawMesh(
        DrawingMesh mesh,
        GraphicsOptions? options = null);
}
```

## Lowering

`DrawMesh(...)` expands the submitted mesh into ordinary triangle fills before the backend sees the scene.

`MeshPrimitiveMode` selects the index traversal:

```csharp
switch (mesh.PrimitiveMode)
{
    case MeshPrimitiveMode.Triangles:
        this.FillIndexedTriangles(mesh);
        break;
    case MeshPrimitiveMode.TriangleStrip:
        this.FillTriangleStrip(mesh);
        break;
}
```

`Triangles` consumes the index buffer in independent groups of three:

```csharp
private void FillIndexedTriangles(DrawingMesh mesh)
{
    for (int i = 0; i < mesh.Indices.Length; i += 3)
    {
        MeshVertex v0 = mesh.Vertices[mesh.Indices[i]];
        MeshVertex v1 = mesh.Vertices[mesh.Indices[i + 1]];
        MeshVertex v2 = mesh.Vertices[mesh.Indices[i + 2]];

        this.FillTriangle(v0, v1, v2);
    }
}
```

`TriangleStrip` consumes one new vertex per triangle and flips the first two vertices on alternating triangles so the winding remains consistent:

```csharp
private void FillTriangleStrip(DrawingMesh mesh)
{
    for (int i = 0; i <= mesh.Indices.Length - 3; i++)
    {
        int i0 = mesh.Indices[i];
        int i1 = mesh.Indices[i + 1];
        int i2 = mesh.Indices[i + 2];

        if ((i & 1) != 0)
        {
            (i0, i1) = (i1, i0);
        }

        this.FillTriangle(mesh.Vertices[i0], mesh.Vertices[i1], mesh.Vertices[i2]);
    }
}
```

Both modes should share one helper:

```csharp
private void FillTriangle(MeshVertex v0, MeshVertex v1, MeshVertex v2)
{
    PointF[] points =
    [
        v0.Position,
        v1.Position,
        v2.Position
    ];

    Color[] colors =
    [
        v0.Color,
        v1.Color,
        v2.Color
    ];

    this.Fill(new PathGradientBrush(points, colors), new Polygon(points));
}
```

`FillPatch(...)` samples the cubic patch into generated mesh vertices, emits two triangles per sampled cell, then lowers those triangles through the same `Polygon` plus `PathGradientBrush` path.

## Backend Impact

No new backend scene command is required for the first implementation. The canvas methods emit normal fill commands, so the existing path remains:

```text
Polygon + PathGradientBrush
CompositionCommand
FlushScene / WebGPUSceneEncoder
DefaultRasterizer / WebGPU fine shader
BrushRenderer / path-gradient WGSL
```

The color interpolation is already implemented by `PathGradientBrushRenderer<TPixel>.FindPointOnTriangle(...)` on CPU and `path_grad_point_on_triangle(...)` in WebGPU.

## Missing From This Design

- General Skia `SkMesh` support with custom vertex and fragment shader programs.
- Arbitrary vertex attributes or varyings beyond position and color.
- Texture coordinates and child shader sampling.
- GPU-resident mesh vertex and index buffers.
- Runtime shader mesh-gradient modes such as inverse-distance or n-linear point-field shading.
- Seam-free rendering guarantees across shared triangle edges.
- A single mesh primitive submitted to the backend as one retained drawable.
- Patch quality controls such as adaptive tessellation or caller-selected subdivision density.
- Non-triangle mesh topologies beyond indexed triangles and triangle strips.
