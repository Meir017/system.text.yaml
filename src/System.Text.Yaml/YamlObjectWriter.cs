using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace System.Text.Yaml;

/// <summary>
/// Serializes CLR objects directly to YAML strings using reflection.
/// Replaces the POCO → JsonElement → YAML pipeline with POCO → YAML.
/// </summary>
internal static class YamlObjectWriter
{
    private static readonly ConcurrentDictionary<Type, SerializableTypeInfo> s_typeCache = new();

    public static string Serialize<T>(T value, YamlSerializerOptions options)
    {
        var sb = new StringBuilder(256);
        if (options.WriteDocumentMarkers)
        {
            sb.Append("---");
            sb.Append(options.NewLine);
        }

        WriteValue(sb, value, typeof(T), 0, options, isRoot: true);
        return sb.ToString();
    }

    public static byte[] SerializeToUtf8Bytes<T>(T value, YamlSerializerOptions options)
    {
        return Encoding.UTF8.GetBytes(Serialize(value, options));
    }

    private static void WriteValue(StringBuilder sb, object? value, Type declaredType, int indent,
        YamlSerializerOptions options, bool isRoot = false, bool inlineFirstKey = false)
    {
        if (value is null)
        {
            sb.Append("null");
            return;
        }

        var type = value.GetType();

        // Primitives / scalars
        if (IsScalarType(type))
        {
            WriteScalar(sb, value, type, options);
            return;
        }

        // Dictionary<K,V>
        if (value is IDictionary dict)
        {
            WriteDictionary(sb, dict, indent, options, inlineFirstKey);
            return;
        }

        // IEnumerable (arrays, lists, etc.) — but not string/dict
        if (value is IEnumerable enumerable)
        {
            WriteSequence(sb, enumerable, indent, options);
            return;
        }

        // POCO
        WritePoco(sb, value, type, indent, options, inlineFirstKey);
    }

    private static void WriteScalar(StringBuilder sb, object value, Type type, YamlSerializerOptions options)
    {
        if (value is string s)
        {
            WriteStringScalar(sb, s, options);
            return;
        }

        if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
        if (value is int i) { sb.Append(i); return; }
        if (value is long l) { sb.Append(l); return; }
        if (value is short sh) { sb.Append(sh); return; }
        if (value is byte by) { sb.Append(by); return; }
        if (value is float f)
        {
            if (float.IsPositiveInfinity(f)) { sb.Append(".inf"); return; }
            if (float.IsNegativeInfinity(f)) { sb.Append("-.inf"); return; }
            if (float.IsNaN(f)) { sb.Append(".nan"); return; }
            sb.Append(f.ToString("G", CultureInfo.InvariantCulture));
            return;
        }
        if (value is double d)
        {
            if (double.IsPositiveInfinity(d)) { sb.Append(".inf"); return; }
            if (double.IsNegativeInfinity(d)) { sb.Append("-.inf"); return; }
            if (double.IsNaN(d)) { sb.Append(".nan"); return; }
            sb.Append(d.ToString("G", CultureInfo.InvariantCulture));
            return;
        }
        if (value is decimal dec) { sb.Append(dec.ToString(CultureInfo.InvariantCulture)); return; }

        if (value is DateTime dt) { sb.Append(dt.ToString("O", CultureInfo.InvariantCulture)); return; }
        if (value is DateTimeOffset dto) { sb.Append(dto.ToString("O", CultureInfo.InvariantCulture)); return; }
        if (value is Guid g) { sb.Append(g); return; }

        if (type.IsEnum)
        {
            sb.Append(value.ToString());
            return;
        }

        sb.Append(value.ToString());
    }

