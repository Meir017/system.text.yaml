using System.Text;

namespace System.Text.Yaml.Tests;

public class YamlSerializerTests
{
    [Fact]
    public void SerializeAndDeserialize_RoundTripsJsonCompatibleObject()
    {
        var value = new SamplePayload
        {
            Name = "Ada",
            Count = 2,
            Enabled = true,
            Tags = ["yaml", "json"]
        };

        var yaml = YamlSerializer.Serialize(value);
        var restored = YamlSerializer.Deserialize<SamplePayload>(yaml);

        Assert.Equal(
            """
            Name: Ada
            Count: 2
            Enabled: true
            Tags:
              - yaml
              - json
            """.ReplaceLineEndings("\n").TrimEnd(),
            yaml);
        Assert.NotNull(restored);
        Assert.Equal(value.Name, restored.Name);
        Assert.Equal(value.Count, restored.Count);
        Assert.Equal(value.Enabled, restored.Enabled);
        Assert.Equal(value.Tags, restored.Tags);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    public void Deserialize_BooleanScalars_ReturnsExpectedValue(string yaml, bool expected)
    {
        var value = YamlSerializer.Deserialize<bool>(yaml);

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-17", -17)]
    public void Deserialize_IntegerScalars_ReturnsExpectedValue(string yaml, int expected)
    {
        var value = YamlSerializer.Deserialize<int>(yaml);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Deserialize_DecimalScalar_ReturnsExpectedValue()
    {
        var value = YamlSerializer.Deserialize<decimal>("3.14");

        Assert.Equal(3.14m, value);
    }

    [Theory]
    [InlineData("'hello ''yaml'''", "hello 'yaml'")]
    [InlineData("\"hello json\"", "hello json")]
    public void Deserialize_QuotedStrings_ReturnsExpectedValue(string yaml, string expected)
    {
        var value = YamlSerializer.Deserialize<string>(yaml);

        Assert.Equal(expected, value);
    }

    [Fact]
    public void Deserialize_NullScalar_ReturnsNull()
    {
        var value = YamlSerializer.Deserialize<string?>("null");

        Assert.Null(value);
    }

    [Fact]
    public void Deserialize_WithDocumentMarkers_AcceptsJsonSubsetPayload()
    {
        const string yaml = """
            ---
            # comments and block YAML now work
            Name: Ada # inline comment
            Count: 2
            Enabled: true
            Tags:
              - yaml
            ...
            """;

        var value = YamlSerializer.Deserialize<SamplePayload>(yaml);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.Name);
        Assert.Equal(2, value.Count);
        Assert.True(value.Enabled);
        Assert.Equal(["yaml"], value.Tags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Deserialize_EmptyInput_ThrowsYamlException(string yaml)
    {
        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object?>(yaml));

        Assert.Equal("YAML content cannot be empty.", exception.Message);
    }

    [Fact]
    public void Deserialize_NestedYamlAndEmbeddedJson_Works()
    {
        const string yaml = """
            Owner:
              Name: Ada
              Count: 2
              Enabled: true
              Tags:
                - yaml
                - parser
            Scores: [1, 2, 3]
            Settings: {"retry": 2, "level": 5}
            """;

        var value = YamlSerializer.Deserialize<ComplexPayload>(yaml);

        Assert.NotNull(value);
        Assert.NotNull(value.Owner);
        Assert.Equal("Ada", value.Owner.Name);
        Assert.Equal([1, 2, 3], value.Scores);
        Assert.Equal(2, value.Settings["retry"]);
        Assert.Equal(5, value.Settings["level"]);
    }

    [Fact]
    public void Deserialize_BlockScalars_Work()
    {
        const string yaml = """
            Message: |
              hello
              yaml
            Summary: >
              folded
              text
            """;

        var value = YamlSerializer.Deserialize<TextPayload>(yaml);

        Assert.NotNull(value);
        Assert.Equal("hello\nyaml\n", value.Message);
        Assert.Equal("folded text\n", value.Summary);
    }

    [Fact]
    public void Deserialize_FlowCollections_Work()
    {
        const string yaml = """
            Name: Ada
            Tags: [yaml, parser, core]
            Settings: { Retry: 2, Enabled: true }
            """;

        var value = YamlSerializer.Deserialize<FlowPayload>(yaml);
        var document = YamlDocument.Parse(yaml);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.Name);
        Assert.Equal(["yaml", "parser", "core"], value.Tags);
        Assert.Equal(2, value.Settings.Retry);
        Assert.True(value.Settings.Enabled);
        Assert.Equal(YamlNodeKind.Sequence, document.RootNode["Tags"].Kind);
        Assert.Equal(3, document.RootNode["Tags"].AsSequence().Count);
    }

    [Fact]
    public void Deserialize_AnchorsAliasesAndMergeKeys_Work()
    {
        const string yaml = """
            Defaults: &defaults
              Enabled: true
              Retry: 3
            Prod:
              <<: *defaults
              Retry: 5
            Alias: *defaults
            """;

        var value = YamlSerializer.Deserialize<AnchorPayload>(yaml);
        var document = YamlDocument.Parse(yaml);

        Assert.NotNull(value);
        Assert.NotNull(value.Prod);
        Assert.NotNull(value.Alias);
        Assert.True(value.Prod.Enabled);
        Assert.Equal(5, value.Prod.Retry);
        Assert.True(value.Alias.Enabled);
        Assert.Equal(3, value.Alias.Retry);
        Assert.True(document.RootNode["Prod"]["Enabled"].GetScalarBool());
        Assert.Equal(5, document.RootNode["Prod"]["Retry"].GetScalarLong());
    }

    [Fact]
    public void Deserialize_FailsafeSchema_LeavesPlainScalarsAsStrings()
    {
        var options = new YamlSerializerOptions
        {
            Schema = YamlSchema.Failsafe
        };

        Assert.Equal("true", YamlSerializer.Deserialize<string>("true", options));
        Assert.Equal("42", YamlSerializer.Deserialize<string>("42", options));
    }

    [Fact]
    public void Deserialize_Yaml11CompatibilityMode_AcceptsLegacyBooleans()
    {
        var options = new YamlSerializerOptions
        {
            Schema = YamlSchema.Yaml11
        };

        Assert.True(YamlSerializer.Deserialize<bool>("yes", options));
        Assert.False(YamlSerializer.Deserialize<bool>("off", options));
        Assert.Equal(16, YamlSerializer.Deserialize<int>("0x10", options));
    }

    [Fact]
    public void Deserialize_ExplicitTagsCanOverrideSchemaResolution()
    {
        var options = new YamlSerializerOptions
        {
            Schema = YamlSchema.Failsafe
        };

        var value = YamlSerializer.Deserialize<TaggedPayload>("Value: !!str true", options);

        Assert.NotNull(value);
        Assert.Equal("true", value.Value);
    }

    [Fact]
    public void Deserialize_DirectivesCanInfluenceSchemaAndTagResolution()
    {
        const string yaml = """
            %YAML 1.1
            %TAG ! tag:yaml.org,2002:
            ---
            Count: !int 42
            Enabled: yes
            """;

        var value = YamlSerializer.Deserialize<DirectivePayload>(yaml);

        Assert.NotNull(value);
        Assert.Equal(42, value.Count);
        Assert.True(value.Enabled);
    }

    [Fact]
    public void Deserialize_ExplicitKeyValueForm_WorksForScalarKeys()
    {
        const string yaml = """
            ? Name
            : Ada
            ? Count
            : 2
            ? Enabled
            : true
            """;

        var value = YamlSerializer.Deserialize<SamplePayload>(yaml);

        Assert.NotNull(value);
        Assert.Equal("Ada", value.Name);
        Assert.Equal(2, value.Count);
        Assert.True(value.Enabled);
    }

    [Fact]
    public void Utf8YamlReader_ReadsNestedTokens()
    {
        var reader = new Utf8YamlReader(Encoding.UTF8.GetBytes("""
            Name: Ada
            Count: 2
            Tags:
              - yaml
            Enabled: true
            """));

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartObject, reader.TokenType);
        Assert.Equal(0, reader.CurrentDepth);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.PropertyName, reader.TokenType);
        Assert.Equal("Name", reader.ValueText);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.String, reader.TokenType);
        Assert.Equal("Ada", reader.ValueText);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.PropertyName, reader.TokenType);
        Assert.Equal("Count", reader.ValueText);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.Number, reader.TokenType);
        Assert.True((long.TryParse(reader.ValueText, out var count)));
        Assert.Equal(2, count);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.PropertyName, reader.TokenType);
        Assert.Equal("Tags", reader.ValueText);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartArray, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.String, reader.TokenType);
        Assert.Equal("yaml", reader.ValueText);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.EndArray, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.PropertyName, reader.TokenType);
        Assert.Equal("Enabled", reader.ValueText);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.True, reader.TokenType);
        Assert.True((reader.ValueText == "true"));

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.EndObject, reader.TokenType);

        Assert.False(reader.Read());
    }

    [Fact]
    public void Utf8YamlReader_ExposesTagAnchorAndAliasMetadata()
    {
        var reader = new Utf8YamlReader(Encoding.UTF8.GetBytes("""
            Defaults: &defaults
              Enabled: true
            Tagged: !!str true
            Alias: *defaults
            """));

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartObject, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal("Defaults", reader.ValueText);
        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartObject, reader.TokenType);
        Assert.Equal("defaults", reader.Anchor);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.PropertyName, reader.TokenType);
        Assert.Equal("Enabled", reader.ValueText);
        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.True, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.EndObject, reader.TokenType);

        Assert.True(reader.Read());
        Assert.Equal("Tagged", reader.ValueText);
        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.String, reader.TokenType);
        Assert.Equal("tag:yaml.org,2002:str", reader.Tag);
        Assert.Equal("true", reader.ValueText);

        Assert.True(reader.Read());
        Assert.Equal("Alias", reader.ValueText);
        Assert.True(reader.Read());
        Assert.Equal(YamlTokenType.StartObject, reader.TokenType);
        Assert.Equal("defaults", reader.Alias);
    }

    [Fact]
    public void Deserialize_InvalidIndentation_ThrowsYamlException()
    {
        const string yaml = """
            Name:
              - yaml
             - broken
            """;

        Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<object?>(yaml));
    }

    [Fact]
    public void Deserialize_DuplicateKeysCanBeRejected()
    {
        const string yaml = """
            value: 1
            value: 2
            """;

        var options = new YamlSerializerOptions
        {
            DuplicateKeyHandling = YamlDuplicateKeyHandling.Disallow
        };

        var exception = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, int>>(yaml, options));
        Assert.Contains("Duplicate YAML mapping key", exception.Message);
    }

    [Fact]
    public void Serialize_UsesYamlMetadataAndOptions()
    {
        var yaml = YamlSerializer.Serialize(new ConverterPayload
        {
            DisplayName = "Ada",
            State = WorkState.InProgress,
            OptionalCount = null
        }, new YamlSerializerOptions
        {
            WriteDocumentMarkers = true,
            IndentationSize = 4,
            PropertyNamingPolicy = YamlNamingPolicy.CamelCase,
        });

        var restored = YamlSerializer.Deserialize<ConverterPayload>(yaml, new YamlSerializerOptions
        {
            PropertyNamingPolicy = YamlNamingPolicy.CamelCase,
        });

        Assert.Contains("displayName: Ada", yaml);
        Assert.Contains("state: InProgress", yaml);
        Assert.DoesNotContain("optionalCount", yaml); // null is omitted
        Assert.NotNull(restored);
        Assert.Equal("Ada", restored.DisplayName);
        Assert.Equal(WorkState.InProgress, restored.State);
    }

    [Fact]
    public void Serialize_BlockFormat_ProducesReadableYaml()
    {
        var value = new SamplePayload
        {
            Name = "Ada",
            Count = 2,
            Enabled = true,
            Tags = ["yaml"]
        };

        var yaml = YamlSerializer.Serialize(value);

        Assert.Contains("Name: Ada", yaml);
        Assert.Contains("Count: 2", yaml);
        Assert.Contains("Enabled: true", yaml);
        Assert.Contains("- yaml", yaml);
    }

    public sealed class SamplePayload
    {
        public string Name { get; init; } = string.Empty;

        public int Count { get; init; }

        public bool Enabled { get; init; }

        public string[] Tags { get; init; } = [];
    }

    public sealed class ComplexPayload
    {
        public SamplePayload Owner { get; init; } = new();

        public int[] Scores { get; init; } = [];

        public Dictionary<string, int> Settings { get; init; } = new();
    }

    public sealed class FlowPayload
    {
        public string Name { get; init; } = string.Empty;

        public string[] Tags { get; init; } = [];

        public FlowSettings Settings { get; init; } = new();
    }

    public sealed class FlowSettings
    {
        public int Retry { get; init; }

        public bool Enabled { get; init; }
    }

    public sealed class AnchorPayload
    {
        public FlowSettings Defaults { get; init; } = new();

        public FlowSettings Prod { get; init; } = new();

        public FlowSettings Alias { get; init; } = new();
    }

    public sealed class TextPayload
    {
        public string Message { get; init; } = string.Empty;

        public string Summary { get; init; } = string.Empty;
    }

    public sealed class TaggedPayload
    {
        public string Value { get; init; } = string.Empty;
    }

    public sealed class DirectivePayload
    {
        public int Count { get; init; }

        public bool Enabled { get; init; }
    }

    public sealed class ConverterPayload
    {
        public string DisplayName { get; init; } = string.Empty;

        public WorkState State { get; init; }

        public int? OptionalCount { get; init; }
    }

    public enum WorkState
    {
        New,
        InProgress,
        Done
    }
}
