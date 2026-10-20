// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// The D3 AQR symbology (Accessible QR): a QR Code symbol, the host, with a D3 component around its lower right corner. The D3 component is a reversed L of two
/// arms of dashes and dots that a camera finds from a distance, and it carries a 14-bit category, 7 bits along each arm with a parity module for each arm. The
/// host carries the data as <see cref="QrCodeSymbology"/> does.
/// <para>
/// The symbol is a grid of cells. At a <see cref="HostRatio"/> of 2, 2.5 or 3, a host module is 1, 4 or 2 cells on a side, and half a D3 module is 1, 5 or
/// 3 cells. A module of the X dimension is one cell.
/// </para>
/// </summary>
public sealed class D3AqrSymbology : BarcodeSymbology
{
    /// <summary>
    /// The largest category, the 14 bits the two arms carry.
    /// </summary>
    private const int MaximumCategory = 16383;

    /// <summary>
    /// Initializes a new instance of the <see cref="D3AqrSymbology"/> class with a host at error correction level M and a D3 module 2 host modules wide.
    /// </summary>
    /// <param name="category">The category, 0 to 16383.</param>
    public D3AqrSymbology(int category)
        : this(category, QrErrorCorrectionLevel.M, D3AqrHostRatio.Two)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="D3AqrSymbology"/> class.
    /// </summary>
    /// <param name="category">The category, 0 to 16383.</param>
    /// <param name="errorCorrectionLevel">The error correction level of the host.</param>
    /// <param name="hostRatio">The width of a D3 module in host modules.</param>
    public D3AqrSymbology(int category, QrErrorCorrectionLevel errorCorrectionLevel, D3AqrHostRatio hostRatio)
    {
        Guard.MustBeBetweenOrEqualTo(category, 0, MaximumCategory, nameof(category));
        Guard.MustBeBetweenOrEqualTo((int)errorCorrectionLevel, (int)QrErrorCorrectionLevel.L, (int)QrErrorCorrectionLevel.H, nameof(errorCorrectionLevel));
        Guard.MustBeBetweenOrEqualTo((int)hostRatio, (int)D3AqrHostRatio.Two, (int)D3AqrHostRatio.Three, nameof(hostRatio));
        this.Category = category;
        this.ErrorCorrectionLevel = errorCorrectionLevel;
        this.HostRatio = hostRatio;
    }

    /// <summary>
    /// Gets the category, 0 to 16383.
    /// </summary>
    public int Category { get; }

    /// <summary>
    /// Gets the error correction level of the host.
    /// </summary>
    public QrErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>
    /// Gets the width of a D3 module in host modules.
    /// </summary>
    public D3AqrHostRatio HostRatio { get; }

