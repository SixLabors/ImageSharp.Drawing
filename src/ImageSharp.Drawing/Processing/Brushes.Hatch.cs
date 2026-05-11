// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Processing;

/// <content>
/// Provides additional hatch pattern brush factories.
/// </content>
public static partial class Brushes
{
    // These hatch arrays were derived using the GDI+ pixel extraction technique described at
    // https://web.archive.org/web/20221228174326/https://www.codeproject.com/Articles/5350583/Recreating-Gdiplus-hatches-with-SkiaSharp.
    private static readonly bool[,] HorizontalPattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] VerticalPattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] ForwardDiagonalPattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  true,  false,  false,  false,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  false,  false, },
        { false,  false,  false,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  false,  true,  false,  false, },
        { false,  false,  false,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  false,  false,  true, },
    };

    private static readonly bool[,] BackwardDiagonalPattern =
    {
        { false,  false,  false,  false,  false,  false,  false,  true, },
        { false,  false,  false,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  true,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  true,  false,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  false,  false, },
        { false,  true,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] CrossPattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DiagonalCrossPattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  true, },
        { false,  true,  false,  false,  false,  false,  true,  false, },
        { false,  false,  true,  false,  false,  true,  false,  false, },
        { false,  false,  false,  true,  true,  false,  false,  false, },
        { false,  false,  false,  true,  true,  false,  false,  false, },
        { false,  false,  true,  false,  false,  true,  false,  false, },
        { false,  true,  false,  false,  false,  false,  true,  false, },
        { true,  false,  false,  false,  false,  false,  false,  true, },
    };

    private static readonly bool[,] Percent05Pattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] Percent10Pattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] Percent20Pattern =
    {
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] Percent25Pattern =
    {
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
    };

    private static readonly bool[,] Percent30Pattern =
    {
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  false,  false,  true,  false,  false,  false,  true, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  false,  false,  true,  false,  false,  false,  true, },
    };

    private static readonly bool[,] Percent40Pattern =
    {
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  false,  false,  true, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  false,  false,  true,  false,  true,  false,  true, },
    };

    private static readonly bool[,] Percent50Pattern =
    {
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
    };

    private static readonly bool[,] Percent60Pattern =
    {
        { true,  true,  true,  false,  true,  true,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  false,  true,  true,  true,  false,  true,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  true,  true,  false,  true,  true,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  false,  true,  true,  true,  false,  true,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
    };

    private static readonly bool[,] Percent70Pattern =
    {
        { false,  true,  true,  true,  false,  true,  true,  true, },
        { true,  true,  false,  true,  true,  true,  false,  true, },
        { false,  true,  true,  true,  false,  true,  true,  true, },
        { true,  true,  false,  true,  true,  true,  false,  true, },
        { false,  true,  true,  true,  false,  true,  true,  true, },
        { true,  true,  false,  true,  true,  true,  false,  true, },
        { false,  true,  true,  true,  false,  true,  true,  true, },
        { true,  true,  false,  true,  true,  true,  false,  true, },
    };

    private static readonly bool[,] Percent75Pattern =
    {
        { false,  true,  true,  true,  false,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  false,  true,  true,  true,  false,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  true,  true,  true,  false,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  false,  true,  true,  true,  false,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
    };

    private static readonly bool[,] Percent80Pattern =
    {
        { true,  true,  true,  false,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  false,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
    };

    private static readonly bool[,] Percent90Pattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  false,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  true,  true,  true,  true,  true,  true,  true, },
    };

    private static readonly bool[,] LightDownwardDiagonalPattern =
    {
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  false,  false,  true,  false,  false,  false,  true, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  false,  false,  true,  false,  false,  false,  true, },
    };

    private static readonly bool[,] LightUpwardDiagonalPattern =
    {
        { false,  false,  false,  true,  false,  false,  false,  true, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  true,  false,  false,  false,  true, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
    };

    private static readonly bool[,] DarkDownwardDiagonalPattern =
    {
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { false,  false,  true,  true,  false,  false,  true,  true, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { false,  false,  true,  true,  false,  false,  true,  true, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
    };

    private static readonly bool[,] DarkUpwardDiagonalPattern =
    {
        { false,  false,  true,  true,  false,  false,  true,  true, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
        { false,  false,  true,  true,  false,  false,  true,  true, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
    };

    private static readonly bool[,] WideDownwardDiagonalPattern =
    {
        { true,  true,  false,  false,  false,  false,  false,  true, },
        { true,  true,  true,  false,  false,  false,  false,  false, },
        { false,  true,  true,  true,  false,  false,  false,  false, },
        { false,  false,  true,  true,  true,  false,  false,  false, },
        { false,  false,  false,  true,  true,  true,  false,  false, },
        { false,  false,  false,  false,  true,  true,  true,  false, },
        { false,  false,  false,  false,  false,  true,  true,  true, },
        { true,  false,  false,  false,  false,  false,  true,  true, },
    };

    private static readonly bool[,] WideUpwardDiagonalPattern =
    {
        { true,  false,  false,  false,  false,  false,  true,  true, },
        { false,  false,  false,  false,  false,  true,  true,  true, },
        { false,  false,  false,  false,  true,  true,  true,  false, },
        { false,  false,  false,  true,  true,  true,  false,  false, },
        { false,  false,  true,  true,  true,  false,  false,  false, },
        { false,  true,  true,  true,  false,  false,  false,  false, },
        { true,  true,  true,  false,  false,  false,  false,  false, },
        { true,  true,  false,  false,  false,  false,  false,  true, },
    };

    private static readonly bool[,] LightVerticalPattern =
    {
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
    };

    private static readonly bool[,] LightHorizontalPattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] NarrowVerticalPattern =
    {
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
    };

    private static readonly bool[,] NarrowHorizontalPattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DarkVerticalPattern =
    {
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
        { true,  true,  false,  false,  true,  true,  false,  false, },
    };

    private static readonly bool[,] DarkHorizontalPattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DashedDownwardDiagonalPattern =
    {
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  false,  false,  true,  false,  false,  false,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DashedUpwardDiagonalPattern =
    {
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  true,  false,  false,  false,  true, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DashedHorizontalPattern =
    {
        { true,  true,  true,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  true,  true,  true, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DashedVerticalPattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
    };

    private static readonly bool[,] SmallConfettiPattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  true,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  true,  false, },
        { false,  false,  false,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  true, },
        { false,  false,  true,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  true,  false,  false, },
    };

    private static readonly bool[,] LargeConfettiPattern =
    {
        { true,  false,  true,  true,  false,  false,  false,  true, },
        { false,  false,  true,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  true,  true, },
        { false,  false,  false,  true,  true,  false,  true,  true, },
        { true,  true,  false,  true,  true,  false,  false,  false, },
        { true,  true,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  true,  false,  false, },
        { true,  false,  false,  false,  true,  true,  false,  true, },
    };

    private static readonly bool[,] ZigZagPattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  true, },
        { false,  true,  false,  false,  false,  false,  true,  false, },
        { false,  false,  true,  false,  false,  true,  false,  false, },
        { false,  false,  false,  true,  true,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  true, },
        { false,  true,  false,  false,  false,  false,  true,  false, },
        { false,  false,  true,  false,  false,  true,  false,  false, },
        { false,  false,  false,  true,  true,  false,  false,  false, },
    };

    private static readonly bool[,] WavePattern =
    {
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  true,  true,  false,  false,  false, },
        { false,  false,  true,  false,  false,  true,  false,  true, },
        { true,  true,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  true,  true,  false,  false,  false, },
        { false,  false,  true,  false,  false,  true,  false,  true, },
        { true,  true,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DiagonalBrickPattern =
    {
        { false,  false,  false,  false,  false,  false,  false,  true, },
        { false,  false,  false,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  true,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  true,  true,  false,  false,  false, },
        { false,  false,  true,  false,  false,  true,  false,  false, },
        { false,  true,  false,  false,  false,  false,  true,  false, },
        { true,  false,  false,  false,  false,  false,  false,  true, },
    };

    private static readonly bool[,] HorizontalBrickPattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
    };

    private static readonly bool[,] WeavePattern =
    {
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  true,  false,  true,  false,  true,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  true,  false,  false,  false,  true,  false,  true, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  true,  false,  true,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  true,  false,  true,  false,  false,  false,  true, },
    };

    private static readonly bool[,] PlaidPattern =
    {
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  true,  false,  true,  false,  true,  false,  true, },
        { true,  true,  true,  true,  false,  false,  false,  false, },
        { true,  true,  true,  true,  false,  false,  false,  false, },
        { true,  true,  true,  true,  false,  false,  false,  false, },
        { true,  true,  true,  true,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DivotPattern =
    {
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  true, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DottedGridPattern =
    {
        { true,  false,  true,  false,  true,  false,  true,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] DottedDiamondPattern =
    {
        { true,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
        { false,  false,  true,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };

    private static readonly bool[,] ShinglePattern =
    {
        { false,  false,  false,  false,  false,  false,  true,  true, },
        { true,  false,  false,  false,  false,  true,  false,  false, },
        { false,  true,  false,  false,  true,  false,  false,  false, },
        { false,  false,  true,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  true,  false,  false, },
        { false,  false,  false,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  false,  false,  true, },
        { false,  false,  false,  false,  false,  false,  false,  true, },
    };

    private static readonly bool[,] TrellisPattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
    };

    private static readonly bool[,] SpherePattern =
    {
        { false,  true,  true,  true,  false,  true,  true,  true, },
        { true,  false,  false,  false,  true,  false,  false,  true, },
        { true,  false,  false,  false,  true,  true,  true,  true, },
        { true,  false,  false,  false,  true,  true,  true,  true, },
        { false,  true,  true,  true,  false,  true,  true,  true, },
        { true,  false,  false,  true,  true,  false,  false,  false, },
        { true,  true,  true,  true,  true,  false,  false,  false, },
        { true,  true,  true,  true,  true,  false,  false,  false, },
    };

    private static readonly bool[,] SmallGridPattern =
    {
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  true,  true,  true,  true,  true,  true,  true, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
        { true,  false,  false,  false,  true,  false,  false,  false, },
    };

    private static readonly bool[,] SmallCheckerBoardPattern =
    {
        { true,  false,  false,  true,  true,  false,  false,  true, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { false,  true,  true,  false,  false,  true,  true,  false, },
        { true,  false,  false,  true,  true,  false,  false,  true, },
    };

    private static readonly bool[,] LargeCheckerBoardPattern =
    {
        { true,  true,  true,  true,  false,  false,  false,  false, },
        { true,  true,  true,  true,  false,  false,  false,  false, },
        { true,  true,  true,  true,  false,  false,  false,  false, },
        { true,  true,  true,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  true,  true,  true,  true, },
        { false,  false,  false,  false,  true,  true,  true,  true, },
        { false,  false,  false,  false,  true,  true,  true,  true, },
        { false,  false,  false,  false,  true,  true,  true,  true, },
    };

    private static readonly bool[,] OutlinedDiamondPattern =
    {
        { true,  false,  false,  false,  false,  false,  true,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { false,  false,  true,  false,  true,  false,  false,  false, },
        { false,  false,  false,  true,  false,  false,  false,  false, },
        { false,  false,  true,  false,  true,  false,  false,  false, },
        { false,  true,  false,  false,  false,  true,  false,  false, },
        { true,  false,  false,  false,  false,  false,  true,  false, },
        { false,  false,  false,  false,  false,  false,  false,  true, },
    };

    private static readonly bool[,] SolidDiamondPattern =
    {
        { false,  false,  false,  true,  false,  false,  false,  false, },
        { false,  false,  true,  true,  true,  false,  false,  false, },
        { false,  true,  true,  true,  true,  true,  false,  false, },
        { true,  true,  true,  true,  true,  true,  true,  false, },
        { false,  true,  true,  true,  true,  true,  false,  false, },
        { false,  false,  true,  true,  true,  false,  false,  false, },
        { false,  false,  false,  true,  false,  false,  false,  false, },
        { false,  false,  false,  false,  false,  false,  false,  false, },
    };
}
