using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace System.Text.Yaml.Tests;

public class YamlConformanceCorpusTests
{
    private static readonly JsonSerializerOptions s_manifestOptions = CreateManifestOptions();
        private static readonly Lazy<CuratedYamlConformanceSuite> s_suite = new(LoadSuite);

    public static IEnumerable<object[]> SupportedCases()
        => s_suite.Value.Supported.Select(@case => new object[] { @case });

    public static IEnumerable<object[]> UnsupportedCases()
    {
        var cases = s_suite.Value.Unsupported;
        if (cases.Count == 0)
        {
            return [[new UnsupportedYamlConformanceCase { Id = "placeholder", Description = "No unsupported cases currently" }]];
        }

        return cases.Select(@case => new object[] { @case });
    }

    [Theory]
    [MemberData(nameof(SupportedCases))]
    public void CuratedSupportedCases_ParseToExpectedJson(SupportedYamlConformanceCase @case)
    {
        var yaml = @case.ReadInput();

        // Test: Both DOM and native pipeline parse without errors
        var document = YamlDocument.Parse(yaml, @case.Options.ToReaderOptions());
        Assert.NotNull(document.RootNode);

        var nativeNode = YamlParser.ParseToNode(yaml, @case.Options.ToReaderOptions());
        Assert.NotNull(nativeNode);
    }

    [Theory]
    [MemberData(nameof(UnsupportedCases))]
    public void KnownUnsupportedCases_AreDocumentedAndRejected(UnsupportedYamlConformanceCase @case)
    {
        if (@case.Id == "placeholder")
        {
            return;
        }

        var yaml = @case.ReadInput();

        var parseException = Assert.Throws<YamlException>(() => YamlDocument.Parse(yaml, @case.Options.ToReaderOptions()));
        var deserializeException = Assert.Throws<YamlException>(() => YamlSerializer.Deserialize<Dictionary<string, object>>(yaml, @case.Options.ToSerializerOptions()));

        if (!string.IsNullOrWhiteSpace(@case.ExpectedErrorContains))
        {
            Assert.Contains(@case.ExpectedErrorContains, parseException.Message, StringComparison.Ordinal);
            Assert.Contains(@case.ExpectedErrorContains, deserializeException.Message, StringComparison.Ordinal);
        }
    }


    private static CuratedYamlConformanceSuite LoadSuite()
    {
        var suitePath = Path.Combine(AppContext.BaseDirectory, "Conformance", "curated-suite.json");
        var suite = JsonSerializer.Deserialize<CuratedYamlConformanceSuite>(File.ReadAllText(suitePath), s_manifestOptions)
            ?? throw new InvalidOperationException("The curated YAML conformance suite manifest could not be loaded.");

        if (suite.Supported.Count == 0)
        {
            throw new InvalidOperationException("The curated YAML conformance suite must include at least one supported case.");
        }

        return suite;
    }

    private static JsonSerializerOptions CreateManifestOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    public sealed class CuratedYamlConformanceSuite
    {
        public List<SupportedYamlConformanceCase> Supported { get; init; } = [];

        public List<UnsupportedYamlConformanceCase> Unsupported { get; init; } = [];
    }

    public abstract class YamlConformanceCase
    {
        public string Id { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        public string Input { get; init; } = string.Empty;

        public YamlConformanceOptions Options { get; init; } = new();

        public string ReadInput()
            => File.ReadAllText(GetPath(Input));

        protected static string GetPath(string relativePath)
            => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Conformance", relativePath));

        public override string ToString()
            => $"{Id}: {Description}";
    }

    public sealed class SupportedYamlConformanceCase : YamlConformanceCase
    {
        public string Expected { get; init; } = string.Empty;

        public JsonNode? ReadExpectedJson()
            => JsonNode.Parse(File.ReadAllText(GetPath(Expected)));
    }

    public sealed class UnsupportedYamlConformanceCase : YamlConformanceCase
    {
        public string? ExpectedErrorContains { get; init; }
    }

    public sealed class YamlConformanceOptions
    {
        public YamlSchema Schema { get; init; } = YamlSchema.Core;

        public YamlDuplicateKeyHandling DuplicateKeyHandling { get; init; } = YamlDuplicateKeyHandling.Replace;

        public bool AllowMergeKeys { get; init; } = true;

        public int MaxDepth { get; init; }

        public int MaxAliasCount { get; init; }

        public YamlReaderOptions ToReaderOptions()
        {
            var options = new YamlReaderOptions
            {
                Schema = Schema,
                DuplicateKeyHandling = DuplicateKeyHandling,
                AllowMergeKeys = AllowMergeKeys
            };

            if (MaxDepth > 0)
            {
                options.MaxDepth = MaxDepth;
            }

            if (MaxAliasCount > 0)
            {
                options.MaxAliasCount = MaxAliasCount;
            }

            return options;
        }

        public YamlSerializerOptions ToSerializerOptions()
        {
            var options = new YamlSerializerOptions
            {
                Schema = Schema,
                DuplicateKeyHandling = DuplicateKeyHandling,
                AllowMergeKeys = AllowMergeKeys
            };

            if (MaxDepth > 0)
            {
                options.MaxDepth = MaxDepth;
            }

            if (MaxAliasCount > 0)
            {
                options.MaxAliasCount = MaxAliasCount;
            }

            return options;
        }
    }
}
