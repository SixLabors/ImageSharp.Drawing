// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Barcodes;

/// <summary>
/// Validates a GS1 Digital Link URI, the GS1 Digital Link Standard: URI Syntax, release 1.7.0: the scheme and the host, the primary identification key and its
/// key qualifiers in the path, and the data attributes in the query string, with the GS1 data rules applied to every Application Identifier the URI carries.
/// </summary>
internal static class Gs1DigitalLink
{
    /// <summary>
    /// The characters a URI can carry, RFC 3986: the unreserved characters, the reserved characters and the percent sign of percent-encoding.
    /// </summary>
    private static readonly SearchValues<char> UriCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~:/?#[]@!$&'()*+,;=%");

    /// <summary>
    /// The characters section 4.2 of the GS1 Digital Link Standard says "must be represented using percent-encoding" in a value, other than "#", "/", "%",
    /// "&amp;" and "?", which end a value or start a percent-encoding before the value is read.
    /// </summary>
    private static readonly SearchValues<char> PercentEncodedCharacters = SearchValues.Create("!+,()*';:<=>");

    /// <summary>
    /// Validates the URI.
    /// </summary>
    /// <param name="uri">The URI.</param>
    /// <exception cref="ArgumentException">The URI is not a valid GS1 Digital Link URI.</exception>
    public static void Validate(ReadOnlySpan<char> uri)
        => Validate(uri, false, default);

    /// <summary>
    /// Validates the URI and writes the human readable interpretation of the element strings it carries, each on a line of its own as
    /// <see cref="Gs1Data.WriteReadable(ReadOnlySpan{char}, ReadOnlySpan{char}, bool, Span{char}, int)"/> writes it, in the order of the URI: the primary key
    /// and its qualifiers in the path, then the data attributes in the query string.
    /// </summary>
    /// <param name="uri">The URI.</param>
    /// <param name="readable">The buffer the interpretation is written to, <see cref="ReadableCapacity"/> long.</param>
    /// <returns>The number of characters written.</returns>
    /// <exception cref="ArgumentException">The URI is not a valid GS1 Digital Link URI.</exception>
    public static int Validate(ReadOnlySpan<char> uri, Span<char> readable)
        => Validate(uri, true, readable);

    /// <summary>
    /// Returns the number of characters <see cref="Validate(ReadOnlySpan{char}, Span{char})"/> writes at most for a URI. Every decoded Application Identifier
    /// and value character comes from at least one character of the URI, and each element string adds two parentheses and a line break to them. The URI holds
    /// at most a quarter of its length in element strings.
    /// </summary>
    /// <param name="uriLength">The length of the URI.</param>
    /// <returns>The capacity the interpretation needs.</returns>
    public static int ReadableCapacity(int uriLength) => uriLength + (3 * ((uriLength / 4) + 1));

