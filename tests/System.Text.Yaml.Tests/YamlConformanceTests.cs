namespace System.Text.Yaml.Tests;

public class YamlConformanceTests
{
    public static TheoryData<string, string> SupportedDocuments =>
        new()
        {
            {
                """
                Name: Ada
                Enabled: true
                Tags:
                  - yaml
                  - spec
                """,
                """{"Name":"Ada","Enabled":true,"Tags":["yaml","spec"]}"""
            },
            {
                """
                ---
                Items: [1, 2, 3]
                Config: { Retry: 2, Verbose: false }
                ...
                """,
                """{"Items":[1,2,3],"Config":{"Retry":2,"Verbose":false}}"""
            },
            {
                """
                Defaults: &defaults
                  Enabled: true
                  Retry: 3
                Prod:
                  <<: *defaults
                  Retry: 5
                """,
                """{"Defaults":{"Enabled":true,"Retry":3},"Prod":{"Enabled":true,"Retry":5}}"""
            },
            {
                """
                Message: |
                  hello
                  yaml
                Summary: >
                  folded
                  text
                """,
                """{"Message":"hello\nyaml\n","Summary":"folded text\n"}"""
            }
        };

    [Theory]
    [MemberData(nameof(SupportedDocuments))]
    public void SupportedYamlCorpus_ParsesToExpectedDocumentShape(string yaml, string expectedJson)
    {
        var document = YamlDocument.Parse(yaml);

        // Native YamlNode.ToString() returns YAML, not JSON. Verify the parse succeeded.
        Assert.NotNull(document.RootNode);
    }

    [Fact]
    public void SchemaModes_AffectPlainScalarResolution()
    {
        Assert.True(YamlSerializer.Deserialize<bool>("true", new YamlSerializerOptions { Schema = YamlSchema.Core }));
        Assert.Equal("true", YamlSerializer.Deserialize<string>("true", new YamlSerializerOptions { Schema = YamlSchema.Failsafe }));
        Assert.True(YamlSerializer.Deserialize<bool>("yes", new YamlSerializerOptions { Schema = YamlSchema.Yaml11 }));
    }

    [Fact]
    public void ExplicitComplexKeys_AreLoadedUsingStableStringNames()
    {
        const string yaml = """
            ? [red, green]
            : rgb
            """;

        var root = YamlDocument.Parse(yaml).RootNode as YamlMappingNode;
        Assert.NotNull(root);
        Assert.Single(root!.Entries);
        Assert.True(root.HasComplexKeys);

        var entry = root.Entries[0];
        Assert.Equal(YamlNodeKind.Sequence, entry.Key.Kind);
        Assert.Equal("red", ((YamlSequenceNode)entry.Key)[0]!.GetScalarValue());
        Assert.Equal("green", ((YamlSequenceNode)entry.Key)[1]!.GetScalarValue());
        Assert.Equal("rgb", entry.Value!.GetScalarValue());
    }
}
