// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Memory;

namespace SixLabors.ImageSharp.Drawing;

/// <summary>
/// Provides the contour profile data used by aliased thin-feature recovery.
/// </summary>
/// <remarks>
/// A profile is a consecutive run of contour segments that continues in one direction on one
/// axis. A change from left to right, right to left, up to down, or down to up starts a new
/// profile. The rasterizer uses the run bounds and contour links to identify a terminating tip.
/// </remarks>
internal readonly struct LinearGeometryProfiles
{
    private const int RecordStride = 3;
    private const int HeaderWordCount = 3;
    private const int SegmentCountOffset = 0;
    private const int XProfileCountOffset = 1;
    private const int YProfileCountOffset = 2;

    private readonly LinearGeometryProfileBuffer? buffer;
    private readonly int offset;
    private readonly int length;

    /// <summary>
    /// The reserved identifier for a segment whose profile cannot be represented in sixteen bits.
    /// </summary>
    public const int SentinelId = ushort.MaxValue;

    /// <summary>
    /// The segment tag whose X and Y fields both contain <see cref="SentinelId"/>.
    /// </summary>
    public const uint SentinelTag = uint.MaxValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGeometryProfiles"/> struct.
    /// </summary>
    /// <param name="buffer">The shared backing buffer that contains the packed profile words.</param>
    /// <param name="offset">The first word of the profile entry.</param>
    /// <param name="length">The number of words in the profile entry.</param>
    internal LinearGeometryProfiles(LinearGeometryProfileBuffer buffer, int offset, int length)
    {
        this.buffer = buffer;
        this.offset = offset;
        this.length = length;
    }

    /// <summary>
    /// Gets a value indicating whether this view contains profile data.
    /// </summary>
    internal bool IsEmpty => this.buffer is null;

    /// <summary>
    /// Gets the number of X-axis profiles.
    /// </summary>
    public int XProfileCount => (int)this.GetData()[XProfileCountOffset];

    /// <summary>
    /// Gets the number of Y-axis profiles.
    /// </summary>
    public int YProfileCount => (int)this.GetData()[YProfileCountOffset];

    /// <summary>
    /// Gets the X-axis and Y-axis profile identifiers for one source segment.
    /// </summary>
    /// <param name="index">The source segment index.</param>
    /// <returns>The X identifier in the low sixteen bits and the Y identifier in the high sixteen bits.</returns>
    public uint GetSegmentTag(int index) => this.GetData()[HeaderWordCount + index];

    /// <summary>
    /// Gets one X-axis profile record.
    /// </summary>
    /// <param name="index">The profile index.</param>
    /// <param name="minimum">Receives the lowest coordinate reached by the profile.</param>
    /// <param name="maximum">Receives the highest coordinate reached by the profile.</param>
    /// <param name="link">Receives the packed contour adjacency data.</param>
    public void GetXProfile(int index, out float minimum, out float maximum, out int link)
    {
        ReadOnlySpan<uint> data = this.GetData();
        GetProfile(data, HeaderWordCount + (int)data[SegmentCountOffset], index, out minimum, out maximum, out link);
    }

    /// <summary>
    /// Gets one Y-axis profile record.
    /// </summary>
    /// <param name="index">The profile index.</param>
    /// <param name="minimum">Receives the lowest coordinate reached by the profile.</param>
    /// <param name="maximum">Receives the highest coordinate reached by the profile.</param>
    /// <param name="link">Receives the packed contour adjacency data.</param>
    public void GetYProfile(int index, out float minimum, out float maximum, out int link)
    {
        ReadOnlySpan<uint> data = this.GetData();
        int baseOffset = HeaderWordCount + (int)data[SegmentCountOffset] + ((int)data[XProfileCountOffset] * RecordStride);
        GetProfile(data, baseOffset, index, out minimum, out maximum, out link);
    }

    /// <summary>
    /// Gets the maximum storage needed to analyze one geometry.
    /// </summary>
    /// <param name="geometry">The geometry to measure.</param>
    /// <returns>The maximum number of packed words required by the analysis.</returns>
    internal static int GetMaximumWordCount(LinearGeometry geometry)
    {
        int segmentCount = geometry.Info.SegmentCount;
        int maximumProfilesPerAxis = Math.Min(segmentCount, SentinelId);
        return checked(HeaderWordCount + segmentCount + (maximumProfilesPerAxis * RecordStride * 2));
    }

    /// <summary>
    /// Analyzes one geometry into caller-owned storage.
    /// </summary>
    /// <remarks>
    /// Each source segment contributes one tag. Each axis profile contributes its minimum,
    /// maximum, and contour link. The method walks the contours once. It writes Y records after
    /// the maximum possible X region, then moves them beside the X records when the actual X count
    /// is known. This small contiguous move avoids a second contour walk.
    /// </remarks>
    /// <param name="geometry">The geometry to analyze.</param>
    /// <param name="data">Storage with at least <see cref="GetMaximumWordCount"/> words.</param>
    /// <returns>The number of words written.</returns>
    internal static int Analyze(LinearGeometry geometry, Span<uint> data)
    {
        int segmentCount = geometry.Info.SegmentCount;
        int maximumProfilesPerAxis = Math.Min(segmentCount, SentinelId);
        int segmentTagOffset = HeaderWordCount;
        int xProfileOffset = segmentTagOffset + segmentCount;
        int yProfileWorkingOffset = xProfileOffset + (maximumProfilesPerAxis * RecordStride);

        ProfileWriter x = new(data.Slice(xProfileOffset, maximumProfilesPerAxis * RecordStride), maximumProfilesPerAxis);
        ProfileWriter y = new(data.Slice(yProfileWorkingOffset, maximumProfilesPerAxis * RecordStride), maximumProfilesPerAxis);

        int segmentOrdinal = 0;
        LinearContour[] contours = (LinearContour[])geometry.Contours;
        PointF[] geometryPoints = (PointF[])geometry.Points;
        for (int i = 0; i < contours.Length; i++)
        {
            LinearContour contour = contours[i];
            if (contour.SegmentCount == 0)
            {
                continue;
            }

            ReadOnlySpan<PointF> points = geometryPoints.AsSpan(contour.PointStart, contour.PointCount);
            PointF current = points[0];
            int xFirst = x.StartRun(current.X, 0);
            int yFirst = y.StartRun(current.Y, 0);

            for (int j = 0; j < contour.SegmentCount; j++)
            {
                int endPointIndex = (j + 1) == points.Length ? 0 : j + 1;
                PointF end = points[endPointIndex];

                x.Advance(current.X, end.X);
                y.Advance(current.Y, end.Y);

                // A reversing segment starts the new run. Read the identifiers after Advance so
                // the segment receives the profile that contains its movement.
                data[segmentTagOffset + segmentOrdinal++] = (uint)(x.CurrentId | (y.CurrentId << 16));
                current = end;
            }

            x.EndContour(contour.IsClosed, xFirst);
            y.EndContour(contour.IsClosed, yFirst);
        }

        int xProfileCount = x.Count;
        int yProfileCount = y.Count;
        int yProfileOffset = xProfileOffset + (xProfileCount * RecordStride);

        // Span.CopyTo supports overlap. Compact only the Y records; tags and X records are already
        // in their final positions.
        data.Slice(yProfileWorkingOffset, yProfileCount * RecordStride).CopyTo(data[yProfileOffset..]);
        data[SegmentCountOffset] = (uint)segmentCount;
        data[XProfileCountOffset] = (uint)xProfileCount;
        data[YProfileCountOffset] = (uint)yProfileCount;
        return yProfileOffset + (yProfileCount * RecordStride);
    }

    /// <summary>
    /// Gets the packed words in this profile view.
    /// </summary>
    /// <returns>The packed profile entry.</returns>
    private ReadOnlySpan<uint> GetData()
    {
        if (this.buffer is LinearGeometryProfileBuffer buffer)
        {
            return buffer.GetSpan(this.offset, this.length);
        }

        return [];
    }

    /// <summary>
    /// Decodes one packed profile record.
    /// </summary>
    /// <param name="data">The complete packed profile entry.</param>
    /// <param name="baseOffset">The first word of the selected axis table.</param>
    /// <param name="index">The profile index.</param>
    /// <param name="minimum">Receives the minimum coordinate.</param>
    /// <param name="maximum">Receives the maximum coordinate.</param>
    /// <param name="link">Receives the contour adjacency data.</param>
    private static void GetProfile(
        ReadOnlySpan<uint> data,
        int baseOffset,
        int index,
        out float minimum,
        out float maximum,
        out int link)
    {
        int recordOffset = baseOffset + (index * RecordStride);
        minimum = BitConverter.Int32BitsToSingle((int)data[recordOffset]);
        maximum = BitConverter.Int32BitsToSingle((int)data[recordOffset + 1]);
        link = (int)data[recordOffset + 2];
    }

    /// <summary>
    /// Builds the profiles for one coordinate axis.
    /// </summary>
    private ref struct ProfileWriter
    {
        private readonly Span<uint> records;
        private readonly int capacity;
        private int direction;
        private float minimum;
        private float maximum;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProfileWriter"/> struct.
        /// </summary>
        /// <param name="records">The destination profile records.</param>
        /// <param name="capacity">The number of records available.</param>
        public ProfileWriter(Span<uint> records, int capacity)
        {
            this.records = records;
            this.capacity = capacity;
            this.direction = 0;
            this.minimum = 0F;
            this.maximum = 0F;
            this.Count = 0;
            this.CurrentId = SentinelId;
        }

        /// <summary>
        /// Gets the number of profiles written.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets the current profile identifier, or <see cref="SentinelId"/> when the table is full.
        /// </summary>
        public int CurrentId { get; private set; }

        /// <summary>
        /// Starts one monotone run.
        /// </summary>
        /// <param name="position">The first coordinate in the run.</param>
        /// <param name="connectedFlag">One when the run follows the preceding run in the contour; otherwise zero.</param>
        /// <returns>The profile identifier, or <see cref="SentinelId"/> when the table is full.</returns>
        public int StartRun(float position, int connectedFlag)
        {
            if (this.Count >= this.capacity)
            {
                this.CurrentId = SentinelId;
                return SentinelId;
            }

            this.CurrentId = this.Count;

            // Bit zero identifies the normal connection to the preceding profile. The remaining
            // bits are populated by EndContour only when a closed contour joins across its end.
            this.records[(this.Count * RecordStride) + 2] = (uint)connectedFlag;
            this.Count++;
            this.direction = 0;
            this.minimum = position;
            this.maximum = position;
            return this.CurrentId;
        }

        /// <summary>
        /// Adds one segment to the current run and opens a new run when the direction reverses.
        /// </summary>
        /// <param name="from">The segment start coordinate.</param>
        /// <param name="to">The segment end coordinate.</param>
        public void Advance(float from, float to)
        {
            float delta = to - from;
            if (delta != 0F)
            {
                int segmentDirection = delta > 0F ? 1 : -1;
                if (this.direction != 0 && segmentDirection != this.direction)
                {
                    // The reversing segment belongs to the new run. Both runs include the shared
                    // extremum so the stub test can compare their closed coordinate ranges.
                    this.CloseRun();
                    _ = this.StartRun(from, 1);
                }

                this.direction = segmentDirection;
            }

            if (to < this.minimum)
            {
                this.minimum = to;
            }
            else if (to > this.maximum)
            {
                this.maximum = to;
            }
        }

        /// <summary>
        /// Closes the final run and records the connection across a closed contour's endpoint.
        /// </summary>
        /// <param name="isClosed">Whether the contour returns to its first point.</param>
        /// <param name="firstId">The first profile identifier in the contour.</param>
        public void EndContour(bool isClosed, int firstId)
        {
            int lastId = this.CurrentId;
            this.CloseRun();
            this.CurrentId = SentinelId;

            if (!isClosed || firstId == SentinelId || lastId == SentinelId || firstId == lastId)
            {
                return;
            }

            int firstLinkOffset = (firstId * RecordStride) + 2;
            int lastLinkOffset = (lastId * RecordStride) + 2;

            // Closing links use one-based identifiers, which leaves zero available for an open
            // contour with no connection across its endpoint.
            this.records[firstLinkOffset] = (this.records[firstLinkOffset] & 1U) | (uint)((lastId + 1) << 1);
            this.records[lastLinkOffset] = (this.records[lastLinkOffset] & 1U) | (uint)((firstId + 1) << 1);
        }

        /// <summary>
        /// Writes the current run's inclusive coordinate range.
        /// </summary>
        private readonly void CloseRun()
        {
            if (this.CurrentId == SentinelId)
            {
                return;
            }

            int recordOffset = this.CurrentId * RecordStride;

            // Preserve the float bits. The CPU decodes them directly, and the GPU converts the
            // same values to target-space fixed point when it packs the final scene.
            this.records[recordOffset] = unchecked((uint)BitConverter.SingleToInt32Bits(this.minimum));
            this.records[recordOffset + 1] = unchecked((uint)BitConverter.SingleToInt32Bits(this.maximum));
        }
    }
}

