using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

internal readonly struct UnitJsonSlice
{
    internal UnitJsonSlice(int start, int length)
    {
        Start = start;
        Length = length;
    }

    internal int Start { get; }
    internal int Length { get; }
}

internal static class UnitEliteVariantJsonShape
{
    private const string JsonInvalid = "UNIT_ELITE_VARIANT_JSON_INVALID";

    internal static UnitJsonSlice RootObject(string json, string context)
    {
        if (json == null)
        {
            throw Invalid(context);
        }

        var index = 0;
        SkipWhitespace(json, ref index, json.Length);
        var start = index;
        if (index >= json.Length || json[index] != '{')
        {
            throw Invalid(context);
        }

        SkipValue(json, ref index, json.Length, context);
        var end = index;
        SkipWhitespace(json, ref index, json.Length);
        if (index != json.Length)
        {
            throw Invalid(context);
        }

        return new UnitJsonSlice(start, end - start);
    }

    internal static IReadOnlyDictionary<string, UnitJsonSlice> ReadObject(
        string json,
        UnitJsonSlice slice,
        string context)
    {
        var end = CheckedEnd(json, slice, context);
        var index = slice.Start;
        SkipWhitespace(json, ref index, end);
        if (index >= end || json[index++] != '{')
        {
            throw Invalid(context);
        }

        var result = new Dictionary<string, UnitJsonSlice>(StringComparer.Ordinal);
        SkipWhitespace(json, ref index, end);
        if (index < end && json[index] == '}')
        {
            index++;
            RequireSliceEnd(json, ref index, end, context);
            return result;
        }

        while (index < end)
        {
            SkipWhitespace(json, ref index, end);
            var propertyName = ReadPropertyName(json, ref index, end, context);
            if (propertyName.Length == 0 || result.ContainsKey(propertyName))
            {
                throw Invalid(context);
            }

            SkipWhitespace(json, ref index, end);
            if (index >= end || json[index++] != ':')
            {
                throw Invalid(context);
            }

            SkipWhitespace(json, ref index, end);
            var valueStart = index;
            SkipValue(json, ref index, end, context);
            result.Add(propertyName, new UnitJsonSlice(valueStart, index - valueStart));

            SkipWhitespace(json, ref index, end);
            if (index >= end)
            {
                throw Invalid(context);
            }

            if (json[index] == '}')
            {
                index++;
                RequireSliceEnd(json, ref index, end, context);
                return result;
            }

            if (json[index++] != ',')
            {
                throw Invalid(context);
            }

            SkipWhitespace(json, ref index, end);
            if (index >= end || json[index] == '}')
            {
                throw Invalid(context);
            }
        }

        throw Invalid(context);
    }

    internal static IReadOnlyList<UnitJsonSlice> ReadArray(
        string json,
        UnitJsonSlice slice,
        string context)
    {
        var end = CheckedEnd(json, slice, context);
        var index = slice.Start;
        SkipWhitespace(json, ref index, end);
        if (index >= end || json[index++] != '[')
        {
            throw Invalid(context);
        }

        var result = new List<UnitJsonSlice>();
        SkipWhitespace(json, ref index, end);
        if (index < end && json[index] == ']')
        {
            index++;
            RequireSliceEnd(json, ref index, end, context);
            return result;
        }

        while (index < end)
        {
            SkipWhitespace(json, ref index, end);
            var valueStart = index;
            SkipValue(json, ref index, end, context);
            result.Add(new UnitJsonSlice(valueStart, index - valueStart));

            SkipWhitespace(json, ref index, end);
            if (index >= end)
            {
                throw Invalid(context);
            }

            if (json[index] == ']')
            {
                index++;
                RequireSliceEnd(json, ref index, end, context);
                return result;
            }

            if (json[index++] != ',')
            {
                throw Invalid(context);
            }

            SkipWhitespace(json, ref index, end);
            if (index >= end || json[index] == ']')
            {
                throw Invalid(context);
            }
        }

        throw Invalid(context);
    }