    private static void WriteStringScalar(StringBuilder sb, string value, YamlSerializerOptions options)
    {
        if (value.Length == 0) { sb.Append("\"\""); return; }

        // Use block scalar for multiline strings (called from WritePropertyValue context)
        // The caller must handle the newline and indentation before calling this
        if (value.Contains('\n') && !value.Contains('\r'))
        {
            // Use block scalar: literal (|) for preserving newlines
            var chomp = value.EndsWith('\n') ? "" : "-";
            sb.Append('|');
            sb.Append(chomp);
            return; // Block scalar content is written by the property value writer
        }

        // Check if quoting is needed
        if (NeedsQuoting(value))
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
        }
        else
        {
            sb.Append(value);
        }
    }

    private static bool NeedsQuoting(string value)
    {
        if (value.Length == 0) return true;

        // Values that look like booleans, null, or numbers
        if (value is "true" or "false" or "True" or "False" or "TRUE" or "FALSE"
            or "null" or "Null" or "NULL" or "~"
            or "yes" or "no" or "Yes" or "No" or "YES" or "NO"
            or "on" or "off" or "On" or "Off" or "ON" or "OFF"
            or ".inf" or "-.inf" or ".nan")
            return true;

        // Contains characters that require quoting
        if (value.Contains('\n') || value.Contains('\r') || value.Contains('\t'))
            return true;

        // Starts with indicator characters
        var first = value[0];
        if (first is '#' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`'
            or '{' or '}' or '[' or ']' or ',' or '?' or '-' or ':')
            return true;

        // Contains ': ' or ' #' patterns
        if (value.Contains(": ") || value.Contains(" #"))
            return true;

        // Looks like a number
        if (char.IsDigit(first) || (first == '.' && value.Length > 1))
        {
            if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return true;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                return true;
        }

        return false;
    }

    private static void WriteSequence(StringBuilder sb, IEnumerable items, int indent, YamlSerializerOptions options)
    {
        var nl = options.NewLine;
        var indentStr = new string(' ', indent);
        var childIndent = indent + options.IndentationSize;
        var first = true;

        foreach (var item in items)
        {
            if (!first || indent > 0)
            {
                sb.Append(nl);
            }

            sb.Append(indentStr);
            sb.Append("- ");

            if (item is null)
            {
                sb.Append("null");
            }
            else if (IsScalarType(item.GetType()))
            {
                WriteValue(sb, item, item.GetType(), childIndent, options);
            }
            else if (item is IDictionary || (!IsScalarType(item.GetType()) && item is not IEnumerable))
            {
                // POCO or dict: inline the first key after "- "
                WriteValue(sb, item, item.GetType(), indent + options.IndentationSize, options, inlineFirstKey: true);
            }
            else
            {
                sb.Append(nl);
                WriteValue(sb, item, item.GetType(), childIndent, options);
            }

            first = false;
        }
    }

    private static void WriteDictionary(StringBuilder sb, IDictionary dict, int indent,
        YamlSerializerOptions options, bool inlineFirstKey = false)
    {
        var nl = options.NewLine;
        var indentStr = new string(' ', indent);
        var childIndent = indent + options.IndentationSize;
        var first = true;

        foreach (DictionaryEntry entry in dict)
        {
            if (!first || (!inlineFirstKey && indent > 0))
                sb.Append(nl);

            if (!first || !inlineFirstKey)
                sb.Append(indentStr);

            WritePropertyKey(sb, entry.Key?.ToString() ?? "null");
            sb.Append(':');

            WritePropertyValue(sb, entry.Value, childIndent, options);

            first = false;
        }
    }

    private static void WritePoco(StringBuilder sb, object value, Type type, int indent,
        YamlSerializerOptions options, bool inlineFirstKey = false)
    {
        var info = s_typeCache.GetOrAdd(type, t => new SerializableTypeInfo(t, options));
        var nl = options.NewLine;
        var indentStr = new string(' ', indent);
        var childIndent = indent + options.IndentationSize;
        var first = true;

        foreach (var prop in info.Properties)
        {
            var propValue = prop.GetValue(value);
            if (propValue is null) continue;

            if (!first || (!inlineFirstKey && indent > 0))
                sb.Append(nl);

            if (!first || !inlineFirstKey)
                sb.Append(indentStr);

            WritePropertyKey(sb, prop.YamlName);
            sb.Append(':');

            WritePropertyValue(sb, propValue, childIndent, options);

            first = false;
        }
    }

    private static void WritePropertyKey(StringBuilder sb, string key)
    {
        if (NeedsQuoting(key))
        {
            sb.Append('"');
            foreach (var c in key)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else sb.Append(c);
            }
            sb.Append('"');
        }
        else
        {
            sb.Append(key);
        }
    }

    private static void WritePropertyValue(StringBuilder sb, object? value, int childIndent,
        YamlSerializerOptions options)
    {
        if (value is null)
        {
            sb.Append(' ');
            sb.Append("null");
            return;
        }

        var type = value.GetType();
        if (type == typeof(string))
        {
            var str = (string)value;
            // Check if this is a multiline string → use block scalar
            if (str.Contains('\n') && !str.Contains('\r'))
            {
                var chomp = str.EndsWith('\n') ? "" : "-";
                sb.Append(" |");
                sb.Append(chomp);
                var nl = options.NewLine;
                var indentStr = new string(' ', childIndent);
                // Write each line of the block scalar
                var content = str.EndsWith('\n') ? str[..^1] : str;
                foreach (var line in content.Split('\n'))
                {
                    sb.Append(nl);
                    sb.Append(indentStr);
                    sb.Append(line);
                }
                return;
            }

            sb.Append(' ');
            WriteStringScalar(sb, str, options);
        }
        else if (IsScalarType(type))
        {
            sb.Append(' ');
            WriteValue(sb, value, type, childIndent, options);
        }
        else
        {
            // Complex value — starts on next line
            WriteValue(sb, value, type, childIndent, options);
        }
    }

    private static bool IsScalarType(Type type) =>
        type == typeof(string) || type == typeof(bool) ||
        type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
        type == typeof(float) || type == typeof(double) || type == typeof(decimal) ||
        type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
        type == typeof(Guid) || type == typeof(Uri) ||
        type.IsEnum;

    // -----------------------------------------------------------------------
    // Serialization type info cache
    // -----------------------------------------------------------------------

    internal sealed class SerializableTypeInfo
    {
        public SerializablePropertyInfo[] Properties { get; }

        public SerializableTypeInfo(Type type, YamlSerializerOptions options)
        {
            Properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead)
                .Where(p => p.GetCustomAttribute<YamlIgnoreAttribute>() is null)
                .Select(p => new SerializablePropertyInfo(p, options))
                .ToArray();
        }
    }

    internal sealed class SerializablePropertyInfo
    {
        public string YamlName { get; }
        public PropertyInfo Property { get; }
        public Type PropertyType => Property.PropertyType;

        public SerializablePropertyInfo(PropertyInfo property, YamlSerializerOptions options)
        {
            Property = property;
            var nameAttr = property.GetCustomAttribute<YamlPropertyNameAttribute>();
            YamlName = nameAttr?.Name ?? ApplyNamingPolicy(property.Name, options);
        }

        public object? GetValue(object instance) => Property.GetValue(instance);

        private static string ApplyNamingPolicy(string name, YamlSerializerOptions options)
        {
            return options.PropertyNamingPolicy switch
            {
                YamlNamingPolicy.CamelCase => char.ToLowerInvariant(name[0]) + name[1..],
                YamlNamingPolicy.SnakeCase => ToSnakeCase(name),
                YamlNamingPolicy.KebabCase => ToKebabCase(name),
                _ => name
            };
        }

        private static string ToSnakeCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }

        private static string ToKebabCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }
    }
}