/// <summary>
/// Holds the growable packed profile words shared by all entries in one partition.
/// </summary>
/// <remarks>
/// Profile views reference this object instead of a particular allocator rental. Growing the
/// buffer can therefore replace the rental without invalidating existing views.
/// </remarks>
internal sealed class LinearGeometryProfileBuffer : IDisposable
{
    private readonly MemoryAllocator allocator;
    private IMemoryOwner<uint> owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGeometryProfileBuffer"/> class.
    /// </summary>
    /// <param name="allocator">The allocator used for backing storage.</param>
    /// <param name="capacity">The initial capacity in packed words.</param>
    public LinearGeometryProfileBuffer(MemoryAllocator allocator, int capacity)
    {
        this.allocator = allocator;
        this.owner = allocator.Allocate<uint>(capacity);
    }

    /// <summary>
    /// Gets writable storage from an offset to the end of the current rental.
    /// </summary>
    /// <param name="offset">The first writable word.</param>
    /// <returns>The writable tail of the buffer.</returns>
    public Span<uint> GetWritableSpan(int offset) => this.owner.Memory.Span[offset..];

    /// <summary>
    /// Gets an immutable range from the current rental.
    /// </summary>
    /// <param name="offset">The first word.</param>
    /// <param name="length">The number of words.</param>
    /// <returns>The requested packed profile words.</returns>
    public ReadOnlySpan<uint> GetSpan(int offset, int length) => this.owner.Memory.Span.Slice(offset, length);

