// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

#pragma warning disable SA1201 // Phase-1 scene-model types are grouped together in one file for now.

using System.Buffers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Processing.Backends;

/// <summary>
/// Builds the flush-scoped scene payload consumed by the staged WebGPU rasterizer.
/// </summary>
internal static class WebGPUSceneEncoder
{
    private const int TileWidth = 16;
    private const int TileHeight = 16;
    private const int FixedShift = 8;
    private const int FixedOne = 1 << FixedShift;
    private const byte PathTagLineToF32 = 0x09;
    private const byte PathTagTransform = 0x20;
    private const byte PathTagPath = 0x10;
    private const byte PathTagStyle = 0x40;
    private const byte PathTagSubpathEnd = 0x04;
    private const uint StyleFlagsFill = 0x40000000U;

    /// <summary>
    /// Encodes prepared composition commands into flush-scoped scene buffers.
    /// </summary>
    public static WebGPUEncodedScene Encode(
        IReadOnlyList<CompositionCommand> commands,
        in Rectangle targetBounds,
        MemoryAllocator allocator)
    {
        if (commands.Count == 0)
        {
            return WebGPUEncodedScene.Empty;
        }

        SupportedSubsetSceneEncoding encoding = SupportedSubsetSceneEncoding.Create(commands, targetBounds, allocator);
        try
        {
            if (encoding.IsEmpty)
            {
                return WebGPUEncodedScene.Empty;
            }

            return SupportedSubsetSceneResolver.Resolve(ref encoding, targetBounds, allocator);
        }
        finally
        {
            encoding.Dispose();
        }
    }

    private ref struct SupportedSubsetSceneEncoding
    {
        private bool hasLastStyle;
        private uint lastStyle0;
        private uint lastStyle1;

        private SupportedSubsetSceneEncoding(MemoryAllocator allocator, int commandCount)
        {
            this.PathTags = new OwnedStream<byte>(allocator, Math.Max(commandCount * 8, 256));
            this.PathData = new OwnedStream<uint>(allocator, Math.Max(commandCount * 16, 256));
            this.DrawTags = new OwnedStream<uint>(allocator, Math.Max(commandCount, 16));
            this.DrawData = new OwnedStream<uint>(allocator, Math.Max(commandCount, 16));
            this.Transforms = new OwnedStream<uint>(allocator, 8);
            this.Styles = new OwnedStream<uint>(allocator, Math.Max(commandCount * 2, 16));
            this.FillCount = 0;
            this.PathCount = 0;
            this.LineCount = 0;
            this.InfoWordCount = 0;
            this.TotalTileMembershipCount = 0;
            this.hasLastStyle = false;
            this.lastStyle0 = 0;
            this.lastStyle1 = 0;

            this.PathTags.Add(PathTagTransform);
            AppendIdentityTransform(ref this.Transforms);
        }

        public OwnedStream<byte> PathTags;

        public OwnedStream<uint> PathData;

        public OwnedStream<uint> DrawTags;

        public OwnedStream<uint> DrawData;

        public OwnedStream<uint> Transforms;

        public OwnedStream<uint> Styles;

        public int FillCount { get; private set; }

        public int PathCount { get; private set; }

        public int LineCount { get; private set; }

        public int InfoWordCount { get; private set; }

        public int TotalTileMembershipCount { get; private set; }

        public readonly bool IsEmpty => this.FillCount == 0;

        public static SupportedSubsetSceneEncoding Create(
            IReadOnlyList<CompositionCommand> commands,
            in Rectangle targetBounds,
            MemoryAllocator allocator)
        {
            SupportedSubsetSceneEncoding encoding = new(allocator, commands.Count);
            encoding.Build(commands, targetBounds);
            return encoding;
        }

        public void Dispose()
        {
            this.Styles.Dispose();
            this.Transforms.Dispose();
            this.DrawData.Dispose();
            this.DrawTags.Dispose();
            this.PathData.Dispose();
            this.PathTags.Dispose();
        }

        private void Build(IReadOnlyList<CompositionCommand> commands, in Rectangle targetBounds)
        {
            for (int i = 0; i < commands.Count; i++)
            {
                this.Append(commands[i], targetBounds);
            }
        }

        private void Append(in CompositionCommand command, in Rectangle targetBounds)
        {
            if (!command.IsVisible)
            {
                return;
            }

            IPath preparedPath = command.PreparedPath
                ?? throw new InvalidOperationException("Commands must be prepared before GPU scene encoding.");

            uint drawTag = GetDrawTag(command);
            GpuSceneDrawMonoid drawTagMonoid = GpuSceneDrawTag.Map(drawTag);
            (uint style0, uint style1) = GetFillStyle(command.RasterizerOptions.IntersectionRule);
            int pathTagCheckpoint = this.PathTags.Count;
            int styleCheckpoint = this.Styles.Count;

            if (!this.hasLastStyle || style0 != this.lastStyle0 || style1 != this.lastStyle1)
            {
                this.PathTags.Add(PathTagStyle);
                this.Styles.Add(style0);
                this.Styles.Add(style1);
            }

            int encodedPathCount = EncodePath(
                command,
                preparedPath,
                ref this.PathTags,
                ref this.PathData,
                out int geometryLineCount,
                out _);

            if (encodedPathCount == 0)
            {
                this.PathTags.SetCount(pathTagCheckpoint);
                this.Styles.SetCount(styleCheckpoint);
                return;
            }

            this.hasLastStyle = true;
            this.lastStyle0 = style0;
            this.lastStyle1 = style1;
            this.FillCount++;
            this.PathCount += encodedPathCount;
            this.LineCount += geometryLineCount;
            this.InfoWordCount += (int)drawTagMonoid.InfoOffset;
            this.DrawTags.Add(drawTag);
            this.DrawData.Add(PackSolidColor(command.Brush));

            Rectangle destinationRegion = command.DestinationRegion;
            int tileMinX = Math.Max(0, destinationRegion.Left / TileWidth);
            int tileMinY = Math.Max(0, destinationRegion.Top / TileHeight);
            int tileMaxX = Math.Max(tileMinX + 1, DivideRoundUp(destinationRegion.Right, TileWidth));
            int tileMaxY = Math.Max(tileMinY + 1, DivideRoundUp(destinationRegion.Bottom, TileHeight));
            this.TotalTileMembershipCount += (tileMaxX - tileMinX) * (tileMaxY - tileMinY);
        }
    }

