# Polygon Scanning with Active Edge List

Scanning is done with a variant of the ["Active Edge Table" algorithm](https://en.wikipedia.org/wiki/Scanline_rendering#Algorithm), that doesn't build a table beforehand, just maintains the list of currently active edges.

After rasterizing polygons a collection of non-horizontal edges (ScanEdge) is extracted into ScanEdgeCollection. These are then sorted by minimum and maximum Y coordinate, which enables the maintanance of the Active Edge List as we traverse the collection from `minY` to `maxY`.

When intersecting a ScanEdge start (Y0) and end (Y1) intersections have special handling. Since these belong to vertices (connection points) sometimes we need to emit the intersection point 2 times. In other cases we do not want to emit it at all.

### Illustration

Consider the following polygon with 4 non-horizontal ScanEdge-s, being intersected by scanlines `SCANLINE 1` and `SCANLINE 2`:

```
                + - - - - - - - - - - - - - - - - +
                | (1)                           (1)\ 
                |                                   \ B
                |                                    \
                |                                 (0) \ 
SCANLINE 1 >>>> | >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>      | (1)  >>>>>>>>>
                |                                      |
                | A                                  C |
                |                                      |
                |               (2)         X          | (1)
SCANLINE 2 >>>> | >>>>>>>>>>>>>> + - - - - - - - - - - +      >>>>>>>>>
                |                |
                |                |               
                |              D |
                |                |
                |                |
                +  - - - - - - - +

```


#### Intersections at SCANLINE 1

- Intersection with edge A is trivial, since it's being intersected on an internal point of the edge
- The second intersection is more tricky: the intersection point is at the connection (vertex) between edges B and C, but we do not want to emit the intersection 2 times.
  - To avoid this, when checking the scanline's collision against edge B we emit 0 intersections for edge B at it's endpoint (Y1), when checking against edge C we emit 1 point at its start point (Y0)

#### Intersections at SCANLINE 2

- Intersection with edge A is trivial, since it's being intersected on an internal point
- However the rest is tricky: We want to to emulate the intersection with the collinear edge X not being listed in `ScanEdgeCollection`.
  - The easiest way is to emit a point pair for the line part between A-D and a second point pair for D-C (to emulate the intersection with X)
  - To achieve this, we should emit the start point (Y0) intersection of the D edge 2 times!

### Edge emit rules

The emit rules are there to provide a consistent way for intersecting scanlines as described in the previous "Illustration" part, handling all corner cases.
These rules only work well, when:
- The outline polygons are Clockwise in screen-space (= "has positive orientation" according to the terminlogy used in the repository)
- Holes have Counter-Clockwise ("negative") orientation.

Most real-world inputs tend to follow these rules, however intersecting polygons which do not do so, leads to inaccuracies around horizontal edges. These inaccuracies are visually acceptable.

The rules apply to vertices (edge connections). `⟶` and `⟵` edges are horizontal, `↑` and `↓` edges are non-horizontal.

Edge In | Edge Out | Emit on "Edge In" | Emit on "Edge out"
-- | -- | -- | --
↑ | ↑ | 0 | 1
↑ | ↓ | 1 | 1
↑ | ⟵ | 2 | 0
↑ | ⟶ | 1 | 0
↓ | ↑ | 1 | 1
↓ | ↓ | 0 | 1
↓ | ⟵ | 1 | 0
↓ | ⟶ | 2 | 0
⟵ | ↑ | 0 | 1
⟵ | ↓ | 0 | 2
⟵ | ⟵ | 0 | 0
⟵ | ⟶ | 0 | 0
⟶ | ↑ | 0 | 2
⟶ | ↓ | 0 | 1
⟶ | ⟵ | 0 | 0
⟶ | ⟶ | 0 | 0