    /// <summary>
    /// Ensures that the buffer can hold the requested total word count.
    /// </summary>
    /// <param name="requiredCapacity">The required total capacity.</param>
    /// <param name="usedLength">The number of initialized words to preserve.</param>
    public void EnsureCapacity(int requiredCapacity, int usedLength)
    {
        if (requiredCapacity <= this.owner.Memory.Length)
        {
            return;
        }

        int nextCapacity = Math.Max(requiredCapacity, checked(this.owner.Memory.Length * 2));
        IMemoryOwner<uint> next = this.allocator.Allocate<uint>(nextCapacity);
        this.owner.Memory.Span[..usedLength].CopyTo(next.Memory.Span);
        this.owner.Dispose();
        this.owner = next;
    }

    /// <summary>
    /// Releases the current allocator rental.
    /// </summary>
    public void Dispose() => this.owner.Dispose();
}

/// <summary>
/// Owns the profile arena for one render or encoding partition.
/// </summary>
/// <remarks>
/// Building is confined to one partition thread. After the partition is complete, raster workers
/// may read its entries concurrently. The arena grows geometrically, so a scene keeps a bounded
/// number of allocator rentals instead of one buffer for every geometry.
/// </remarks>
internal sealed class LinearGeometryProfileStorage : IDisposable
{
    private readonly MemoryAllocator allocator;
    private LinearGeometryProfileBuffer? buffer;
    private LinearGeometry? firstGeometry;
    private LinearGeometryProfiles firstProfiles;
    private Dictionary<LinearGeometry, LinearGeometryProfiles>? cache;
    private int count;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinearGeometryProfileStorage"/> class.
    /// </summary>
    /// <param name="allocator">The allocator used for the growable backing storage.</param>
    public LinearGeometryProfileStorage(MemoryAllocator allocator) => this.allocator = allocator;

