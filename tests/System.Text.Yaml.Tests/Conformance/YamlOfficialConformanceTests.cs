using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace System.Text.Yaml.Tests;

public class YamlOfficialConformanceTests
{
    private static readonly IDeserializer s_suiteDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    [Fact]
    public void YamlTestSuite_LoadCases_MatchExpectedBehavior_WhenEnabled()
    {
        var suitePath = ResolveSuitePath();
        if (suitePath is null)
        {
            return;
        }

        var failures = new List<string>();
        var statistics = new OfficialSuiteStatistics();

        foreach (var path in Directory.EnumerateFiles(suitePath, "*.yaml", SearchOption.TopDirectoryOnly).OrderBy(static path => path, StringComparer.Ordinal))
        {
            var content = File.ReadAllText(path);
            var cases = s_suiteDeserializer.Deserialize<List<OfficialSuiteCase>>(content);

            if (cases is null)
            {
                continue;
            }

            for (var index = 0; index < cases.Count; index++)
            {
                var @case = cases[index];
                statistics.TotalCases++;

                if (string.IsNullOrWhiteSpace(@case.Yaml))
                {
                    continue;
                }

                var caseId = $"{Path.GetFileNameWithoutExtension(path)}[{index}] {@case.Name}".Trim();
                var yamlInput = DecodeTestSuiteSpecialChars(@case.Yaml);

                if (!string.IsNullOrWhiteSpace(@case.Error))
                {
                    statistics.ErrorCases++;

                    try
                    {
                        _ = YamlDocument.Parse(yamlInput);
                        failures.Add($"{caseId}: expected parser failure, but parsing succeeded.");
                    }
                    catch (YamlException)
                    {
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(@case.Json))
                {
                    statistics.SkippedWithoutJson++;
                    continue;
                }

                statistics.LoadCases++;

                try
                {
                    var expectedDocuments = SplitJsonDocuments(@case.Json);
                    // Use native pipeline: parse YAML → YamlNode, then serialize to JSON for comparison
                    var nativeDocuments = YamlParser.ParseToNodeDocuments(yamlInput, default(YamlReaderOptions));

                    if (expectedDocuments.Count != nativeDocuments.Count)
                    {
                        failures.Add($"{caseId}: expected {expectedDocuments.Count} JSON document(s) but produced {nativeDocuments.Count}.");
                        continue;
                    }

                    for (var documentIndex = 0; documentIndex < expectedDocuments.Count; documentIndex++)
                    {
                        var actualJson = YamlNodeToJson(nativeDocuments[documentIndex].Root);
                        var actualNode = JsonNode.Parse(actualJson);
                        if (!JsonNode.DeepEquals(expectedDocuments[documentIndex], actualNode))
                        {
                            failures.Add($"{caseId}: mismatch in document {documentIndex}.");
                            break;
                        }
                    }
                }
                catch (Exception exception) when (exception is YamlException or JsonException or InvalidOperationException or FormatException)
                {
                    failures.Add($"{caseId}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        var passRate = statistics.LoadCases > 0 ? (double)(statistics.LoadCases - failures.Count) / statistics.LoadCases * 100 : 0;
        var summary = $"YAML conformance: {statistics.LoadCases - failures.Count}/{statistics.LoadCases} ({passRate:F1}%), errors checked: {statistics.ErrorCases}, skipped: {statistics.SkippedWithoutJson}";

        var reportPath = Path.Combine(Path.GetTempPath(), "yaml-conformance-report.txt");
        File.WriteAllText(reportPath, summary + Environment.NewLine + string.Join(Environment.NewLine, failures));

        Assert.True(passRate >= 100, summary);
    }

    private static string? ResolveSuitePath()
    {
        var path = Environment.GetEnvironmentVariable("YAML_TEST_SUITE_PATH");
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
        {
            return path;
        }

        return null;
    }

    /// <summary>
    /// Converts a YamlNode tree to a JSON string for comparison with expected JSON.
    /// </summary>
    private static string YamlNodeToJson(YamlNode? node)
    {
        if (node is null) return "null";

        if (node is YamlScalarNode scalar)
        {
            if (scalar.IsNull) return "null";

            // Explicit string tag or non-specific ! tag → always string
            if (scalar.Tag is not null && (scalar.Tag.EndsWith(":str") || scalar.Tag == "!"))
                return JsonSerializer.Serialize(scalar.Value);

            // Quoted/block scalars are always strings
            if (scalar.Style is YamlScalarStyle.SingleQuoted or YamlScalarStyle.DoubleQuoted
                or YamlScalarStyle.Literal or YamlScalarStyle.Folded)
                return JsonSerializer.Serialize(scalar.Value);

            var v = scalar.Value;
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
            if (long.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l)) return l.ToString();
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && long.TryParse(v.AsSpan(2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var hex)) return hex.ToString();
            if (v.StartsWith("0o", StringComparison.OrdinalIgnoreCase)) { try { return Convert.ToInt64(v[2..], 8).ToString(); } catch { } }
            if (decimal.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (v is ".inf" or ".Inf" or ".INF" or "+.inf") return JsonSerializer.Serialize(double.PositiveInfinity);
            if (v is "-.inf" or "-.Inf" or "-.INF") return JsonSerializer.Serialize(double.NegativeInfinity);
            if (v is ".nan" or ".NaN" or ".NAN") return JsonSerializer.Serialize(double.NaN);
            return JsonSerializer.Serialize(v);
        }

        if (node is YamlSequenceNode seq)
        {
            var items = seq.Children.Select(YamlNodeToJson);
            return "[" + string.Join(",", items) + "]";
        }

        if (node is YamlMappingNode map)
        {
            var entries = map.Entries.Select(e =>
            {
                var key = e.Key is YamlScalarNode s ? s.Value : YamlMappingNode.GetStringKey(e.Key);
                return JsonSerializer.Serialize(key) + ":" + YamlNodeToJson(e.Value);
            });
            return "{" + string.Join(",", entries) + "}";
        }

        return "null";
    }

    private static string DecodeTestSuiteSpecialChars(string text)
    {
        // Normalize CRLF first so that ↵\r\n → ↵\n before decode
        var result = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Replace("\u2423", " ")     // ␣ → space
            .Replace("\u2192", "\t")    // → → tab (single)
            .Replace("\u2014\u2014\u2014\u2014\u00BB", "\t")  // ————» → tab (4-wide)
            .Replace("\u2014\u2014\u2014\u00BB", "\t")  // ———» → tab (3-wide)
            .Replace("\u2014\u2014\u00BB", "\t")         // ——» → tab (2-wide)
            .Replace("\u2014\u00BB", "\t")               // —» → tab (1-wide)
            .Replace("\u00BB", "\t")                     // » → tab
            .Replace("\u21D4", "\uFEFF"); // ⇔ → BOM

        var hasEndMarker = result.Contains('\u220E');

        // ↵ represents an explicit newline in content — it replaces the structural \n
        result = result.Replace("\u21B5\n", "\n");
        result = result.Replace("\u21B5", "\n");

        if (hasEndMarker)
        {
            result = result.Replace("\u220E", "");
            if (result.EndsWith('\n'))
            {
                result = result[..^1];
            }
        }

        return result;
    }

    private static IReadOnlyList<JsonNode?> SplitJsonDocuments(string json)
    {
        var results = new List<JsonNode?>();
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { AllowMultipleValues = true });

        while (reader.Read())
        {
            using var document = JsonDocument.ParseValue(ref reader);
            results.Add(JsonNode.Parse(document.RootElement.GetRawText()));
        }

        return results;
    }

    private sealed class OfficialSuiteCase
    {
        public string Name { get; init; } = string.Empty;

        public string Yaml { get; init; } = string.Empty;

        public string Json { get; init; } = string.Empty;

        public string Error { get; init; } = string.Empty;
    }

    private sealed class OfficialSuiteStatistics
    {
        public int TotalCases { get; set; }

        public int LoadCases { get; set; }

        public int ErrorCases { get; set; }

        public int SkippedWithoutJson { get; set; }
    }
}
