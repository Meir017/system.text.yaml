using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Text.Yaml;

/// <summary>
/// Maps a YamlNode tree to CLR objects using reflection.
/// </summary>
internal static class YamlObjectMapper
{
    private static readonly ConcurrentDictionary<Type, TypeAccessor> s_typeCache = new();

    public static T? Deserialize<T>(YamlNode? node, YamlSerializerOptions? options)
    {
        var result = DeserializeNode(node, typeof(T), options ?? new YamlSerializerOptions());
        return result is null ? default : (T)result;
    }

    public static object? Deserialize(YamlNode? node, Type targetType, YamlSerializerOptions? options)
    {
        return DeserializeNode(node, targetType, options ?? new YamlSerializerOptions());
    }

    private static object? DeserializeNode(YamlNode? node, Type targetType, YamlSerializerOptions options)
    {
        if (node is null || (node is YamlScalarNode { IsNull: true }))
        {
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        // Nullable<T> — unwrap
        var underlyingNullable = Nullable.GetUnderlyingType(targetType);
        if (underlyingNullable is not null)
        {
            if (node is YamlScalarNode { IsNull: true }) return null;
            return DeserializeNode(node, underlyingNullable, options);
        }

        // Direct YamlNode assignment
        if (targetType == typeof(YamlNode) || targetType.IsAssignableFrom(node.GetType()))
            return node;

        // Scalar
        if (node is YamlScalarNode scalar)
            return ConvertScalar(scalar, targetType, options);

        // Sequence
        if (node is YamlSequenceNode seq)
            return DeserializeSequence(seq, targetType, options);

        // Mapping
        if (node is YamlMappingNode map)
            return DeserializeMapping(map, targetType, options);

        return null;
    }

    // -----------------------------------------------------------------------
    // Scalar conversion
    // -----------------------------------------------------------------------

    private static object? ConvertScalar(YamlScalarNode scalar, Type targetType, YamlSerializerOptions options)
    {
        var value = scalar.Value;

        if (targetType == typeof(string)) return value;
        if (targetType == typeof(object)) return ResolveScalarToObject(scalar, options);

        if (targetType == typeof(bool))
            return TryParseBool(value, options) ?? throw new YamlException($"Cannot convert '{value}' to Boolean.");

        if (targetType == typeof(int))
            return (int)ParseInteger(value);
        if (targetType == typeof(long))
            return ParseInteger(value);
        if (targetType == typeof(short))
            return (short)ParseInteger(value);
        if (targetType == typeof(byte))
            return (byte)ParseInteger(value);

        if (targetType == typeof(float))
            return ParseFloat(value);
        if (targetType == typeof(double))
            return (double)ParseFloat(value);
        if (targetType == typeof(decimal))
            return ParseDecimal(value);

        if (targetType == typeof(DateTime))
            return DateTime.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(DateTimeOffset))
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(TimeSpan))
            return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
        if (targetType == typeof(Guid))
            return Guid.Parse(value);
        if (targetType == typeof(Uri))
            return new Uri(value, UriKind.RelativeOrAbsolute);

        if (targetType.IsEnum)
            return Enum.Parse(targetType, value, ignoreCase: true);