    internal static void RequireExactProperties(
        IReadOnlyDictionary<string, UnitJsonSlice> properties,
        string context,
        params string[] expected)
    {
        if (properties == null || expected == null)
        {
            throw new InvalidOperationException(context);
        }

        var expectedSet = new HashSet<string>(expected, StringComparer.Ordinal);
        var missing = expectedSet
            .Where(name => !properties.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var unexpected = properties.Keys
            .Where(name => !expectedSet.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (missing.Length != 0 || unexpected.Length != 0)
        {
            throw new InvalidOperationException(
                context
                + " missing=" + string.Join(",", missing)
                + " unexpected=" + string.Join(",", unexpected));
        }
    }

    private static int CheckedEnd(string json, UnitJsonSlice slice, string context)
    {
        if (json == null
            || slice.Start < 0
            || slice.Length < 0
            || slice.Start > json.Length - slice.Length)
        {
            throw Invalid(context);
        }

        return slice.Start + slice.Length;
    }

    private static void RequireSliceEnd(
        string json,
        ref int index,
        int end,
        string context)
    {
        SkipWhitespace(json, ref index, end);
        if (index != end)
        {
            throw Invalid(context);
        }
    }

    private static void SkipValue(
        string json,
        ref int index,
        int end,
        string context)
    {
        if (index >= end)
        {
            throw Invalid(context);
        }

        switch (json[index])
        {
            case '"':
                SkipString(json, ref index, end, context);
                return;
            case '{':
                SkipObject(json, ref index, end, context);
                return;
            case '[':
                SkipArray(json, ref index, end, context);
                return;
            case 't':
                SkipLiteral(json, ref index, end, "true", context);
                return;
            case 'f':
                SkipLiteral(json, ref index, end, "false", context);
                return;
            case 'n':
                SkipLiteral(json, ref index, end, "null", context);
                return;
            default:
                SkipNumber(json, ref index, end, context);
                return;
        }
    }

    private static void SkipObject(
        string json,
        ref int index,
        int end,
        string context)
    {
        index++;
        SkipWhitespace(json, ref index, end);
        if (index < end && json[index] == '}')
        {
            index++;
            return;
        }

        while (index < end)
        {
            SkipWhitespace(json, ref index, end);
            ReadPropertyName(json, ref index, end, context);
            SkipWhitespace(json, ref index, end);
            if (index >= end || json[index++] != ':')
            {
                throw Invalid(context);
            }

            SkipWhitespace(json, ref index, end);
            SkipValue(json, ref index, end, context);
            SkipWhitespace(json, ref index, end);
            if (index >= end)
            {
                throw Invalid(context);
            }

            if (json[index] == '}')
            {
                index++;
                return;
            }

            if (json[index++] != ',')
            {
                throw Invalid(context);
            }

            SkipWhitespace(json, ref index, end);
            if (index >= end || json[index] == '}')
            {
                throw Invalid(context);
            }
        }

        throw Invalid(context);
    }

    private static void SkipArray(
        string json,
        ref int index,
        int end,
        string context)
    {
        index++;
        SkipWhitespace(json, ref index, end);
        if (index < end && json[index] == ']')
        {
            index++;
            return;
        }

        while (index < end)
        {
            SkipWhitespace(json, ref index, end);
            SkipValue(json, ref index, end, context);
            SkipWhitespace(json, ref index, end);
            if (index >= end)
            {
                throw Invalid(context);
            }

            if (json[index] == ']')
            {
                index++;
                return;
            }

            if (json[index++] != ',')
            {
                throw Invalid(context);
            }

            SkipWhitespace(json, ref index, end);
            if (index >= end || json[index] == ']')
            {
                throw Invalid(context);
            }
        }

        throw Invalid(context);
    }

    private static string ReadPropertyName(
        string json,
        ref int index,
        int end,
        string context)
    {
        if (index >= end || json[index] != '"')
        {
            throw Invalid(context);
        }

        var builder = new StringBuilder();
        index++;
        while (index < end)
        {
            var character = json[index++];
            if (character == '"')
            {
                if (builder.Length == 0)
                {
                    throw Invalid(context);
                }

                return builder.ToString();
            }

            if (character < 0x20)
            {
                throw Invalid(context);
            }

            if (character != '\\')
            {
                builder.Append(character);
                continue;
            }

            if (index >= end)
            {
                throw Invalid(context);
            }

            var escape = json[index++];
            switch (escape)
            {
                case '"':
                case '\\':
                case '/':
                    builder.Append(escape);
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u':
                    builder.Append(ReadUnicodeEscape(json, ref index, end, context));
                    break;
                default:
                    throw Invalid(context);
            }
        }

        throw Invalid(context);
    }

    private static char ReadUnicodeEscape(
        string json,
        ref int index,
        int end,
        string context)
    {
        if (end - index < 4)
        {
            throw Invalid(context);
        }

        var value = 0;
        for (var digitIndex = 0; digitIndex < 4; digitIndex++)
        {
            var digit = json[index++];
            value <<= 4;
            if (digit >= '0' && digit <= '9')
            {
                value += digit - '0';
            }
            else if (digit >= 'a' && digit <= 'f')
            {
                value += digit - 'a' + 10;
            }
            else if (digit >= 'A' && digit <= 'F')
            {
                value += digit - 'A' + 10;
            }
            else
            {
                throw Invalid(context);
            }
        }

        return (char)value;
    }

    private static void SkipString(
        string json,
        ref int index,
        int end,
        string context)
    {
        index++;
        while (index < end)
        {
            var character = json[index++];
            if (character == '"')
            {
                return;
            }

            if (character < 0x20)
            {
                throw Invalid(context);
            }

            if (character != '\\')
            {
                continue;
            }

            if (index >= end)
            {
                throw Invalid(context);
            }

            var escape = json[index++];
            if (escape == 'u')
            {
                ReadUnicodeEscape(json, ref index, end, context);
            }
            else if (escape != '"'
                     && escape != '\\'
                     && escape != '/'
                     && escape != 'b'
                     && escape != 'f'
                     && escape != 'n'
                     && escape != 'r'
                     && escape != 't')
            {
                throw Invalid(context);
            }
        }

        throw Invalid(context);
    }

    private static void SkipLiteral(
        string json,
        ref int index,
        int end,
        string literal,
        string context)
    {
        if (end - index < literal.Length
            || string.CompareOrdinal(json, index, literal, 0, literal.Length) != 0)
        {
            throw Invalid(context);
        }

        index += literal.Length;
    }

    private static void SkipNumber(
        string json,
        ref int index,
        int end,
        string context)
    {
        var start = index;
        if (index < end && json[index] == '-')
        {
            index++;
        }

        if (index >= end)
        {
            throw Invalid(context);
        }

        if (json[index] == '0')
        {
            index++;
            if (index < end && IsAsciiDigit(json[index]))
            {
                throw Invalid(context);
            }
        }
        else
        {
            if (json[index] < '1' || json[index] > '9')
            {
                throw Invalid(context);
            }

            while (index < end && IsAsciiDigit(json[index]))
            {
                index++;
            }
        }

        if (index < end && json[index] == '.')
        {
            index++;
            var fractionStart = index;
            while (index < end && IsAsciiDigit(json[index]))
            {
                index++;
            }

            if (index == fractionStart)
            {
                throw Invalid(context);
            }
        }

        if (index < end && (json[index] == 'e' || json[index] == 'E'))
        {
            index++;
            if (index < end && (json[index] == '+' || json[index] == '-'))
            {
                index++;
            }

            var exponentStart = index;
            while (index < end && IsAsciiDigit(json[index]))
            {
                index++;
            }

            if (index == exponentStart)
            {
                throw Invalid(context);
            }
        }

        if (index == start)
        {
            throw Invalid(context);
        }
    }

    private static void SkipWhitespace(string json, ref int index, int end)
    {
        while (index < end)
        {
            var character = json[index];
            if (character != ' '
                && character != '\t'
                && character != '\r'
                && character != '\n')
            {
                return;
            }

            index++;
        }
    }

    private static bool IsAsciiDigit(char character)
    {
        return character >= '0' && character <= '9';
    }

    private static InvalidOperationException Invalid(string context)
    {
        return new InvalidOperationException(JsonInvalid + " context=" + context);
    }
}
