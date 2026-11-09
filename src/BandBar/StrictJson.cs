namespace BandBar
{
    /// <summary>
    /// Strict JSON syntax check (RFC 8259). The .NET Framework's JSON reader accepts some broken input, such as a file
    /// cut off half-way through a save; settings.json must be one complete JSON object before it is used or changed
    /// (docs/DESIGN.md §8.1).
    /// </summary>
    internal static class StrictJson
    {
        private const int MaxDepth = 64;

        /// <summary>True when <paramref name="text"/> is exactly one JSON object, with only whitespace around it.</summary>
        public static bool IsObject(string text)
        {
            if (text == null)
                return false;
            int i = 0;
            SkipWhitespace(text, ref i);
            if (i >= text.Length || text[i] != '{' || !Value(text, ref i, 0))
                return false;
            SkipWhitespace(text, ref i);
            return i == text.Length;
        }

        /// <summary>True when the text is empty or whitespace only.</summary>
        public static bool IsBlank(string text)
        {
            int i = 0;
            SkipWhitespace(text ?? "", ref i);
            return i == (text ?? "").Length;
        }

        private static bool Value(string s, ref int i, int depth)
        {
            if (depth > MaxDepth || i >= s.Length)
                return false;
            switch (s[i])
            {
                case '{': return Container(s, ref i, depth + 1, '}', members: true);
                case '[': return Container(s, ref i, depth + 1, ']', members: false);
                case '"': return String(s, ref i);
                case 't': return Literal(s, ref i, "true");
                case 'f': return Literal(s, ref i, "false");
                case 'n': return Literal(s, ref i, "null");
                default: return Number(s, ref i);
            }
        }

        // An object ("name": value, …) or an array (value, …); no trailing comma.
        private static bool Container(string s, ref int i, int depth, char close, bool members)
        {
            i++; // the opening bracket
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == close)
            {
                i++;
                return true;
            }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (members)
                {
                    if (i >= s.Length || s[i] != '"' || !String(s, ref i))
                        return false;
                    SkipWhitespace(s, ref i);
                    if (i >= s.Length || s[i] != ':')
                        return false;
                    i++;
                    SkipWhitespace(s, ref i);
                }
                if (!Value(s, ref i, depth))
                    return false;
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    return false;
                if (s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (s[i] != close)
                    return false;
                i++;
                return true;
            }
        }

        private static bool String(string s, ref int i)
        {
            i++; // the opening quote
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '"')
                {
                    i++;
                    return true;
                }
                if (c < 0x20)
                    return false;
                if (c != '\\')
                {
                    i++;
                    continue;
                }

                i++;
                if (i >= s.Length)
                    return false;
                switch (s[i])
                {
                    case '"': case '\\': case '/': case 'b': case 'f': case 'n': case 'r': case 't':
                        i++;
                        break;
                    case 'u':
                        if (i + 4 >= s.Length)
                            return false;
                        for (int k = 1; k <= 4; k++)
                        {
                            if (!IsHex(s[i + k]))
                                return false;
                        }
                        i += 5;
                        break;
                    default:
                        return false;
                }
            }
            return false; // no closing quote
        }

        // -? (0 | [1-9][0-9]*) (\.[0-9]+)? ([eE][+-]?[0-9]+)?
        private static bool Number(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && s[i] == '-')
                i++;
            if (i >= s.Length)
                return false;
            if (s[i] == '0')
                i++;
            else if (!Digits(s, ref i))
                return false;

            if (i < s.Length && s[i] == '.')
            {
                i++;
                if (!Digits(s, ref i))
                    return false;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-'))
                    i++;
                if (!Digits(s, ref i))
                    return false;
            }
            return i > start;
        }

        private static bool Digits(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
                i++;
            return i > start;
        }

        private static bool Literal(string s, ref int i, string literal)
        {
            if (string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                return false;
            i += literal.Length;
            return true;
        }

        private static bool IsHex(char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
                i++;
        }
    }
}