    /// <inheritdoc/>
    internal override void Encode(ReadOnlySpan<char> text, BarcodeOptions options, ref BarcodeSymbol symbol)
    {
        // Half a D3 module and a host module must both be whole cells. At a ratio of 2, 2.5 or 3, half a D3 module is 1, 5 or 3 cells, and a host module is
        // 1, 4 or 2 cells.
        (int half, int hostCellSize) = this.HostRatio switch
        {
            D3AqrHostRatio.Two => (1, 1),
            D3AqrHostRatio.TwoAndAHalf => (5, 4),
            _ => (3, 2),
        };

        double xDimension = options.ResolveXDimension(DefaultXDimension);
        BarcodeSymbol host = default;
        try
        {
            QrEncoder.BuildSymbol(text, false, QrFormat.Full, this.ErrorCorrectionLevel, 0, xDimension, ref host);

            // The arms reach 35 half modules from the lower right corner and stand in a margin of 6 half modules to the right of and below the host. The host
            // keeps at least 2 half modules, one D3 module, above and to its left, and more where it is too small to reach the ends of the arms.
            int hostSize = host.Columns;
            int hostCells = hostSize * hostCellSize;
            int hostOffset = Math.Max(2 * half, (29 * half) - hostCells);
            int size = hostOffset + hostCells + (6 * half);

            // Section 5.3.7 of ISO/IEC 18004: the host has a 4X quiet zone. The margins to the right and below are wider than 4 host modules at every ratio, so
            // the quiet zone around the grid makes up what the offset above and to the left of the host lacks.
            int quietZone = Math.Max(0, (4 * hostCellSize) - hostOffset);
            Span<bool> modules = symbol.BeginMatrix(size, size, quietZone, xDimension);

            ReadOnlySpan<bool> hostModules = host.Modules;
            for (int y = 0; y < hostCells; y++)
            {
                int hostRow = y / hostCellSize * hostSize;
                Span<bool> row = modules.Slice(((hostOffset + y) * size) + hostOffset, hostCells);
                for (int x = 0; x < hostCells; x++)
                {
                    row[x] = hostModules[hostRow + (x / hostCellSize)];
                }
            }

            // The D3 component, in half modules from the lower right corner, x to the left and y upwards: the elbow, an arm module on each side, the four
            // half-module dash tips and two dots on each arm.
            CellGrid grid = new(modules, size, half);
            grid.PaintModule(2, 2);
            grid.PaintModule(4, 2);
            grid.PaintModule(2, 4);
            grid.PaintCell(6, 2);
            grid.PaintCell(6, 3);
            grid.PaintCell(2, 6);
            grid.PaintCell(3, 6);
            grid.PaintModule(9, 2);
            grid.PaintModule(2, 9);
            grid.PaintModule(13, 2);
            grid.PaintModule(2, 13);

            // Category bits 0 to 6 stand up the right arm, a dark module for a 1, with a timing module beside every second bit, and a parity module above them
            // that is dark when the bits hold an odd number of ones. Bits 7 to 13 stand along the bottom arm the same way, and their parity module is dark when
            // they hold an even number of ones.
            int category = this.Category;
            int parity = 0;
            for (int i = 0; i <= 6; i++)
            {
                int y = 31 - (2 * i);
                if (i % 2 == 0)
                {
                    grid.PaintModule(0, y);
                }

                int bit = (category >> i) & 1;
                if (bit == 1)
                {
                    grid.PaintModule(2, y);
                }

                parity ^= bit;
            }

            if (parity == 1)
            {
                grid.PaintModule(2, 33);
            }

            parity = 1;
            for (int i = 7; i <= 13; i++)
            {
                int x = 45 - (2 * i);
                if (i % 2 == 1)
                {
                    grid.PaintModule(x, 0);
                }

                int bit = (category >> i) & 1;
                if (bit == 1)
                {
                    grid.PaintModule(x, 2);
                }

                parity ^= bit;
            }

            if (parity == 1)
            {
                grid.PaintModule(33, 2);
            }
        }
        finally
        {
            host.Dispose();
        }
    }

    /// <summary>
    /// The cell grid of a D3 AQR symbol, addressed in half modules from its lower right corner.
    /// </summary>
    private readonly ref struct CellGrid
    {
        /// <summary>
        /// The cells, row-major.
        /// </summary>
        private readonly Span<bool> modules;

        /// <summary>
        /// The number of cells on a side.
        /// </summary>
        private readonly int size;

        /// <summary>
        /// The number of cells on a side of a half module.
        /// </summary>
        private readonly int half;

        /// <summary>
        /// Initializes a new instance of the <see cref="CellGrid"/> struct.
        /// </summary>
        /// <param name="modules">The cells, row-major, <paramref name="size"/> x <paramref name="size"/> long.</param>
        /// <param name="size">The number of cells on a side.</param>
        /// <param name="half">The number of cells on a side of a half module.</param>
        public CellGrid(Span<bool> modules, int size, int half)
        {
            this.modules = modules;
            this.size = size;
            this.half = half;
        }

        /// <summary>
        /// Darkens one D3 module, 2 x 2 half modules, whose lower right half module stands at a position.
        /// </summary>
        /// <param name="x">The position in half modules to the left of the lower right corner.</param>
        /// <param name="y">The position in half modules above the lower right corner.</param>
        public void PaintModule(int x, int y)
        {
            this.PaintCell(x, y);
            this.PaintCell(x, y + 1);
            this.PaintCell(x + 1, y);
            this.PaintCell(x + 1, y + 1);
        }

        /// <summary>
        /// Darkens one half module.
        /// </summary>
        /// <param name="x">The position in half modules to the left of the lower right corner.</param>
        /// <param name="y">The position in half modules above the lower right corner.</param>
        public void PaintCell(int x, int y)
        {
            int half = this.half;
            int bottom = this.size - 1 - (y * half);
            int right = this.size - 1 - (x * half);
            for (int row = bottom - half + 1; row <= bottom; row++)
            {
                this.modules.Slice((row * this.size) + right - half + 1, half).Fill(true);
            }
        }
    }
}
