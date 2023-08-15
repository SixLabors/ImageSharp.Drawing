// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.Shapes.Scan;

/// <summary>
/// See: NumericCornerCases.jpg
/// </summary>
internal class NumericCornerCasePolygons
{
    public static readonly Polygon A = PolygonFactory.CreatePolygon(
        (2, 2.5f), (11, 2.5f), (11, 3.25f), (8, 3.1f), (5, 3), (2, 3));

    public static readonly Polygon B = PolygonFactory.CreatePolygon(
        (12, 2.5f), (21, 2.5f), (21, 3.2f), (18, 3.125f), (15, 3), (12, 3));

    public static readonly Polygon C = PolygonFactory.CreatePolygon(
        (2, 3.4f), (8, 3.6f), (8, 4), (5, 3.875f), (2, 4));

    public static readonly Polygon D = PolygonFactory.CreatePolygon(
        (12, 3.3f), (18, 3.6f), (18, 4), (15, 3.87f), (12, 4));

    public static readonly Polygon E = PolygonFactory.CreatePolygon(
        (3, 4.4f), (4, 4.75f), (6, 4.6f), (6, 5), (2, 5));

    public static readonly Polygon F = PolygonFactory.CreatePolygon(
        (13, 4.3f), (14, 4.75f), (16, 4.6f), (16, 5), (12, 5));

    public static readonly Polygon G = PolygonFactory.CreatePolygon((2, 2.25f), (6, 1.87f), (10, 2.25f));

    public static readonly Polygon H = PolygonFactory.CreatePolygon(
        (14, 1.88f), (16, 1.75f), (16, 2.25f), (14, 2.11f));

    public static Polygon GetByName(string name) => (Polygon)typeof(NumericCornerCasePolygons).GetField(name).GetValue(null);
}
