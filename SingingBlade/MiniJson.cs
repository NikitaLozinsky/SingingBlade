using System;
using System.Collections.Generic;
using System.Text;

namespace SingingBlade
{
    // Крошечный самодостаточный парсер JSON общего вида (object/array/string/bool/
    // number) — без внешних зависимостей, чтобы не заводить HintPath на
    // Newtonsoft.Json сверх разрешённого списка сборок (Assembly-CSharp /
    // -firstpass / UnityEngine / UnityEngine.CoreModule). Достаточно для формата
    // Localization.json: { "LocalizedStrings": [ { "Key": "...", ... }, ... ] }.
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            var pos = 0;
            var value = ParseValue(json, ref pos);
            SkipWhitespace(json, ref pos);
            return value;
        }

        private static object ParseValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            var c = Peek(json, pos);
            switch (c)
            {
                case '{': return ParseObject(json, ref pos);
                case '[': return ParseArray(json, ref pos);
                case '"': return ReadString(json, ref pos);
                case 't':
                    Expect(json, ref pos, "true");
                    return true;
                case 'f':
                    Expect(json, ref pos, "false");
                    return false;
                case 'n':
                    Expect(json, ref pos, "null");
                    return null;
                default:
                    return ReadNumber(json, ref pos);
            }
        }

        private static Dictionary<string, object> ParseObject(string json, ref int pos)
        {
            var map = new Dictionary<string, object>();
            Expect(json, ref pos, '{');
            SkipWhitespace(json, ref pos);
            if (Peek(json, pos) == '}')
            {
                pos++;
                return map;
            }

            while (true)
            {
                SkipWhitespace(json, ref pos);
                var key = ReadString(json, ref pos);
                SkipWhitespace(json, ref pos);
                Expect(json, ref pos, ':');
                map[key] = ParseValue(json, ref pos);
                SkipWhitespace(json, ref pos);

                var c = Peek(json, pos);
                if (c == ',')
                {
                    pos++;
                    continue;
                }
                if (c == '}')
                {
                    pos++;
                    break;
                }
                break;
            }

            return map;
        }

        private static List<object> ParseArray(string json, ref int pos)
        {
            var list = new List<object>();
            Expect(json, ref pos, '[');
            SkipWhitespace(json, ref pos);
            if (Peek(json, pos) == ']')
            {
                pos++;
                return list;
            }

            while (true)
            {
                list.Add(ParseValue(json, ref pos));
                SkipWhitespace(json, ref pos);

                var c = Peek(json, pos);
                if (c == ',')
                {
                    pos++;
                    continue;
                }
                if (c == ']')
                {
                    pos++;
                    break;
                }
                break;
            }

            return list;
        }

        private static string ReadString(string json, ref int pos)
        {
            Expect(json, ref pos, '"');
            var sb = new StringBuilder();
            while (true)
            {
                var c = json[pos++];
                if (c == '"') break;
                if (c == '\\')
                {
                    var esc = json[pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            var hex = json.Substring(pos, 4);
                            pos += 4;
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static double ReadNumber(string json, ref int pos)
        {
            var start = pos;
            while (pos < json.Length && "+-0123456789.eE".IndexOf(json[pos]) >= 0) pos++;
            return double.Parse(json.Substring(start, pos - start), System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
        }

        private static char Peek(string json, int pos)
        {
            return pos < json.Length ? json[pos] : '\0';
        }

        private static void Expect(string json, ref int pos, char expected)
        {
            if (Peek(json, pos) != expected)
            {
                throw new FormatException($"Localization.json: ожидался символ '{expected}' в позиции {pos}");
            }
            pos++;
        }

        private static void Expect(string json, ref int pos, string literal)
        {
            if (pos + literal.Length > json.Length || json.Substring(pos, literal.Length) != literal)
            {
                throw new FormatException($"Localization.json: ожидался литерал '{literal}' в позиции {pos}");
            }
            pos += literal.Length;
        }
    }
}