        // Fallback: try TypeConverter or Convert.ChangeType
        try
        {
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            throw new YamlException($"Cannot convert YAML scalar '{value}' to {targetType.Name}.");
        }
    }

    private static object ResolveScalarToObject(YamlScalarNode scalar, YamlSerializerOptions options)
    {
        var value = scalar.Value;
        if (scalar.Style is YamlScalarStyle.SingleQuoted or YamlScalarStyle.DoubleQuoted
            or YamlScalarStyle.Literal or YamlScalarStyle.Folded)
            return value;

        if (value.Length == 0 || value == "~" || value.Equals("null", StringComparison.OrdinalIgnoreCase))
            return value; // keep as string for object target (caller checks null separately)

        if (TryParseBool(value, options) is bool b) return b;

        if (TryParseInteger(value, out var l)) return l;

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
        {
            if (value is ".inf" or ".Inf" or ".INF" or "+.inf") return float.PositiveInfinity;
            if (value is "-.inf" or "-.Inf" or "-.INF") return float.NegativeInfinity;
            if (value is ".nan" or ".NaN" or ".NAN") return float.NaN;
            return f;
        }

        return value;
    }

    private static bool? TryParseBool(string value, YamlSerializerOptions options)
    {
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

        // YAML 1.1 legacy booleans
        if (options.Schema == YamlSchema.Yaml11)
        {
            var lower = value.ToLowerInvariant();
            if (lower is "yes" or "y" or "on") return true;
            if (lower is "no" or "n" or "off") return false;
        }

        return null;
    }

    private static long ParseInteger(string value)
    {
        if (TryParseInteger(value, out var result)) return result;
        throw new YamlException($"Cannot parse '{value}' as integer.");
    }

    private static bool TryParseInteger(string value, out long result)
    {
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value.Length > 2)
            return long.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

        if (value.StartsWith("0o", StringComparison.OrdinalIgnoreCase) && value.Length > 2)
        {
            try { result = Convert.ToInt64(value[2..], 8); return true; }
            catch { result = 0; return false; }
        }

        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static float ParseFloat(string value)
    {
        if (value is ".inf" or ".Inf" or ".INF" or "+.inf") return float.PositiveInfinity;
        if (value is "-.inf" or "-.Inf" or "-.INF") return float.NegativeInfinity;
        if (value is ".nan" or ".NaN" or ".NAN") return float.NaN;
        return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // -----------------------------------------------------------------------
    // Sequence deserialization
    // -----------------------------------------------------------------------

    private static object? DeserializeSequence(YamlSequenceNode seq, Type targetType, YamlSerializerOptions options)
    {
        // T[]
        if (targetType.IsArray)
        {
            var elemType = targetType.GetElementType()!;
            var array = Array.CreateInstance(elemType, seq.Count);
            for (var i = 0; i < seq.Count; i++)
            {
                array.SetValue(DeserializeNode(seq[i], elemType, options), i);
            }
            return array;
        }

        // List<T>, IList<T>, IReadOnlyList<T>, IEnumerable<T>, ICollection<T>
        var listElemType = GetListElementType(targetType);
        if (listElemType is not null)
        {
            var listType = typeof(List<>).MakeGenericType(listElemType);
            var list = (IList)Activator.CreateInstance(listType, seq.Count)!;
            foreach (var item in seq.Children)
            {
                list.Add(DeserializeNode(item, listElemType, options));
            }

            if (targetType.IsArray) return ConvertListToArray(list, listElemType);
            return list;
        }

        // object — return List<object?>
        if (targetType == typeof(object))
        {
            var list = new List<object?>(seq.Count);
            foreach (var item in seq.Children)
            {
                list.Add(DeserializeNode(item, typeof(object), options));
            }
            return list;
        }

        throw new YamlException($"Cannot deserialize YAML sequence to {targetType.Name}.");
    }

    private static Type? GetListElementType(Type type)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(List<>) || def == typeof(IList<>) || def == typeof(IReadOnlyList<>)
                || def == typeof(IEnumerable<>) || def == typeof(ICollection<>) || def == typeof(IReadOnlyCollection<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        // Check implemented interfaces
        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static Array ConvertListToArray(IList list, Type elemType)
    {
        var arr = Array.CreateInstance(elemType, list.Count);
        list.CopyTo(arr, 0);
        return arr;
    }

    // -----------------------------------------------------------------------
    // Mapping deserialization
    // -----------------------------------------------------------------------

    private static object? DeserializeMapping(YamlMappingNode map, Type targetType, YamlSerializerOptions options)
    {
        // Dictionary<K,V>
        var dictTypes = GetDictionaryTypes(targetType);
        if (dictTypes is not null)
        {
            return DeserializeDictionary(map, targetType, dictTypes.Value.Key, dictTypes.Value.Value, options);
        }

        // object → Dictionary<string, object?>
        if (targetType == typeof(object))
        {
            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var entry in map.Entries)
            {
                var key = YamlMappingNode.GetStringKey(entry.Key);
                dict[key] = DeserializeNode(entry.Value, typeof(object), options);
            }
            return dict;
        }

        // POCO
        return DeserializePoco(map, targetType, options);
    }

    private static object? DeserializeDictionary(YamlMappingNode map, Type dictType,
        Type keyType, Type valueType, YamlSerializerOptions options)
    {
        var concreteDictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
        var dict = (IDictionary)Activator.CreateInstance(concreteDictType)!;

        foreach (var entry in map.Entries)
        {
            var keyScalar = entry.Key is YamlScalarNode s ? s.Value : YamlMappingNode.GetStringKey(entry.Key);
            var key = keyType == typeof(string)
                ? keyScalar
                : Convert.ChangeType(keyScalar, keyType, CultureInfo.InvariantCulture);
            var value = DeserializeNode(entry.Value, valueType, options);
            dict[key!] = value;
        }

        return dict;
    }

    private static KeyValuePair<Type, Type>? GetDictionaryTypes(Type type)
    {
        if (type.IsGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            if (def == typeof(Dictionary<,>) || def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
            {
                var args = type.GetGenericArguments();
                return new KeyValuePair<Type, Type>(args[0], args[1]);
            }
        }

        foreach (var iface in type.GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                var args = iface.GetGenericArguments();
                return new KeyValuePair<Type, Type>(args[0], args[1]);
            }
        }

        return null;
    }

    private static object DeserializePoco(YamlMappingNode map, Type targetType, YamlSerializerOptions options)
    {
        var accessor = s_typeCache.GetOrAdd(targetType, t => new TypeAccessor(t, options));
        var instance = Activator.CreateInstance(targetType)
            ?? throw new YamlException($"Cannot create instance of {targetType.Name}.");

        foreach (var entry in map.Entries)
        {
            var yamlKey = entry.Key is YamlScalarNode s ? s.Value : YamlMappingNode.GetStringKey(entry.Key);

            if (accessor.TryGetProperty(yamlKey, out var prop))
            {
                var value = DeserializeNode(entry.Value, prop.PropertyType, options);
                prop.SetValue(instance, value);
            }
            // Unknown properties are silently ignored (like STJ default)
        }

        return instance;
    }

    // -----------------------------------------------------------------------
    // Type metadata cache
    // -----------------------------------------------------------------------

    private sealed class TypeAccessor
    {
        private readonly Dictionary<string, PropertyInfo> _properties;

        public TypeAccessor(Type type, YamlSerializerOptions options)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite);

            _properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in props)
            {
                // Check for [YamlPropertyName] attribute
                var yamlNameAttr = prop.GetCustomAttribute<YamlPropertyNameAttribute>();
                var name = yamlNameAttr?.Name ?? ApplyNamingPolicy(prop.Name, options);

                // Check for [YamlIgnore]
                if (prop.GetCustomAttribute<YamlIgnoreAttribute>() is not null)
                    continue;

                _properties[name] = prop;

                // Also register the raw property name for case-insensitive matching
                if (!_properties.ContainsKey(prop.Name))
                    _properties[prop.Name] = prop;
            }
        }

        public bool TryGetProperty(string yamlKey, out PropertyInfo prop)
            => _properties.TryGetValue(yamlKey, out prop!);

        private static string ApplyNamingPolicy(string name, YamlSerializerOptions options)
        {
            return options.PropertyNamingPolicy switch
            {
                YamlNamingPolicy.CamelCase => char.ToLowerInvariant(name[0]) + name[1..],
                YamlNamingPolicy.SnakeCase => ToSnakeCase(name),
                YamlNamingPolicy.KebabCase => ToKebabCase(name),
                _ => name // PascalCase (default)
            };
        }

        private static string ToSnakeCase(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }

        private static string ToKebabCase(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('-');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }
    }
}

/// <summary>Specifies the YAML property name for serialization/deserialization.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class YamlPropertyNameAttribute : Attribute
{
    public YamlPropertyNameAttribute(string name) => Name = name;
    public string Name { get; }
}

/// <summary>Indicates a property should be ignored during YAML serialization/deserialization.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class YamlIgnoreAttribute : Attribute { }
