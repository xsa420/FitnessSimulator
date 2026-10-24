using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace FitnessSimulator.Game.Configuration
{
    // Reflection is limited to explicit content DTO fields. It never reads or writes live game state.
    internal static class StrictContentJson
    {
        private sealed class Number { internal string Text; }
        internal static Func<object> BuildDecoder(object value, Type type)
        {
            if (value == null || type == typeof(string) || type == typeof(bool) || type.IsPrimitive || type.IsEnum) {
                var scalar = Decode(value, type);
                return () => scalar;
            }
            if (type.IsArray) {
                var element = type.GetElementType();
                var items = ((List<object>)value).Select(item => BuildDecoder(item, element)).ToArray();
                return () => {
                    var array = Array.CreateInstance(element, items.Length);
                    for (int i = 0; i < items.Length; i++) array.SetValue(items[i](), i);
                    return array;
                };
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
                var element = type.GetGenericArguments()[0];
                var items = ((List<object>)value).Select(item => BuildDecoder(item, element)).ToArray();
                return () => {
                    var list = (System.Collections.IList)Activator.CreateInstance(type);
                    foreach (var item in items) list.Add(item());
                    return list;
                };
            }
            var fields = ((Dictionary<string, object>)value).Select(pair => {
                var field = type.GetField(pair.Key, BindingFlags.Public | BindingFlags.Instance);
                return Tuple.Create(field, BuildDecoder(pair.Value, field.FieldType));
            }).ToArray();
            return () => {
                var result = Activator.CreateInstance(type);
                foreach (var field in fields) field.Item1.SetValue(result, field.Item2());
                return result;
            };
        }
        internal static object Decode(object value, Type type)
        {
            if (value == null) return null;
            if (type == typeof(string) || type == typeof(bool)) return value;
            if (type.IsEnum) return Enum.ToObject(type, Convert.ChangeType(((Number)value).Text, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture));
            if (type.IsPrimitive) return Convert.ChangeType(((Number)value).Text, type, CultureInfo.InvariantCulture);
            if (type.IsArray) {
                var list = (List<object>)value; var element = type.GetElementType(); var array = Array.CreateInstance(element, list.Count);
                for (int i = 0; i < list.Count; i++) array.SetValue(Decode(list[i], element), i);
                return array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
                var list = (System.Collections.IList)Activator.CreateInstance(type); var element = type.GetGenericArguments()[0];
                foreach (var item in (List<object>)value) list.Add(Decode(item, element)); return list;
            }
            var result = Activator.CreateInstance(type);
            foreach (var pair in (Dictionary<string, object>)value) {
                var field = type.GetField(pair.Key, BindingFlags.Public | BindingFlags.Instance);
                field.SetValue(result, Decode(pair.Value, field.FieldType));
            }
            return result;
        }
        internal static object Encode(object value, int depth = 0)
        {
            if (depth > 64) throw new ArgumentException("Content nesting exceeds limit");
            if (value == null || value is string || value is bool) return value;
            var type = value.GetType();
            if (type.IsEnum) return new Number { Text = Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) };
            if (type.IsPrimitive) return new Number { Text = Convert.ToString(value, CultureInfo.InvariantCulture) };
            if (value is System.Collections.IList list) { var items = new List<object>(); foreach (var item in list) items.Add(Encode(item, depth + 1)); return items; }
            if (!type.IsSerializable) throw new ArgumentException("Serializable content DTO required");
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsNotSerialized)) {
                var item = field.GetValue(value);
                if (item == null && Attribute.IsDefined(field, typeof(ContentOptionalAttribute))) continue;
                map.Add(field.Name, Encode(item, depth + 1));
            }
            return map;
        }
        internal static object Parse(string id, string text) => new Parser(id, text).ReadDocument();
        internal static void Validate(string id, object value, Type type, string path, int depth)
        {
            if (depth > 64) throw new ContentException(id, path, "DTO nesting exceeds limit.");
            if (value == null) throw new ContentException(id, path, "Explicit non-null value required.");
            if (type == typeof(string)) { Require(value is string, id, path, "string"); return; }
            if (type == typeof(bool)) { Require(value is bool, id, path, "boolean"); return; }
            if (type.IsEnum) {
                Require(value is Number, id, path, "integer enum");
                var token = ((Number)value).Text;
                try {
                    object integer = Convert.ChangeType(token, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture);
                    if (!Enum.IsDefined(type, Enum.ToObject(type, integer))) throw new ContentException(id, path, "Undefined enum value.");
                } catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException) { throw new ContentException(id, path, "Invalid enum integer."); }
                return;
            }
            if (type == typeof(decimal) || type == typeof(char)) throw new ContentException(id, path, "Use a supported JSON scalar DTO field; decimal/char are unsupported by this codec.");
            if (type.IsPrimitive) {
                Require(value is Number, id, path, "number"); string token = ((Number)value).Text;
                try {
                    if (type == typeof(float) || type == typeof(double)) {
                        double parsed = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (double.IsInfinity(parsed) || double.IsNaN(parsed) || type == typeof(float) && Math.Abs(parsed) > float.MaxValue) throw new OverflowException();
                    } else { if (token.Contains(".") || token.Contains("e") || token.Contains("E")) throw new FormatException(); Convert.ChangeType(token, type, CultureInfo.InvariantCulture); }
                } catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is InvalidCastException) { throw new ContentException(id, path, "Number does not fit " + type.Name + "."); }
                return;
            }
            if (type.IsArray || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) {
                Require(value is List<object>, id, path, "array"); var list = (List<object>)value;
                Type element = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                for (int i = 0; i < list.Count; i++) Validate(id, list[i], element, path + "[" + i + "]", depth + 1);
                return;
            }
            Require(value is Dictionary<string, object>, id, path, "object");
            if (!type.IsSerializable || type.IsGenericType || type.IsAbstract) throw new ContentException(id, path, "Expected serializable concrete public-field DTO.");
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsNotSerialized).ToDictionary(f => f.Name, StringComparer.Ordinal);
            var map = (Dictionary<string, object>)value;
            foreach (var pair in map) {
                if (!fields.TryGetValue(pair.Key, out var field)) throw new ContentException(id, path + "." + pair.Key, "Unknown field.");
                if (field.IsInitOnly) throw new ContentException(id, path + "." + pair.Key, "Readonly DTO fields are unsupported.");
                Validate(id, pair.Value, field.FieldType, path + "." + pair.Key, depth + 1);
            }
            foreach (var field in fields.Values)
                if (!map.ContainsKey(field.Name) && !Attribute.IsDefined(field, typeof(ContentOptionalAttribute))) throw new ContentException(id, path + "." + field.Name, "Required field is missing.");
        }
        private static void Require(bool test, string id, string path, string expected) { if (!test) throw new ContentException(id, path, "Expected " + expected + "."); }
        internal static string Canonical(object value)
        {
            if (value == null) return "null";
            if (value is string text) return Quote(text);
            if (value is bool flag) return flag ? "true" : "false";
            if (value is Number number) return number.Text;
            if (value is List<object> list) return "[" + string.Join(",", list.Select(Canonical)) + "]";
            return "{" + string.Join(",", ((Dictionary<string, object>)value).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => Quote(p.Key) + ":" + Canonical(p.Value))) + "}";
        }
        private static string Quote(string text)
        {
            var output = new StringBuilder("\"");
            foreach (char c in text) {
                if (c == '"' || c == '\\') output.Append('\\').Append(c);
                else if (c < 32 || char.IsSurrogate(c)) output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else output.Append(c);
            }
            return output.Append('"').ToString();
        }
        private sealed class Parser
        {
            private readonly string id, text;
            private int position, nodes;
            internal Parser(string id, string text)
            {
                this.id = id; this.text = text;
                if (text == null || text.Length > 8 * 1024 * 1024) throw new ContentException(id, "$", "Missing document or document exceeds 8 MiB characters.");
            }
            private ContentException Error(string path, string reason) => new ContentException(id, path, reason + " (character " + position + ")");
            internal object ReadDocument() { object result = Value("$", 0); White(); if (position != text.Length) throw Error("$", "Trailing JSON input"); return result; }
            private void White() { while (position < text.Length && (text[position] == ' ' || text[position] == '\t' || text[position] == '\r' || text[position] == '\n')) position++; }
            private bool Take(char c) { White(); if (position < text.Length && text[position] == c) { position++; return true; } return false; }
            private void Need(char c, string path) { if (!Take(c)) throw Error(path, "Expected '" + c + "'"); }
            private object Value(string path, int depth)
            {
                if (depth > 64 || ++nodes > 200000) throw Error(path, "JSON complexity limit exceeded");
                White(); if (position >= text.Length) throw Error(path, "Unexpected end of JSON");
                char c = text[position];
                if (c == '"') return String(path);
                if (c == '{') {
                    position++; var map = new Dictionary<string, object>(StringComparer.Ordinal); if (Take('}')) return map;
                    do { White(); string key = String(path); Need(':', path);
                        if (map.ContainsKey(key)) throw Error(path + "." + key, "Duplicate field");
                        map.Add(key, Value(path + "." + key, depth + 1)); if (Take('}')) return map;
                    } while (Take(',')); throw Error(path, "Expected ',' or '}'");
                }
                if (c == '[') {
                    position++; var list = new List<object>(); if (Take(']')) return list;
                    do { list.Add(Value(path + "[" + list.Count + "]", depth + 1)); if (Take(']')) return list; } while (Take(','));
                    throw Error(path, "Expected ',' or ']'");
                }
                if (Literal("true")) return true; if (Literal("false")) return false; if (Literal("null")) return null;
                int start = position;
                if (position < text.Length && text[position] == '-') position++;
                if (position < text.Length && text[position] == '0') position++;
                else { int digits = position; while (Digit()) position++; if (digits == position) throw Error(path, "Invalid JSON value"); }
                if (position < text.Length && text[position] == '.') { position++; int digits = position; while (Digit()) position++; if (digits == position) throw Error(path, "Missing fraction digits"); }
                if (position < text.Length && (text[position] == 'e' || text[position] == 'E')) {
                    position++; if (position < text.Length && (text[position] == '+' || text[position] == '-')) position++;
                    int digits = position; while (Digit()) position++; if (digits == position) throw Error(path, "Missing exponent digits");
                }
                return new Number { Text = text.Substring(start, position - start) };
            }
            private bool Digit() => position < text.Length && text[position] >= '0' && text[position] <= '9';
            private bool Literal(string value)
            {
                if (position + value.Length > text.Length || string.CompareOrdinal(text, position, value, 0, value.Length) != 0) return false;
                position += value.Length; return true;
            }
            private string String(string path)
            {
                if (position >= text.Length || text[position++] != '"') throw Error(path, "Expected string");
                var result = new StringBuilder();
                while (position < text.Length) {
                    char c = text[position++]; if (c == '"') {
                        string value = result.ToString();
                        for (int i = 0; i < value.Length; i++) {
                            if (char.IsHighSurrogate(value[i])) { if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw Error(path, "Unpaired Unicode surrogate"); }
                            else if (char.IsLowSurrogate(value[i])) throw Error(path, "Unpaired Unicode surrogate");
                        }
                        return value;
                    }
                    if (c < 32) throw Error(path, "Unescaped control character");
                    if (c != '\\') { result.Append(c); continue; }
                    if (position >= text.Length) throw Error(path, "Incomplete escape");
                    c = text[position++];
                    switch (c) {
                        case '"': case '\\': case '/': result.Append(c); break;
                        case 'b': result.Append('\b'); break; case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break; case 'r': result.Append('\r'); break; case 't': result.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length || !ushort.TryParse(text.Substring(position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex)) throw Error(path, "Invalid Unicode escape");
                            result.Append((char)hex); position += 4; break;
                        default: throw Error(path, "Invalid string escape");
                    }
                }
                throw Error(path, "Unterminated string");
            }
        }
    }
}
