using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

#pragma warning disable CS0618 // Instance id and sort mode calls exist in every supported Unity version.

namespace FirstSymbol.Lettera.Editor.Cli
{
    /// <summary>A relaxed JSON reader: unquoted keys and words, single quotes. Objects become dictionaries, arrays lists, numbers doubles.</summary>
    internal static class LetteraCliJson
    {
        // Nesting past this is someone's attack on the stack, and an overflow cannot be caught: it would take the editor with it.
        const int MaxDepth = 64;

        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("Empty JSON.");
            int i = 0;
            object value = Value(json, ref i, 0);
            Skip(json, ref i);
            if (i < json.Length) throw new FormatException($"Unexpected '{json[i]}' at {i} in JSON.");
            return value;
        }

        public static Dictionary<string, object> Object(string json) => Parse(json) as Dictionary<string, object> ?? throw new ArgumentException("Expected a JSON object: {...}");

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static char Peek(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON ended too soon.");
            return s[i];
        }

        static object Value(string s, ref int i, int depth)
        {
            if (depth > MaxDepth) throw new FormatException($"JSON is nested more than {MaxDepth} deep.");
            char c = Peek(s, ref i);
            if (c == '{') return ReadObject(s, ref i, depth);
            if (c == '[') return ReadArray(s, ref i, depth);
            if (c == '"' || c == '\'') return ReadString(s, ref i);
            return ReadWord(s, ref i);
        }

        static Dictionary<string, object> ReadObject(string s, ref int i, int depth)
        {
            var d = new Dictionary<string, object>();
            i++;
            if (Peek(s, ref i) == '}')
            {
                i++;
                return d;
            }
            while (true)
            {
                char c = Peek(s, ref i);
                string key = c == '"' || c == '\'' ? ReadString(s, ref i) : Word(s, ref i);
                if (key.Length == 0) throw new FormatException($"Expected a key at {i}.");
                if (Peek(s, ref i) != ':') throw new FormatException($"Expected ':' at {i}.");
                i++;
                d[key] = Value(s, ref i, depth + 1);
                char next = Peek(s, ref i);
                i++;
                if (next == ',') continue;
                if (next == '}') return d;
                throw new FormatException($"Expected ',' or '}}' at {i - 1}.");
            }
        }

        static List<object> ReadArray(string s, ref int i, int depth)
        {
            var l = new List<object>();
            i++;
            if (Peek(s, ref i) == ']')
            {
                i++;
                return l;
            }
            while (true)
            {
                l.Add(Value(s, ref i, depth + 1));
                char next = Peek(s, ref i);
                i++;
                if (next == ',') continue;
                if (next == ']') return l;
                throw new FormatException($"Expected ',' or ']' at {i - 1}.");
            }
        }

        static string ReadString(string s, ref int i)
        {
            char quote = s[i++];
            var text = new StringBuilder();
            while (i < s.Length && s[i] != quote)
            {
                char c = s[i++];
                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': text.Append('\n'); break;
                    case 't': text.Append('\t'); break;
                    case 'r': text.Append('\r'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'u':
                        if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw new FormatException($"Bad \\u escape at {i}.");
                        text.Append((char)code);
                        i += 4;
                        break;
                    default: text.Append(e); break;
                }
            }
            if (i >= s.Length) throw new FormatException("A string in the JSON never ends.");
            i++;
            return text.ToString();
        }

