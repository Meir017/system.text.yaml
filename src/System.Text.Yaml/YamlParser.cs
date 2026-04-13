using System.Text;

namespace System.Text.Yaml;

internal static class YamlParser
{
    public static YamlNode? ParseToNode(string yaml, YamlReaderOptions options = default)
    {
        return ParseToNodeDocument(yaml, options).Root;
    }

    public static YamlDocumentParseResult ParseToNodeDocument(string yaml, YamlReaderOptions options = default)
    {
        var documents = ParseToNodeDocuments(yaml, options);
        return documents.Count switch
        {
            0 => throw new YamlException("YAML content cannot be empty."),
            1 => documents[0],
            _ => throw new YamlException("Multiple YAML documents were found. Use ParseToNodeDocuments for multi-document.")
        };
    }

    public static IReadOnlyList<YamlDocumentParseResult> ParseToNodeDocuments(string yaml, YamlReaderOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        if (string.IsNullOrWhiteSpace(yaml))
        {
            throw new YamlException("YAML content cannot be empty.");
        }

        return new YamlNodeComposer(yaml, options).ComposeAll();
    }
}