    /// <summary>
    /// Validates the URI and, when asked, writes the human readable interpretation of its element strings.
    /// </summary>
    /// <param name="uri">The URI.</param>
    /// <param name="writeReadable">Whether the interpretation is written.</param>
    /// <param name="readable">The buffer the interpretation is written to when <paramref name="writeReadable"/> is <see langword="true"/>.</param>
    /// <returns>The number of characters written.</returns>
    /// <exception cref="ArgumentException">The URI is not a valid GS1 Digital Link URI.</exception>
    private static int Validate(ReadOnlySpan<char> uri, bool writeReadable, Span<char> readable)
    {
        int bad = uri.IndexOfAnyExcept(UriCharacters);
        if (bad >= 0)
        {
            throw new ArgumentException($"A URI does not carry '{uri[bad]}', at position {bad + 1}; percent-encode it.", nameof(uri));
        }

        // Section 4.11: the scheme is "http", "https", "HTTP" or "HTTPS".
        ReadOnlySpan<char> rest;
        if (uri.StartsWith("https://") || uri.StartsWith("HTTPS://"))
        {
            rest = uri[8..];
        }
        else if (uri.StartsWith("http://") || uri.StartsWith("HTTP://"))
        {
            rest = uri[7..];
        }
        else
        {
            throw new ArgumentException("A GS1 Digital Link URI starts with http:// or https://.", nameof(uri));
        }

        // The host runs to the first slash. Section 4.11 gives it no user information, so it carries no "@", and it cannot be empty.
        int slash = rest.IndexOf('/');
        if (slash <= 0 || rest[..slash].Contains('@'))
        {
            throw new ArgumentException("A GS1 Digital Link URI has a host name and then a path.", nameof(uri));
        }

        // Section 4.11 builds the URI from the path and the query string with no fragment, and section 4.2 percent-encodes "#" in data, so the URI has no "#".
        ReadOnlySpan<char> path = rest[slash..];
        if (path.Contains('#'))
        {
            throw new ArgumentException("A GS1 Digital Link URI has no fragment; percent-encode \"#\" as %23.", nameof(uri));
        }

        ReadOnlySpan<char> query = [];
        int question = path.IndexOf('?');
        if (question >= 0)
        {
            query = path[(question + 1)..];
            path = path[..question];
        }

        // The decoded Application Identifiers and values collect in one buffer, which the GS1 data rules then read through the element positions. Every decoded
        // character comes from at least one character of the URI, so the buffer never holds more characters than the URI. An element takes at least four
        // characters of the URI, "AI=v" in the query or "/AI/v" in the path, so the URI holds at most a quarter of its length in elements. Neither builder
        // grows.
        InlineArray64<char> bufferStorage = default;
        InlineArray16<Gs1Element> elementStorage = default;
        int elementCapacity = (uri.Length / 4) + 1;
        ValueStringBuilder buffer = uri.Length <= Gs1Data.InlineBufferLength ? new(bufferStorage) : new(uri.Length);
        ValueListBuilder<Gs1Element> elements = elementCapacity <= Gs1Data.InlineElementCount ? new(elementStorage) : new(elementCapacity);
        try
        {
            int pathCount = ParsePath(path, ref buffer, ref elements);
            ParseQuery(query, ref buffer, ref elements);

            ReadOnlySpan<char> decoded = buffer.AsSpan();
            ReadOnlySpan<Gs1Element> parsed = elements.AsSpan();
            Gs1ApplicationIdentifier key = parsed[0].Entry;

            // Section 4.9: the path is the key and then its qualifiers, in the order of one of the qualifier sequences of the key, any of them left out.
            if (!IsKeyPath(key, decoded, parsed[1..pathCount], -1, default))
            {
                throw new ArgumentException(
                    $"The path of the GS1 Digital Link URI is not a primary key ({parsed[0].Identifier(decoded)}) with its qualifiers in order.",
                    nameof(uri));
            }

            for (int i = pathCount; i < parsed.Length; i++)
            {
                // Section 4.10: the query string carries the data attributes the standard lists.
                ReadOnlySpan<char> identifier = parsed[i].Identifier(decoded);
                if (!parsed[i].Entry.DataAttribute)
                {
                    throw new ArgumentException($"The Application Identifier ({identifier}) is not a data attribute of a GS1 Digital Link URI.", nameof(uri));
                }

                // A qualifier of the key in the path belongs in the path, at its place in the sequence.
                for (int position = 0; position < pathCount; position++)
                {
                    if (IsKeyPath(key, decoded, parsed[1..pathCount], position, parsed[i]))
                    {
                        throw new ArgumentException(
                            $"The Application Identifier ({identifier}) qualifies the primary key, so it belongs in the path.",
                            nameof(uri));
                    }
                }
            }

            Gs1Data.Validate(decoded, parsed);
            int written = 0;
            if (writeReadable)
            {
                foreach (Gs1Element element in parsed)
                {
                    written = Gs1Data.WriteReadable(element.Identifier(decoded), element.Data(decoded), true, readable, written);
                }
            }

            return written;
        }
        finally
        {
            elements.Dispose();
            buffer.Dispose();
        }
    }