        static string Word(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && ",}]:{[ \t\r\n".IndexOf(s[i]) < 0) i++;
            return s.Substring(start, i - start);
        }

        static object ReadWord(string s, ref int i)
        {
            string word = Word(s, ref i);
            if (word.Length == 0) throw new FormatException($"Unexpected '{s[i]}' at {i} in JSON.");
            if (word == "true") return true;
            if (word == "false") return false;
            if (word == "null") return null;
            if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && !double.IsNaN(number) && !double.IsInfinity(number)) return number;
            return word;
        }

        /// <summary>A tree of dictionaries, lists and plain values, as JSON text.</summary>
        public static string Write(object value)
        {
            var text = new StringBuilder();
            Write(value, text);
            return text.ToString();
        }

        static void Write(object value, StringBuilder text)
        {
            switch (value)
            {
                case null: text.Append("null"); break;
                case string s: Quote(s, text); break;
                case bool b: text.Append(b ? "true" : "false"); break;
                case IDictionary d:
                    text.Append('{');
                    bool firstKey = true;
                    foreach (DictionaryEntry entry in d)
                    {
                        if (!firstKey) text.Append(',');
                        firstKey = false;
                        Quote(entry.Key.ToString(), text);
                        text.Append(':');
                        Write(entry.Value, text);
                    }
                    text.Append('}');
                    break;
                case IList l:
                    text.Append('[');
                    for (int i = 0; i < l.Count; i++)
                    {
                        if (i > 0) text.Append(',');
                        Write(l[i], text);
                    }
                    text.Append(']');
                    break;
                case IFormattable number: text.Append(number.ToString(null, CultureInfo.InvariantCulture)); break;
                default: throw new InvalidOperationException("Not plain data: " + value.GetType().Name);
            }
        }

        static void Quote(string s, StringBuilder text)
        {
            text.Append('"');
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') text.Append('\\').Append(c);
                else if (c == '\n') text.Append("\\n");
                else if (c == '\r') text.Append("\\r");
                else if (c == '\t') text.Append("\\t");
                else if (c < 32) text.Append("\\u").Append(((int)c).ToString("x4"));
                else text.Append(c);
            }
            text.Append('"');
        }
    }

    /// <summary>Lettera's objects as plain data, and patches applied back to them by reflection. Whatever is wrong with a value throws: the commands turn that into an answer.</summary>
    internal static class LetteraCliData
    {
        static readonly Type[] s_Bases = { typeof(LetteraEffect), typeof(LetteraMatcher), typeof(LetteraFieldRule), typeof(LetteraValueCheck), typeof(LetteraFitBudget) };
        static readonly string[] s_Suffixes = { "CharFilter", "Effect", "Matcher", "Rule", "Decorator", "Check", "Budget" };
        static readonly Dictionary<Type, Dictionary<string, Type>> s_Index = new Dictionary<Type, Dictionary<string, Type>>();

        /// <summary>When true, class objects list only what differs from a fresh instance.</summary>
        [ThreadStatic] public static bool Compact;

        static bool IsPolymorphic(Type type) => s_Bases.Any(b => b.IsAssignableFrom(type));

        public static Dictionary<string, Type> TypesOf(Type baseType)
        {
            if (s_Index.TryGetValue(baseType, out Dictionary<string, Type> index)) return index;
            index = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
            foreach (Type type in TypeCache.GetTypesDerivedFrom(baseType))
            {
                if (type.IsAbstract || type.IsGenericType || type.IsNestedPrivate || type.GetConstructor(Type.EmptyTypes) == null) continue;
                index[type.Name] = type;
                index[ShortName(type)] = type;
            }
            s_Index[baseType] = index;
            return index;
        }

        public static string ShortName(Type type)
        {
            foreach (string suffix in s_Suffixes)
                if (type.Name.EndsWith(suffix, StringComparison.Ordinal) && type.Name.Length > suffix.Length)
                    return type.Name.Substring(0, type.Name.Length - suffix.Length);
            return type.Name;
        }

        public static Type ResolveType(string name, Type baseType)
        {
            if (!string.IsNullOrWhiteSpace(name) && TypesOf(baseType).TryGetValue(name.Trim(), out Type type)) return type;
            throw new ArgumentException($"Unknown {baseType.Name} '{name}'. Run 'unity command lettera types' for the list.");
        }

        public static IEnumerable<FieldInfo> Fields(Type type)
        {
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(object) && t != typeof(ScriptableObject) && t != typeof(MonoBehaviour); t = t.BaseType)
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.IsInitOnly && !f.IsNotSerialized && (f.IsPublic || f.GetCustomAttribute<SerializeField>() != null || f.GetCustomAttribute<SerializeReference>() != null))
                        list.Add(f);
            return list;
        }

        public static string Key(string fieldName)
        {
            string name = fieldName.StartsWith("m_", StringComparison.Ordinal) ? fieldName.Substring(2) : fieldName;
            return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        static object Round(float f) => float.IsNaN(f) || float.IsInfinity(f) ? null : (object)Math.Round(f, 4);

        public static object ToData(object o, int depth = 0)
        {
            if (o == null) return null;
            if (depth > 12) return "...";
            switch (o)
            {
                case string s: return s;
                case bool b: return b;
                case float f: return Round(f);
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? null : (object)d;
                case int i: return i;
                case uint u: return (long)u;
                case long l: return l;
                case short h: return (int)h;
                case byte y: return (int)y;
                case Enum e: return e.ToString();
                case Color c: return c.maxColorComponent > 1f ? (object)new List<object> { Round(c.r), Round(c.g), Round(c.b), Round(c.a) } : "#" + ColorUtility.ToHtmlStringRGBA(c);
                case Color32 c: return "#" + ColorUtility.ToHtmlStringRGBA(c);
                case Vector2 v: return new List<object> { Round(v.x), Round(v.y) };
                case Vector3 v: return new List<object> { Round(v.x), Round(v.y), Round(v.z) };
                case Vector4 v: return new List<object> { Round(v.x), Round(v.y), Round(v.z), Round(v.w) };
                case Rect r: return new List<object> { Round(r.x), Round(r.y), Round(r.width), Round(r.height) };
                case AnimationCurve curve: return curve.keys.Select(k => (object)new List<object> { Round(k.time), Round(k.value) }).ToList();
                case Object unity:
                    if (unity == null) return null;
                    string path = AssetDatabase.GetAssetPath(unity);
                    return string.IsNullOrEmpty(path) ? unity.name : path;
                case IList list:
                {
                    var l = new List<object>();
                    foreach (object item in list) l.Add(ToData(item, depth + 1));
                    return l;
                }
            }

            return Members(o, depth);
        }

        /// <summary>An object's serialized fields as plain data, a Unity object's too, which elsewhere stands for its path.</summary>
        public static Dictionary<string, object> Members(object o, int depth = 0)
        {
            Type t = o.GetType();
            var dict = new Dictionary<string, object>();
            if (IsPolymorphic(t)) dict["$type"] = ShortName(t);
            object defaults = null;
            if (Compact && (t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null))
            {
                try
                {
                    defaults = Activator.CreateInstance(t);
                }
                catch (Exception)
                {
                    defaults = null;
                }
            }
            foreach (FieldInfo f in Fields(t))
            {
                object value = ToData(f.GetValue(o), depth + 1);
                if (defaults != null)
                {
                    Compact = false;
                    object fresh = ToData(f.GetValue(defaults), depth + 1);
                    Compact = true;
                    if (DataEquals(value, fresh)) continue;
                }
                dict[Key(f.Name)] = value;
            }
            return dict;
        }

        static bool DataEquals(object a, object b)
        {
            if (a is string sa && sa.Length == 0) a = null;
            if (b is string sb && sb.Length == 0) b = null;
            if (a == null || b == null) return a == null && b == null;
            if (a is Dictionary<string, object> da && b is Dictionary<string, object> db)
                return da.Count <= db.Count && da.All(kv => db.TryGetValue(kv.Key, out object v) && DataEquals(kv.Value, v));
            if (a is List<object> la && b is List<object> lb) return la.Count == lb.Count && la.Zip(lb, DataEquals).All(x => x);
            return a.Equals(b);
        }

        /// <summary>Sets the fields named in the data. An unknown key throws, naming the keys there are.</summary>
        public static void Apply(object target, Dictionary<string, object> data)
        {
            Type t = target.GetType();
            List<FieldInfo> fields = Fields(t).ToList();
            foreach (KeyValuePair<string, object> kv in data)
            {
                if (kv.Key == "$type") continue;
                FieldInfo f = fields.FirstOrDefault(x => string.Equals(x.Name, kv.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(Key(x.Name), kv.Key, StringComparison.OrdinalIgnoreCase));
                if (f == null) throw new ArgumentException($"{ShortName(t)} has no field '{kv.Key}'. Fields: {string.Join(", ", fields.Select(x => Key(x.Name)))}");
                object value;
                if (kv.Value == null && f.FieldType.IsValueType)
                {
                    // Paint settings use NaN for "inherit", which JSON spells null. Nothing else takes null for a number.
                    if (!(target is LetteraPaintSettings)) throw new ArgumentException($"{Key(f.Name)} cannot be null.");
                    value = f.FieldType == typeof(float) ? float.NaN : f.FieldType == typeof(Vector2) ? new Vector2(float.NaN, float.NaN) : Activator.CreateInstance(f.FieldType);
                }
                else value = Limit(f, Convert(kv.Value, f.FieldType, f.GetValue(target)));
                f.SetValue(target, value);
            }
        }

        static object Limit(FieldInfo f, object value)
        {
            var range = f.GetCustomAttribute<RangeAttribute>();
            var least = f.GetCustomAttribute<MinAttribute>();
            if (value is float x)
            {
                if (range != null) return Mathf.Clamp(x, range.min, range.max);
                if (least != null) return Mathf.Max(x, least.min);
            }
            if (value is int n)
            {
                if (range != null) return Mathf.Clamp(n, (int)range.min, (int)range.max);
                if (least != null) return Mathf.Max(n, (int)least.min);
            }
            return value;
        }

        public static double ToDouble(object v)
        {
            double d;
            if (v is double x) d = x;
            else if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) d = parsed;
            else throw new ArgumentException($"Expected a number, got {Show(v)}.");
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("A number must be finite.");
            return d;
        }

        static string Show(object v)
        {
            string text = v is string s ? "'" + s + "'" : v is IDictionary ? "an object" : v is IList ? "an array" : v?.ToString() ?? "null";
            return text.Length > 80 ? text.Substring(0, 80) + "..." : text;
        }

        public static object Convert(object v, Type type, object existing = null)
        {
            if (v == null) return type.IsValueType ? throw new ArgumentException($"{type.Name} cannot be null.") : (object)null;
            if (type == typeof(string))
            {
                if (v is IDictionary || v is IList) throw new ArgumentException($"Expected text, got {Show(v)}.");
                return v is double n ? n.ToString(CultureInfo.InvariantCulture) : v.ToString();
            }
            if (type == typeof(float))
            {
                double d = ToDouble(v);
                if (Math.Abs(d) > float.MaxValue) throw new ArgumentException($"{d} is out of range.");
                return (float)d;
            }
            if (type == typeof(double)) return ToDouble(v);
            if (type == typeof(int) || type == typeof(long) || type == typeof(uint) || type == typeof(byte) || type == typeof(short))
            {
                double d = Math.Round(ToDouble(v));
                if (type == typeof(int) && (d < int.MinValue || d > int.MaxValue) || type == typeof(uint) && (d < 0 || d > uint.MaxValue) || type == typeof(byte) && (d < 0 || d > 255) || type == typeof(short) && (d < short.MinValue || d > short.MaxValue) || Math.Abs(d) > 9e15)
                    throw new ArgumentException($"{d} is out of range for {type.Name}.");
                return System.Convert.ChangeType(d, type, CultureInfo.InvariantCulture);
            }
            if (type == typeof(bool))
            {
                if (v is bool b) return b;
                if (v is string s && bool.TryParse(s, out bool parsed)) return parsed;
                throw new ArgumentException($"Expected true or false, got {Show(v)}.");
            }
            if (type.IsEnum) return ToEnum(v, type);
            if (type == typeof(Color)) return ToColor(v);
            if (type == typeof(Color32)) return (Color32)ToColor(v);
            if (type == typeof(Vector2)) { float[] a = Floats(v, 2); return new Vector2(a[0], a[1]); }
            if (type == typeof(Vector3)) { float[] a = Floats(v, 3); return new Vector3(a[0], a[1], a[2]); }
            if (type == typeof(Vector4)) { float[] a = Floats(v, 4); return new Vector4(a[0], a[1], a[2], a[3]); }
            if (type == typeof(Rect)) { float[] a = Floats(v, 4); return new Rect(a[0], a[1], a[2], a[3]); }
            if (type == typeof(AnimationCurve))
            {
                if (!(v is List<object> points)) throw new ArgumentException("A curve is a list of [time, value].");
                return new AnimationCurve(points.Select(k => Floats(k, 2)).Select(k => new Keyframe(k[0], k[1])).ToArray());
            }
            if (typeof(Object).IsAssignableFrom(type)) return ToAsset(v, type);
            if (type == typeof(LetteraGradient)) return ToGradient(v, existing);
            if (type == typeof(LetteraPaint)) return ToPaint(v, existing as LetteraPaint);
            if (type == typeof(LetteraPaintChoice)) return ToChoice(v, existing as LetteraPaintChoice);
            if (type == typeof(LetteraStyle)) return ToStyle(v, existing as LetteraStyle);

            if (typeof(IList).IsAssignableFrom(type))
            {
                Type element = type.IsArray ? type.GetElementType() : type.IsGenericType ? type.GetGenericArguments()[0] : throw new ArgumentException($"Cannot fill a {type.Name}.");
                var old = existing as IList;
                if (v is List<object> list)
                {
                    var made = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element));
                    for (int i = 0; i < list.Count; i++) made.Add(Convert(list[i], element, old != null && i < old.Count ? old[i] : null));
                    if (!type.IsArray) return made;
                    Array array = Array.CreateInstance(element, made.Count);
                    made.CopyTo(array, 0);
                    return array;
                }
                if (v is Dictionary<string, object> patch && old != null)
                {
                    foreach (KeyValuePair<string, object> kv in patch)
                    {
                        if (!int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= old.Count)
                            throw new ArgumentException($"Index '{kv.Key}' is out of range (count {old.Count}).");
                        old[index] = Convert(kv.Value, element, old[index]);
                    }
                    return old;
                }
                throw new ArgumentException($"Expected a list, got {Show(v)}.");
            }

            if (v is string typeName && IsPolymorphic(type)) return Activator.CreateInstance(ResolveType(typeName, type));
            if (v is Dictionary<string, object> dict)
            {
                object target = existing;
                if (dict.TryGetValue("$type", out object named))
                {
                    if (!(named is string concreteName)) throw new ArgumentException("$type must be a name.");
                    Type concrete = ResolveType(concreteName, type);
                    if (target == null || target.GetType() != concrete) target = Activator.CreateInstance(concrete);
                }
                else if (target == null)
                {
                    if (type.IsAbstract) throw new ArgumentException($"{type.Name} needs a \"$type\".");
                    target = Activator.CreateInstance(type);
                }
                Apply(target, dict);
                return target;
            }
            throw new ArgumentException($"Cannot make a {type.Name} from {Show(v)}.");
        }

        static object ToEnum(object v, Type type)
        {
            object value;
            if (v is string s)
            {
                string name = s.Replace(" ", "");
                if (!Enum.TryParse(type, name, true, out value)) throw new ArgumentException($"'{s}' is not one of {string.Join(", ", Enum.GetNames(type))}.");
            }
            else value = Enum.ToObject(type, (int)(double)ToBoundedInt(v));
            if (!type.IsDefined(typeof(FlagsAttribute), false) && !Enum.IsDefined(type, value)) throw new ArgumentException($"{Show(v)} is not one of {string.Join(", ", Enum.GetNames(type))}.");
            return value;
        }

        static object ToBoundedInt(object v)
        {
            double d = ToDouble(v);
            if (d < int.MinValue || d > int.MaxValue) throw new ArgumentException($"{d} is out of range.");
            return Math.Round(d);
        }

        static object ToAsset(object v, Type type)
        {
            if (!(v is string path)) throw new ArgumentException($"An asset is given by its path, got {Show(v)}.");
            Object asset = AssetDatabase.LoadAssetAtPath(path, type);
            if (asset == null) asset = AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault(type.IsInstanceOfType);
            if (asset == null && type == typeof(GameObject)) asset = LetteraCliScene.Find(path);
            if (asset == null && typeof(Component).IsAssignableFrom(type)) asset = LetteraCliScene.Find(path)?.GetComponent(type);
            if (asset == null) throw new ArgumentException($"No {type.Name} at '{path}'.");
            return asset;
        }

        static object ToGradient(object v, object existing)
        {
            if (v is Dictionary<string, object> dict)
            {
                var gradient = existing as LetteraGradient ?? new LetteraGradient();
                Apply(gradient, dict);
                return gradient;
            }
            List<object> colors = v is string s ? s.Split(',').Select(x => (object)x.Trim()).ToList() : v as List<object>;
            if (colors == null || colors.Count == 0) throw new ArgumentException("A gradient is \"#f00,#00f\", a list of colours, or {stops:[...]}.");
            var made = new LetteraGradient();
            for (int i = 0; i < colors.Count; i++) made.Stops.Add(new LetteraGradientStop(colors.Count == 1 ? 0 : i / (colors.Count - 1f), (Color32)ToColor(colors[i])));
            return made;
        }

        static readonly string[] s_SettingKeys = { "span", "shape", "fit", "edge", "blend", "angle", "scale", "offset" };

        // A paint can be written short: "#f00", "#f00,#00f", or {gradient:"#f00,#00f",angle:90}.
        static LetteraPaint ToPaint(object v, LetteraPaint existing)
        {
            var paint = existing ?? new LetteraPaint();
            if (v is string s && s.Contains(",") && (s.Contains("#") || !char.IsDigit(s.TrimStart()[0])) || v is List<object> l && l.Count > 0 && l[0] is string)
            {
                paint.Source = LetteraPaintSourceKind.Gradient;
                paint.Gradient = (LetteraGradient)ToGradient(v, null);
                return paint;
            }
            if (!(v is Dictionary<string, object> dict))
            {
                paint.Source = LetteraPaintSourceKind.Solid;
                paint.Color = (Color32)ToColor(v);
                return paint;
            }
            var rest = new Dictionary<string, object>();
            var settings = new Dictionary<string, object>();
            foreach (KeyValuePair<string, object> kv in dict)
                if (s_SettingKeys.Contains(kv.Key, StringComparer.OrdinalIgnoreCase)) settings[kv.Key] = kv.Value;
                else rest[kv.Key] = kv.Value;
            bool Has(string key) => rest.Keys.Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (!Has("source")) paint.Source = Has("gradient") ? LetteraPaintSourceKind.Gradient : Has("texture") ? LetteraPaintSourceKind.Texture : Has("color") ? LetteraPaintSourceKind.Solid : paint.Source;
            Apply(paint, rest);
            if (settings.Count > 0) Apply(paint.Settings ?? (paint.Settings = new LetteraPaintSettings()), settings);
            return paint;
        }

        static LetteraPaintChoice ToChoice(object v, LetteraPaintChoice existing)
        {
            var choice = existing ?? new LetteraPaintChoice();
            if (v is Dictionary<string, object> dict && dict.Keys.Any(k => k.Equals("kind", StringComparison.OrdinalIgnoreCase) || k.Equals("inline", StringComparison.OrdinalIgnoreCase) || k.Equals("swatchName", StringComparison.OrdinalIgnoreCase)))
            {
                if (dict.Keys.Any(k => k.Equals("inline", StringComparison.OrdinalIgnoreCase)) && !dict.Keys.Any(k => k.Equals("kind", StringComparison.OrdinalIgnoreCase))) choice.Kind = LetteraPaintChoiceKind.Inline;
                Apply(choice, dict);
                return choice;
            }
            if (v is string s && !s.Contains(",") && !s.StartsWith("#", StringComparison.Ordinal) && !ColorUtility.TryParseHtmlString(s, out _))
            {
                choice.Kind = LetteraPaintChoiceKind.Named;
                choice.SwatchName = s;
                return choice;
            }
            choice.Kind = LetteraPaintChoiceKind.Inline;
            choice.Inline = ToPaint(v, choice.Inline);
            return choice;
        }

        /// <summary>A style from {tag, matcher, effect, enabled, parameter}. No tag and no matcher means the whole text.</summary>
        internal static LetteraStyle ToStyle(object v, LetteraStyle existing)
        {
            if (!(v is Dictionary<string, object> dict)) throw new ArgumentException($"A style is an object: {{effect:..., tag:...}}, got {Show(v)}.");
            LetteraStyle style = existing != null ? existing.Clone() : new LetteraStyle();
            foreach (KeyValuePair<string, object> kv in dict)
            {
                switch (kv.Key.ToLowerInvariant())
                {
                    case "tag":
                        var tag = (string)Convert(kv.Value, typeof(string));
                        if (string.IsNullOrWhiteSpace(tag)) throw new ArgumentException("A tag needs a name.");
                        style.Matcher = new TagMatcher(tag);
                        break;
                    case "matcher": style.Matcher = (LetteraMatcher)Convert(kv.Value, typeof(LetteraMatcher), style.Matcher); break;
                    case "effect": style.Effect = (LetteraEffect)Convert(kv.Value, typeof(LetteraEffect), style.Effect); break;
                    case "enabled": style.Enabled = (bool)Convert(kv.Value, typeof(bool)); break;
                    case "parameter":
                    case "defaultparameter": style.DefaultParameter = (string)Convert(kv.Value, typeof(string)) ?? ""; break;
                    case "$type": break;
                    default: throw new ArgumentException($"A style has no '{kv.Key}'. It has tag, matcher, effect, enabled, parameter.");
                }
            }
            if (style.Matcher == null) style.Matcher = new WholeTextMatcher();
            return style;
        }

        public static float[] Floats(object v, int n)
        {
            if (v is string s) v = s.Split(',').Select(x => (object)x.Trim()).ToList();
            var r = new float[n];
            if (v is List<object> l)
            {
                if (l.Count < n) throw new ArgumentException($"Expected {n} numbers, got {l.Count}.");
                for (int i = 0; i < n; i++) r[i] = (float)Convert(l[i], typeof(float));
                return r;
            }
            if (v is Dictionary<string, object> d)
            {
                string[] names = n == 4 && d.ContainsKey("width") ? new[] { "x", "y", "width", "height" } : new[] { "x", "y", "z", "w" };
                for (int i = 0; i < n; i++)
                    if (d.TryGetValue(names[i], out object x))
                        r[i] = (float)Convert(x, typeof(float));
                return r;
            }
            throw new ArgumentException($"Expected {n} numbers, got {Show(v)}.");
        }

        public static Color ToColor(object v)
        {
            if (v is string s)
            {
                if (!s.StartsWith("#", StringComparison.Ordinal) && s.Contains(",")) return ToColor(s.Split(',').Select(x => (object)x.Trim()).ToList());
                if (ColorUtility.TryParseHtmlString(s, out Color c)) return c;
                throw new ArgumentException($"Bad colour '{s}': use #RRGGBB, #RRGGBBAA, a colour name or [r,g,b,a].");
            }
            if (v is Dictionary<string, object> d)
            {
                float F(string k) => d.TryGetValue(k, out object x) ? (float)Convert(x, typeof(float)) : k == "a" ? 1 : throw new ArgumentException($"A colour needs '{k}'.");
                return new Color(F("r"), F("g"), F("b"), F("a"));
            }
            if (v is List<object> l && (l.Count == 3 || l.Count == 4))
            {
                float[] a = Floats(l, l.Count);
                return new Color(a[0], a[1], a[2], l.Count == 4 ? a[3] : 1);
            }
            throw new ArgumentException($"Bad colour {Show(v)}: use #RRGGBB, #RRGGBBAA, a colour name or [r,g,b,a].");
        }
    }

    /// <summary>Finds scene objects by hierarchy path, name or instance id ("#1234").</summary>
    internal static class LetteraCliScene
    {
        public static GameObject Find(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return null;
            if (target[0] == '#')
            {
                if (!int.TryParse(target.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return null;
                Object found = EditorUtility.InstanceIDToObject(id);
                GameObject go = found as GameObject ?? (found as Component)?.gameObject;
                return go != null && go.scene.IsValid() ? go : null;
            }
            string trimmed = target.TrimStart('/');
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.hideFlags == HideFlags.None && (Path(t.gameObject).TrimStart('/') == trimmed))
                    return t.gameObject;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.hideFlags == HideFlags.None && t.name == target)
                    return t.gameObject;
            return null;
        }

        public static string Path(GameObject go)
        {
            var text = new StringBuilder(go.name);
            for (Transform t = go.transform.parent; t != null; t = t.parent) text.Insert(0, t.name + "/");
            return "/" + text;
        }
    }
}