    private static class SupportedSubsetSceneResolver
    {
        public static WebGPUEncodedScene Resolve(
            ref SupportedSubsetSceneEncoding encoding,
            in Rectangle targetBounds,
            MemoryAllocator allocator)
        {
            int pathTagByteCount = encoding.PathTags.Count;
            int pathTagWordCount = AlignUp(DivideRoundUp(pathTagByteCount, 4), 256);
            int pathDataWordCount = encoding.PathData.Count;
            int drawTagCount = encoding.DrawTags.Count;
            int drawDataWordCount = encoding.DrawData.Count;
            int transformWordCount = encoding.Transforms.Count;
            int styleWordCount = encoding.Styles.Count;
            int drawTagBase = pathTagWordCount + pathDataWordCount;
            int drawDataBase = drawTagBase + drawTagCount;
            int transformBase = drawDataBase + drawDataWordCount;
            int styleBase = transformBase + transformWordCount;
            int sceneWordCount = styleBase + styleWordCount;
            GpuSceneLayout layout = new(
                (uint)encoding.FillCount,
                (uint)encoding.PathCount,
                0U,
                (uint)encoding.InfoWordCount,
                0U,
                (uint)pathTagWordCount,
                (uint)drawTagBase,
                (uint)drawDataBase,
                (uint)transformBase,
                (uint)styleBase);

            IMemoryOwner<uint> sceneDataOwner = allocator.Allocate<uint>(sceneWordCount);
            try
            {
                PackSceneData(
                    layout,
                    pathTagWordCount,
                    encoding.PathTags.WrittenSpan,
                    encoding.PathData.WrittenSpan,
                    encoding.DrawTags.WrittenSpan,
                    encoding.DrawData.WrittenSpan,
                    encoding.Transforms.WrittenSpan,
                    encoding.Styles.WrittenSpan,
                    sceneDataOwner.Memory.Span[..sceneWordCount]);

                return new WebGPUEncodedScene(
                    targetBounds.Size,
                    encoding.InfoWordCount,
                    sceneDataOwner,
                    sceneWordCount,
                    layout,
                    encoding.FillCount,
                    encoding.PathCount,
                    encoding.LineCount,
                    pathTagByteCount,
                    pathTagWordCount,
                    pathDataWordCount,
                    drawTagCount,
                    drawDataWordCount,
                    transformWordCount,
                    styleWordCount,
                    0,
                    0,
                    0,
                    encoding.TotalTileMembershipCount,
                    0,
                    DivideRoundUp(targetBounds.Width, TileWidth),
                    DivideRoundUp(targetBounds.Height, TileHeight));
            }
            catch
            {
                sceneDataOwner.Dispose();
                throw;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetDrawTag(in CompositionCommand command)
        => GpuSceneDrawTag.FillColor;

    private static int EncodePath(
        in CompositionCommand command,
        IPath preparedPath,
        ref OwnedStream<byte> pathTags,
        ref OwnedStream<uint> pathData,
        out int lineCount,
        out int pathSegmentCount)
    {
        Rectangle interest = command.RasterizerOptions.Interest;
        float samplingOffset = command.RasterizerOptions.SamplingOrigin == RasterizerSamplingOrigin.PixelCenter ? 0.5F : 0F;
        float translateY = command.DestinationRegion.Y - command.SourceOffset.Y;
        pathSegmentCount = 0;
        lineCount = 0;

        foreach (ISimplePath subPath in preparedPath.Flatten())
        {
            ReadOnlySpan<PointF> points = subPath.Points.Span;
            if (points.Length < 2)
            {
                continue;
            }

            int segmentCount = subPath.IsClosed ? points.Length : points.Length - 1;
            if (segmentCount == 0)
            {
                continue;
            }

            // Emit moveto for the first point of each subpath.
            PointF startPoint = points[0];
            float firstX = startPoint.X + samplingOffset;
            float firstY = startPoint.Y + samplingOffset;
            pathData.Add(BitcastSingle(firstX));
            pathData.Add(BitcastSingle(firstY));
            float currentX = firstX;
            float currentY = firstY;

            for (int j = 0; j < segmentCount; j++)
            {
                PointF p0 = points[j];
                PointF p1 = points[(j + 1) % points.Length];
                if (PointsEqual(p0, p1))
                {
                    continue;
                }

                float translatedEndX = p1.X + samplingOffset;
                float translatedEndY = p1.Y + samplingOffset;
                pathData.Add(BitcastSingle(translatedEndX));
                pathData.Add(BitcastSingle(translatedEndY));
                pathTags.Add(PathTagLineToF32);
                pathSegmentCount++;
                currentX = translatedEndX;
                currentY = translatedEndY;

                float y0 = ((p0.Y - interest.Top) + samplingOffset) + translateY;
                float y1 = ((p1.Y - interest.Top) + samplingOffset) + translateY;
                int fy0 = (int)MathF.Round(y0 * FixedOne);
                int fy1 = (int)MathF.Round(y1 * FixedOne);
                if (fy0 != fy1)
                {
                    lineCount++;
                }
            }

            // Close the subpath.
            if (!PointsEqual(firstX, firstY, currentX, currentY))
            {
                pathData.Add(BitcastSingle(firstX));
                pathData.Add(BitcastSingle(firstY));
                pathTags.Add(PathTagLineToF32);
                pathSegmentCount++;
            }

            if (pathTags.Count > 0)
            {
                pathTags[pathTags.Count - 1] |= PathTagSubpathEnd;
            }
        }

        if (pathSegmentCount == 0)
        {
            return 0;
        }

        pathTags.Add(PathTagPath);
        return 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendIdentityTransform(ref OwnedStream<uint> transforms)
    {
        transforms.Add(BitcastSingle(1F));
        transforms.Add(BitcastSingle(0F));
        transforms.Add(BitcastSingle(0F));
        transforms.Add(BitcastSingle(1F));
        transforms.Add(BitcastSingle(0F));
        transforms.Add(BitcastSingle(0F));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (uint Style0, uint Style1) GetFillStyle(IntersectionRule intersectionRule)
        => (intersectionRule == IntersectionRule.EvenOdd ? StyleFlagsFill : 0U, 0U);

    private static void PackSceneData(
        GpuSceneLayout layout,
        int pathTagWordCount,
        ReadOnlySpan<byte> pathTags,
        ReadOnlySpan<uint> pathData,
        ReadOnlySpan<uint> drawTags,
        ReadOnlySpan<uint> drawData,
        ReadOnlySpan<uint> transforms,
        ReadOnlySpan<uint> styles,
        Span<uint> sceneWords)
    {
        Span<byte> sceneBytes = MemoryMarshal.Cast<uint, byte>(sceneWords);
        int paddedPathTagBytes = checked(pathTagWordCount * sizeof(uint));
        sceneBytes[..paddedPathTagBytes].Clear();
        pathTags.CopyTo(sceneBytes);
        pathData.CopyTo(sceneWords[(int)layout.PathDataBase..]);
        drawTags.CopyTo(sceneWords[(int)layout.DrawTagBase..]);
        drawData.CopyTo(sceneWords[(int)layout.DrawDataBase..]);
        transforms.CopyTo(sceneWords[(int)layout.TransformBase..]);
        styles.CopyTo(sceneWords[(int)layout.StyleBase..]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PointsEqual(PointF a, PointF b)
        => a.X == b.X && a.Y == b.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool PointsEqual(float ax, float ay, float bx, float by)
        => ax == bx && ay == by;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BitcastSingle(float value)
        => unchecked((uint)BitConverter.SingleToInt32Bits(value));

    private static int CountLineTileSlices(float x0, float y0, float x1, float y1)
    {
        if (x0 == x1 && y0 == y1)
        {
            return 0;
        }

        bool isDown = y1 >= y0;
        float startX = isDown ? x0 : x1;
        float startY = isDown ? y0 : y1;
        float endX = isDown ? x1 : x0;
        float endY = isDown ? y1 : y0;
        float s0x = startX / TileWidth;
        float s0y = startY / TileHeight;
        float s1x = endX / TileWidth;
        float s1y = endY / TileHeight;

        int spanX = Math.Max((int)MathF.Ceiling(MathF.Max(s0x, s1x)) - (int)MathF.Floor(MathF.Min(s0x, s1x)), 1);
        int spanY = Math.Max((int)MathF.Ceiling(MathF.Max(s0y, s1y)) - (int)MathF.Floor(MathF.Min(s0y, s1y)), 1);
        return checked((spanX - 1) + spanY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DivideRoundUp(int value, int divisor)
        => (value + divisor - 1) / divisor;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int AlignUp(int value, int alignment)
        => value + ((alignment - (value % alignment)) % alignment);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint PackSolidColor(Brush brush)
    {
        if (brush is not SolidBrush solidBrush)
        {
            return 0;
        }

        Vector4 color = solidBrush.Color.ToScaledVector4();
        color.X *= color.W;
        color.Y *= color.W;
        color.Z *= color.W;
        return Rgba32.FromScaledVector4(color).Rgba;
    }
}

/// <summary>
/// Flush-scoped encoded scene payload.
/// </summary>
internal sealed class WebGPUEncodedScene : IDisposable
{
    public static WebGPUEncodedScene Empty { get; } = new(
        Size.Empty,
        0,
        null,
        0,
        default,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);

    private readonly IMemoryOwner<uint>? sceneDataOwner;
    private bool disposed;

    public WebGPUEncodedScene(
        Size targetSize,
        int infoWordCount,
        IMemoryOwner<uint>? sceneDataOwner,
        int sceneWordCount,
        GpuSceneLayout layout,
        int fillCount,
        int pathCount,
        int lineCount,
        int pathTagByteCount,
        int pathTagWordCount,
        int pathDataWordCount,
        int drawTagCount,
        int drawDataWordCount,
        int transformWordCount,
        int styleWordCount,
        int clipCount,
        int uniqueDefinitionCount,
        int totalBinMembershipCount,
        int totalTileMembershipCount,
        int totalLineSliceCount,
        int tileCountX,
        int tileCountY)
    {
        this.TargetSize = targetSize;
        this.InfoWordCount = infoWordCount;
        this.sceneDataOwner = sceneDataOwner;
        this.SceneWordCount = sceneWordCount;
        this.Layout = layout;
        this.FillCount = fillCount;
        this.PathCount = pathCount;
        this.LineCount = lineCount;
        this.PathTagByteCount = pathTagByteCount;
        this.PathTagWordCount = pathTagWordCount;
        this.PathDataWordCount = pathDataWordCount;
        this.DrawTagCount = drawTagCount;
        this.DrawDataWordCount = drawDataWordCount;
        this.TransformWordCount = transformWordCount;
        this.StyleWordCount = styleWordCount;
        this.ClipCount = clipCount;
        this.UniqueDefinitionCount = uniqueDefinitionCount;
        this.TotalBinMembershipCount = totalBinMembershipCount;
        this.TotalTileMembershipCount = totalTileMembershipCount;
        this.TotalLineSliceCount = totalLineSliceCount;
        this.TileCountX = tileCountX;
        this.TileCountY = tileCountY;
    }

    public Size TargetSize { get; }

    public ReadOnlyMemory<uint> SceneData
        => this.sceneDataOwner is null ? ReadOnlyMemory<uint>.Empty : this.sceneDataOwner.Memory[..this.SceneWordCount];

    public int FillCount { get; }

    public int InfoWordCount { get; }

    public int SceneWordCount { get; }

    public int PathCount { get; }

    public int LineCount { get; }

    public int PathTagByteCount { get; }

    public int PathTagWordCount { get; }

    public int PathDataWordCount { get; }

    public int DrawTagCount { get; }

    public int DrawDataWordCount { get; }

    public int TransformWordCount { get; }

    public int StyleWordCount { get; }

    public int ClipCount { get; }

    public int UniqueDefinitionCount { get; }

    public int TotalBinMembershipCount { get; }

    public int TotalTileMembershipCount { get; }

    public int TotalLineSliceCount { get; }

    public int TileCountX { get; }

    public int TileCountY { get; }

    public int TileCount => this.TileCountX * this.TileCountY;

    public GpuSceneLayout Layout { get; }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.sceneDataOwner?.Dispose();
    }
}

internal ref struct OwnedStream<T>
    where T : unmanaged
{
    private readonly MemoryAllocator allocator;
    private IMemoryOwner<T> owner;
    private Span<T> span;

    public OwnedStream(MemoryAllocator allocator, int initialCapacity)
    {
        this.allocator = allocator;
        this.owner = allocator.Allocate<T>(Math.Max(initialCapacity, 16));
        this.span = this.owner.Memory.Span;
        this.Count = 0;
    }

    public int Count { get; private set; }

    public ref T this[int index] => ref this.span[index];

    public readonly ReadOnlySpan<T> WrittenSpan => this.span[..this.Count];

    public void Add(T value)
    {
        this.EnsureCapacity(this.Count + 1);
        this.span[this.Count++] = value;
    }

    public void SetCount(int count) => this.Count = count;

    public void Dispose()
    {
        this.owner.Dispose();
        this.span = default;
        this.Count = 0;
    }

    private void EnsureCapacity(int requiredCapacity)
    {
        if (requiredCapacity <= this.span.Length)
        {
            return;
        }

        IMemoryOwner<T> next = this.allocator.Allocate<T>(Math.Max(requiredCapacity, this.span.Length * 2));
        this.span[..this.Count].CopyTo(next.Memory.Span);
        this.owner.Dispose();
        this.owner = next;
        this.span = next.Memory.Span;
    }
}

#pragma warning restore SA1201