    /// <summary>
    /// Parses the path: optional path segments before the key, then the primary identification key and its qualifiers as "/AI/value" pairs to the end. The key
    /// is the rightmost pair whose Application Identifier is a primary key, with every pair after it an Application Identifier too.
    /// </summary>
    /// <param name="path">The path, from its first "/".</param>
    /// <param name="buffer">The buffer the decoded Application Identifiers and values are written to.</param>
    /// <param name="elements">The builder the element positions are written to.</param>
    /// <returns>The number of element strings in the path.</returns>
    private static int ParsePath(ReadOnlySpan<char> path, ref ValueStringBuilder buffer, ref ValueListBuilder<Gs1Element> elements)
    {
        // Walk back over the pairs from the end until a pair's Application Identifier is a primary key. A segment that is not an Application Identifier ends
        // the walk, because the pairs must run to the end of the path.
        int keyStart = -1;
        int end = path.Length;
        while (end > 0)
        {
            int valueSlash = path[..end].LastIndexOf('/');
            if (valueSlash <= 0)
            {
                break;
            }

            int identifierSlash = path[..valueSlash].LastIndexOf('/');
            if (identifierSlash < 0)
            {
                break;
            }

            Gs1ApplicationIdentifier? entry = Gs1SyntaxDictionary.Find(path[(identifierSlash + 1)..valueSlash]);
            if (entry is null)
            {
                break;
            }

            if (entry.KeyQualifiers is not null)
            {
                keyStart = identifierSlash;
                break;
            }

            end = identifierSlash;
        }

        if (keyStart < 0)
        {
            throw new ArgumentException("The path of a GS1 Digital Link URI ends with a primary identification key and its qualifiers.", nameof(path));
        }

        // Each pair after the key start is an Application Identifier and its percent-encoded value.
        int count = 0;
        ReadOnlySpan<char> pairs = path[(keyStart + 1)..];
        while (pairs.Length > 0)
        {
            int separator = pairs.IndexOf('/');
            ReadOnlySpan<char> identifier = pairs[..separator];
            ReadOnlySpan<char> value = pairs[(separator + 1)..];
            int next = value.IndexOf('/');
            if (next >= 0)
            {
                pairs = value[(next + 1)..];
                value = value[..next];
            }
            else
            {
                pairs = [];
            }

            AppendElement(identifier, value, ref buffer, ref elements);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Parses the query string. Section 4.11 separates the parameters with "&amp;" or ";". A parameter with an all-digit key is an Application Identifier, and
    /// section 4.10.1 reserves those keys for it. Any other parameter is an extension, which carries no GS1 data.
    /// </summary>
    /// <param name="query">The query string, without "?".</param>
    /// <param name="buffer">The buffer the decoded Application Identifiers and values are written to.</param>
    /// <param name="elements">The builder the element positions are written to.</param>
    private static void ParseQuery(ReadOnlySpan<char> query, ref ValueStringBuilder buffer, ref ValueListBuilder<Gs1Element> elements)
    {
        while (query.Length > 0)
        {
            int separator = query.IndexOfAny('&', ';');
            ReadOnlySpan<char> parameter = separator < 0 ? query : query[..separator];
            query = separator < 0 ? [] : query[(separator + 1)..];

            int equals = parameter.IndexOf('=');
            if (equals <= 0 || parameter[..equals].ContainsAnyExceptInRange('0', '9'))
            {
                continue;
            }

            AppendElement(parameter[..equals], parameter[(equals + 1)..], ref buffer, ref elements);
        }
    }

    /// <summary>
    /// Decodes one Application Identifier and its value into the buffer.
    /// </summary>
    /// <param name="identifier">The Application Identifier.</param>
    /// <param name="value">The percent-encoded value.</param>
    /// <param name="buffer">The buffer the decoded Application Identifier and value are written to.</param>
    /// <param name="elements">The builder the element position is written to.</param>
    private static void AppendElement(
        ReadOnlySpan<char> identifier,
        ReadOnlySpan<char> value,
        ref ValueStringBuilder buffer,
        ref ValueListBuilder<Gs1Element> elements)
    {
        int entryIndex = Gs1SyntaxDictionary.IndexOf(identifier);
        if (entryIndex < 0)
        {
            throw new ArgumentException($"({identifier}) is not an Application Identifier of the GS1 General Specifications.", nameof(identifier));
        }

        if (value.Length == 0)
        {
            throw new ArgumentException($"The Application Identifier ({identifier}) has no value in the GS1 Digital Link URI.", nameof(value));
        }

        int identifierStart = buffer.Length;
        buffer.Append(identifier);
        int dataStart = buffer.Length;

        // Section 4.2: the characters that have special meanings in URIs "must be represented using percent-encoding".
        int literal = value.IndexOfAny(PercentEncodedCharacters);
        if (literal >= 0)
        {
            throw new ArgumentException(
                $"The value of Application Identifier ({identifier}) carries '{value[literal]}', which a GS1 Digital Link URI percent-encodes.",
                nameof(value));
        }

        // Section 4.11: pct-encoded = "%" HEXDIG HEXDIG, the percent-encoding of RFC 3986, so two hexadecimal digits stand for one character.
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '%')
            {
                if (i + 2 >= value.Length || !char.IsAsciiHexDigit(value[i + 1]) || !char.IsAsciiHexDigit(value[i + 2]))
                {
                    throw new ArgumentException(
                        $"The value of Application Identifier ({identifier}) carries a \"%\" without two hexadecimal digits; percent-encode it as %25.",
                        nameof(value));
                }

                int decoded = (HexValue(value[i + 1]) << 4) | HexValue(value[i + 2]);
                if (decoded == 0)
                {
                    throw new ArgumentException($"The value of Application Identifier ({identifier}) decodes to a null character.", nameof(value));
                }

                buffer.Append((char)decoded);
                i += 2;
            }
            else
            {
                buffer.Append(c);
            }
        }

        elements.Append(new Gs1Element(identifierStart, identifier.Length, dataStart, buffer.Length - dataStart, entryIndex));
    }