    /// <summary>
    /// Gets the existing entry for a geometry or analyzes it into this arena.
    /// </summary>
    /// <param name="geometry">The geometry whose profiles are required.</param>
    /// <returns>A stable offset view into this arena.</returns>
    public LinearGeometryProfiles GetOrAdd(LinearGeometry geometry)
    {
        if (ReferenceEquals(geometry, this.firstGeometry))
        {
            return this.firstProfiles;
        }

        if (this.cache is not null && this.cache.TryGetValue(geometry, out LinearGeometryProfiles cached))
        {
            return cached;
        }

        int maximumWordCount = LinearGeometryProfiles.GetMaximumWordCount(geometry);
        LinearGeometryProfileBuffer buffer = this.buffer ??= new LinearGeometryProfileBuffer(this.allocator, maximumWordCount);
        buffer.EnsureCapacity(checked(this.count + maximumWordCount), this.count);

        int offset = this.count;
        int wordCount = LinearGeometryProfiles.Analyze(geometry, buffer.GetWritableSpan(offset));
        LinearGeometryProfiles profiles = new(buffer, offset, wordCount);
        this.count += wordCount;

        if (this.firstGeometry is null)
        {
            this.firstGeometry = geometry;
            this.firstProfiles = profiles;
            return profiles;
        }

        // Most small scenes contain one aliased geometry. Delay the dictionary until a second
        // distinct geometry appears, while still making repeated glyphs and paths constant-time.
        this.cache ??= new Dictionary<LinearGeometry, LinearGeometryProfiles>
        {
            [this.firstGeometry] = this.firstProfiles
        };
        this.cache.Add(geometry, profiles);
        return profiles;
    }

    /// <summary>
    /// Releases the arena and all geometry references held by its lookup table.
    /// </summary>
    public void Dispose()
    {
        this.buffer?.Dispose();
        this.buffer = null;
        this.firstGeometry = null;
        this.firstProfiles = default;
        this.cache?.Clear();
        this.cache = null;
        this.count = 0;
    }
}
