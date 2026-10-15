// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// A binary extension field GF(2^m) and the Reed-Solomon error correction over it. Addition is exclusive
/// or. Multiplication adds logarithms to the base alpha, where alpha is the element 2 and the primitive
/// polynomial reduces each power of alpha into the field.
/// </summary>
internal sealed class GaloisField
{
    /// <summary>
    /// The field of Data Matrix ECC 200: GF(2^8) with the primitive polynomial x^8 + x^5 + x^3 + x^2 + 1,
    /// 301 decimal.
    /// </summary>
    public static readonly GaloisField DataMatrix = new(256, 301);

    /// <summary>
    /// The powers of alpha, alpha^i at index i, written twice: for i from 0 to size - 2 and again from
    /// size - 1 to 2 x (size - 2). The sum of two logarithms is at most 2 x (size - 2), so a product reads its
    /// power directly, with no reduction modulo size - 1.
    /// </summary>
    private readonly int[] exp;

    /// <summary>
    /// The logarithms to the base alpha, i at index alpha^i. Index 0 is not used, because zero has no
    /// logarithm.
    /// </summary>
    private readonly int[] log;

    /// <summary>
    /// Initializes a new instance of the <see cref="GaloisField"/> class.
    /// </summary>
    /// <param name="size">The number of elements, a power of two.</param>
    /// <param name="primitivePolynomial">The primitive polynomial as a bit mask, the highest term included.</param>
    public GaloisField(int size, int primitivePolynomial)
    {
        this.Size = size;
        this.exp = new int[2 * (size - 1)];
        this.log = new int[size];

        // Each power of alpha is the power before it times x. A product that reaches the x^m term is
        // reduced by the primitive polynomial, which clears that term.
        int value = 1;
        for (int i = 0; i < size - 1; i++)
        {
            this.exp[i] = value;
            this.exp[i + size - 1] = value;
            this.log[value] = i;
            value <<= 1;
            if (value >= size)
            {
                value ^= primitivePolynomial;
            }
        }
    }

    /// <summary>
    /// Gets the number of elements in the field.
    /// </summary>
    public int Size { get; }

    /// <summary>
    /// Returns alpha raised to the given power.
    /// </summary>
    /// <param name="power">The power, 0 or more.</param>
    /// <returns>The field element.</returns>
    public int Exp(int power) => this.exp[power % (this.Size - 1)];

    /// <summary>
    /// Returns the product of two field elements.
    /// </summary>
    /// <param name="a">The first element.</param>
    /// <param name="b">The second element.</param>
    /// <returns>The product.</returns>
    public int Multiply(int a, int b)
        => a == 0 || b == 0 ? 0 : this.exp[this.log[a] + this.log[b]];

    /// <summary>
    /// Writes the generator polynomial (x + alpha^r)(x + alpha^(r+1)) ... (x + alpha^(r+k-1)) of a code
    /// with k check symbols, where r is the first root. The polynomial is monic, so the coefficient of
    /// x^k is 1 and is not written.
    /// </summary>
    /// <param name="firstRoot">The power of alpha of the first root.</param>
    /// <param name="generator">The buffer that receives the coefficients of x^0 to x^(k-1), the coefficient of x^j at index j.</param>
    public void Generator(int firstRoot, Span<int> generator)
    {
        // The product grows one factor at a time. Multiplying by (x + a) moves every coefficient up one
        // power and adds a times the coefficient itself, so the update runs from the top down in place.
        // Before factor d the product has degree d - 1, and its leading coefficient, 1, is at index d - 1
        // until the last factor, whose new leading 1 is the implied coefficient of x^k.
        int count = generator.Length;
        generator.Clear();
        generator[0] = 1;
        for (int degree = 1; degree <= count; degree++)
        {
            int root = this.Exp(firstRoot + degree - 1);
            if (degree < count)
            {
                generator[degree] = generator[degree - 1];
            }

            for (int j = degree - 1; j > 0; j--)
            {
                generator[j] = this.Multiply(generator[j], root) ^ generator[j - 1];
            }

            generator[0] = this.Multiply(generator[0], root);
        }
    }

    /// <summary>
    /// Calculates the check symbols of a Reed-Solomon code: the remainder of the data polynomial, the
    /// first symbol as its highest term, times x^k, divided by the generator polynomial.
    /// </summary>
    /// <param name="data">The data symbols. Symbol i of the block is at index i x <paramref name="step"/>.</param>
    /// <param name="step">The distance between two symbols of the block, 1 when the block is not interleaved.</param>
    /// <param name="count">The number of data symbols in the block.</param>
    /// <param name="generator">The generator coefficients of x^0 to x^(k-1), from <see cref="Generator"/>.</param>
    /// <param name="check">The buffer that receives the k check symbols, the highest term first.</param>
    public void Remainder(ReadOnlySpan<int> data, int step, int count, ReadOnlySpan<int> generator, Span<int> check)
    {
        // A linear feedback shift register divides as the symbols arrive. The register holds the running
        // remainder with its highest term at index 0. Each symbol adds to that term, and the sum times the
        // generator is subtracted, which in a binary field is the same exclusive or.
        int[] exp = this.exp;
        int[] log = this.log;
        int k = check.Length;
        check.Clear();
        for (int i = 0; i < count; i++)
        {
            int feedback = data[i * step] ^ check[0];
            if (feedback == 0)
            {
                // A zero feedback adds nothing, so the register only moves up one term.
                check[1..].CopyTo(check);
                check[k - 1] = 0;
                continue;
            }

            // The logarithm of the feedback is the same for every coefficient, so it is read once, and each
            // product is one table read from the sum of two logarithms.
            int feedbackLog = log[feedback];
            for (int j = 0; j < k - 1; j++)
            {
                int coefficient = generator[k - 1 - j];
                check[j] = check[j + 1] ^ (coefficient == 0 ? 0 : exp[log[coefficient] + feedbackLog]);
            }

            check[k - 1] = generator[0] == 0 ? 0 : exp[log[generator[0]] + feedbackLog];
        }
    }
}