    /// <summary>
    /// Returns the value of a hexadecimal digit.
    /// </summary>
    /// <param name="c">The digit.</param>
    /// <returns>The value, 0 to 15.</returns>
    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    /// <summary>
    /// Returns whether the qualifiers in the path, with one more qualifier inserted when one is given, form one of the qualifier sequences of the key in order,
    /// with any of its qualifiers left out.
    /// </summary>
    /// <param name="key">The entry of the primary key.</param>
    /// <param name="buffer">The buffer the element strings are in.</param>
    /// <param name="qualifiers">The qualifiers in the path, in order.</param>
    /// <param name="insertAt">The index the extra qualifier goes before, or -1 for none.</param>
    /// <param name="inserted">The extra qualifier.</param>
    /// <returns><see langword="true"/> when the sequence is valid for the key.</returns>
    private static bool IsKeyPath(
        Gs1ApplicationIdentifier key,
        ReadOnlySpan<char> buffer,
        ReadOnlySpan<Gs1Element> qualifiers,
        int insertAt,
        Gs1Element inserted)
    {
        int count = qualifiers.Length + (insertAt >= 0 ? 1 : 0);
        foreach (string[] sequence in key.KeyQualifiers!)
        {
            // Each qualifier must be found in the sequence after the one before it.
            int next = 0;
            bool valid = true;
            for (int q = 0; q < count && valid; q++)
            {
                ReadOnlySpan<char> identifier = q == insertAt
                    ? inserted.Identifier(buffer)
                    : qualifiers[insertAt >= 0 && q > insertAt ? q - 1 : q].Identifier(buffer);

                while (next < sequence.Length && !identifier.SequenceEqual(sequence[next]))
                {
                    next++;
                }

                valid = next < sequence.Length;
                next++;
            }

            if (valid)
            {
                return true;
            }
        }

        return false;
    }
}
