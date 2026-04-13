using System.Text;

namespace System.Text.Yaml;

/// <summary>
/// Provides high-performance YAML serialization and deserialization.
/// </summary>
public static class YamlSerializer
{
    // -----------------------------------------------------------------------
    // Serialization
    // -----------------------------------------------------------------------

    public static string Serialize<TValue>(TValue value, YamlSerializerOptions? options = null)
    {
        options ??= new YamlSerializerOptions();
        return YamlObjectWriter.Serialize(value, options);
    }

    public static string Serialize(object? value, Type inputType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        options ??= new YamlSerializerOptions();
        return YamlObjectWriter.Serialize(value, options);
    }

    public static byte[] SerializeToUtf8Bytes<TValue>(TValue value, YamlSerializerOptions? options = null)
    {
        return Encoding.UTF8.GetBytes(Serialize(value, options));
    }

    // -----------------------------------------------------------------------
    // Deserialization
    // -----------------------------------------------------------------------

    public static TValue? Deserialize<TValue>(string yaml, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        options ??= new YamlSerializerOptions();
        var result = YamlParser.ParseToNodeDocument(yaml, options.CreateReaderOptions());
        var effectiveOptions = ApplyEffectiveSchema(options, result);
        return YamlObjectMapper.Deserialize<TValue>(result.Root, effectiveOptions);
    }

    public static object? Deserialize(string yaml, Type returnType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(returnType);
        options ??= new YamlSerializerOptions();
        var result = YamlParser.ParseToNodeDocument(yaml, options.CreateReaderOptions());
        var effectiveOptions = ApplyEffectiveSchema(options, result);
        return YamlObjectMapper.Deserialize(result.Root, returnType, effectiveOptions);
    }

    public static TValue? Deserialize<TValue>(ReadOnlySpan<byte> utf8Yaml, YamlSerializerOptions? options = null)
        => Deserialize<TValue>(Encoding.UTF8.GetString(utf8Yaml), options);

    public static IReadOnlyList<TValue?> DeserializeAll<TValue>(string yaml, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        options ??= new YamlSerializerOptions();

        return YamlParser.ParseToNodeDocuments(yaml, options.CreateReaderOptions())
            .Select(document =>
            {
                var effectiveOptions = ApplyEffectiveSchema(options, document);
                return YamlObjectMapper.Deserialize<TValue>(document.Root, effectiveOptions);
            })
            .ToArray();
    }

    public static IReadOnlyList<object?> DeserializeAll(string yaml, Type returnType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(returnType);
        options ??= new YamlSerializerOptions();

        return YamlParser.ParseToNodeDocuments(yaml, options.CreateReaderOptions())
            .Select(document =>
            {
                var effectiveOptions = ApplyEffectiveSchema(options, document);
                return YamlObjectMapper.Deserialize(document.Root, returnType, effectiveOptions);
            })
            .ToArray();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static YamlSerializerOptions ApplyEffectiveSchema(YamlSerializerOptions options, YamlDocumentParseResult result)
    {
        if (result.EffectiveSchema != options.Schema)
        {
            return new YamlSerializerOptions
            {
                Schema = result.EffectiveSchema,
                PropertyNamingPolicy = options.PropertyNamingPolicy,
                AllowMergeKeys = options.AllowMergeKeys,
                DuplicateKeyHandling = options.DuplicateKeyHandling,
                MaxDepth = options.MaxDepth,
                MaxAliasCount = options.MaxAliasCount,
            };
        }

        return options;
    }
}