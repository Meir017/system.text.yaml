using System.Text;

namespace System.Text.Yaml;

/// <summary>
/// Represents a parsed YAML document with a root node and directives.
/// </summary>
public sealed class YamlDocument
{
    private YamlDocument(YamlNode? root, string? yamlVersionDirective, IReadOnlyDictionary<string, string>? tagDirectives)
    {
        RootNode = root;
        YamlVersionDirective = yamlVersionDirective;
        TagDirectives = tagDirectives ?? new Dictionary<string, string>();
    }

    /// <summary>The root node of the document.</summary>
    public YamlNode? RootNode { get; }

    /// <summary>The YAML version directive (e.g., "1.2").</summary>
    public string? YamlVersionDirective { get; }

    /// <summary>The tag directives declared for this document.</summary>
    public IReadOnlyDictionary<string, string> TagDirectives { get; }

    /// <summary>
    /// Parses a UTF-16 YAML payload into a document.
    /// </summary>
    public static YamlDocument Parse(string yaml, YamlReaderOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var result = YamlParser.ParseToNodeDocument(yaml, options);
        return new YamlDocument(result.Root, result.YamlVersionDirective, result.TagDirectives);
    }

    /// <summary>
    /// Parses a UTF-8 YAML payload into a document.
    /// </summary>
    public static YamlDocument Parse(ReadOnlySpan<byte> utf8Yaml, YamlReaderOptions options = default)
        => Parse(Encoding.UTF8.GetString(utf8Yaml), options);

    /// <summary>
    /// Parses all documents from a YAML stream.
    /// </summary>
    public static IReadOnlyList<YamlDocument> ParseAll(string yaml, YamlReaderOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        return YamlParser.ParseToNodeDocuments(yaml, options)
            .Select(result => new YamlDocument(result.Root, result.YamlVersionDirective, result.TagDirectives))
            .ToArray();
    }

    /// <summary>
    /// Parses all documents from a UTF-8 YAML stream.
    /// </summary>
    public static IReadOnlyList<YamlDocument> ParseAll(ReadOnlySpan<byte> utf8Yaml, YamlReaderOptions options = default)
        => ParseAll(Encoding.UTF8.GetString(utf8Yaml), options);

    public override string ToString() => RootNode?.ToString() ?? "";
}