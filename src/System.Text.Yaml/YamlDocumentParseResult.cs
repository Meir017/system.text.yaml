namespace System.Text.Yaml;

/// <summary>
/// Result of parsing a single YAML document. Contains the root node and document-level metadata.
/// </summary>
internal sealed class YamlDocumentParseResult
{
    public YamlDocumentParseResult(YamlNode? root, string? yamlVersionDirective,
        Dictionary<string, string> tagDirectives, YamlSchema effectiveSchema)
    {
        Root = root;
        YamlVersionDirective = yamlVersionDirective;
        TagDirectives = tagDirectives;
        EffectiveSchema = effectiveSchema;
    }

    public YamlNode? Root { get; }
    public string? YamlVersionDirective { get; }
    public IReadOnlyDictionary<string, string> TagDirectives { get; }
    public YamlSchema EffectiveSchema { get; }
}
