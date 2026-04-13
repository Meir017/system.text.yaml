namespace System.Text.Yaml.Tests;

public class Utf8YamlScannerTests
{
    [Fact]
    public void Scanner_SimpleMapping_ProducesCorrectTokens()
    {
        var scanner = new Utf8YamlScanner("key: value\n");
        var tokens = scanner.ReadAllTokens();
        var types = tokens.Select(t => t.Type).ToArray();

        Assert.Equal(YamlScannerTokenType.StreamStart, types[0]);
        Assert.Contains(YamlScannerTokenType.BlockMappingStart, types);
        Assert.Contains(YamlScannerTokenType.Key, types);
        Assert.Contains(YamlScannerTokenType.Value, types);
        Assert.Equal(YamlScannerTokenType.StreamEnd, types[^1]);

        var scalars = tokens.Where(t => t.Type == YamlScannerTokenType.Scalar).ToArray();
        Assert.Equal(2, scalars.Length);
        Assert.Equal("key", scalars[0].Value);
        Assert.Equal("value", scalars[1].Value);
    }

    [Fact]
    public void Scanner_Sequence_ProducesCorrectTokens()
    {
        var scanner = new Utf8YamlScanner("- a\n- b\n- c\n");
        var tokens = scanner.ReadAllTokens();
        var types = tokens.Select(t => t.Type).ToArray();

        Assert.Equal(YamlScannerTokenType.StreamStart, types[0]);
        Assert.Contains(YamlScannerTokenType.BlockSequenceStart, types);
        Assert.Equal(3, types.Count(t => t == YamlScannerTokenType.BlockEntry));

        var scalars = tokens.Where(t => t.Type == YamlScannerTokenType.Scalar).ToArray();
        Assert.Equal(3, scalars.Length);
        Assert.Equal("a", scalars[0].Value);
        Assert.Equal("b", scalars[1].Value);
        Assert.Equal("c", scalars[2].Value);
    }

    [Fact]
    public void Scanner_FlowMapping_ProducesCorrectTokens()
    {
        var scanner = new Utf8YamlScanner("{a: 1, b: 2}\n");
        var tokens = scanner.ReadAllTokens();
        var types = tokens.Select(t => t.Type).ToArray();

        Assert.Contains(YamlScannerTokenType.FlowMappingStart, types);
        Assert.Contains(YamlScannerTokenType.FlowMappingEnd, types);
        Assert.Equal(2, types.Count(t => t == YamlScannerTokenType.Key));
        Assert.Equal(2, types.Count(t => t == YamlScannerTokenType.Value));
    }

    [Fact]
    public void Scanner_AnchorAndAlias_ProducesCorrectTokens()
    {
        var scanner = new Utf8YamlScanner("a: &anchor value\nb: *anchor\n");
        var tokens = scanner.ReadAllTokens();

        var anchor = tokens.FirstOrDefault(t => t.Type == YamlScannerTokenType.Anchor);
        var alias = tokens.FirstOrDefault(t => t.Type == YamlScannerTokenType.Alias);

        Assert.Equal("anchor", anchor.Value);
        Assert.Equal("anchor", alias.Value);
    }

    [Fact]
    public void Scanner_BlockScalar_Literal()
    {
        var scanner = new Utf8YamlScanner("text: |\n  line1\n  line2\n");
        var tokens = scanner.ReadAllTokens();

        var scalar = tokens.Last(t => t.Type == YamlScannerTokenType.Scalar);
        Assert.Equal(ScalarStyle.Literal, scalar.Style);
        Assert.Equal("line1\nline2\n", scalar.Value);
    }

    [Fact]
    public void Scanner_BlockScalar_Folded()
    {
        var scanner = new Utf8YamlScanner("text: >\n  line1\n  line2\n");
        var tokens = scanner.ReadAllTokens();

        var scalar = tokens.Last(t => t.Type == YamlScannerTokenType.Scalar);
        Assert.Equal(ScalarStyle.Folded, scalar.Style);
        Assert.Equal("line1 line2\n", scalar.Value);
    }

    [Fact]
    public void Scanner_DoubleQuoted_WithEscapes()
    {
        var scanner = new Utf8YamlScanner("\"hello\\nworld\"\n");
        var tokens = scanner.ReadAllTokens();

        var scalar = tokens.First(t => t.Type == YamlScannerTokenType.Scalar);
        Assert.Equal("hello\nworld", scalar.Value);
        Assert.Equal(ScalarStyle.DoubleQuoted, scalar.Style);
    }

    [Fact]
    public void Scanner_SingleQuoted_WithEscapedQuote()
    {
        var scanner = new Utf8YamlScanner("'it''s'\n");
        var tokens = scanner.ReadAllTokens();

        var scalar = tokens.First(t => t.Type == YamlScannerTokenType.Scalar);
        Assert.Equal("it's", scalar.Value);
    }

    [Fact]
    public void Scanner_DocumentMarkers()
    {
        var scanner = new Utf8YamlScanner("---\nvalue\n...\n");
        var tokens = scanner.ReadAllTokens();
        var types = tokens.Select(t => t.Type).ToArray();

        Assert.Contains(YamlScannerTokenType.DocumentStart, types);
        Assert.Contains(YamlScannerTokenType.DocumentEnd, types);
    }

    [Fact]
    public void Scanner_ExplicitKey()
    {
        var scanner = new Utf8YamlScanner("? key\n: value\n");
        var tokens = scanner.ReadAllTokens();
        var types = tokens.Select(t => t.Type).ToArray();

        Assert.Contains(YamlScannerTokenType.Key, types);
        Assert.Contains(YamlScannerTokenType.Value, types);
    }

    [Fact]
    public void Scanner_NestedMapping()
    {
        var scanner = new Utf8YamlScanner("outer:\n  inner: value\n");
        var tokens = scanner.ReadAllTokens();

        var scalars = tokens.Where(t => t.Type == YamlScannerTokenType.Scalar).Select(t => t.Value).ToArray();
        Assert.Equal(new[] { "outer", "inner", "value" }, scalars);
    }

    [Fact]
    public void Scanner_SimpleKeyResolution_AnchorWithColon()
    {
        // This is the 2SXE case — anchor &a followed by : key separator
        var scanner = new Utf8YamlScanner("&a key: &a value\n");
        var tokens = scanner.ReadAllTokens();

        var anchors = tokens.Where(t => t.Type == YamlScannerTokenType.Anchor).Select(t => t.Value).ToArray();
        Assert.Contains("a", anchors);

        var scalars = tokens.Where(t => t.Type == YamlScannerTokenType.Scalar).Select(t => t.Value).ToArray();
        Assert.Contains("key", scalars);
        Assert.Contains("value", scalars);
    }

    [Fact]
    public void Composer_SimpleMapping_ProducesYamlNode()
    {
        // First verify scanner produces correct tokens
        var scanner = new Utf8YamlScanner("key: value\n");
        var tokens = scanner.ReadAllTokens();
        var tokenTypes = string.Join(", ", tokens.Select(t => t.Type));
        
        // Now test the composer
        var composer = new YamlNodeComposer("key: value\n", default);
        var results = composer.ComposeAll();

        Assert.Single(results);
        Assert.NotNull(results[0].Root);
        Assert.Equal("value", results[0].Root!["key"]!.GetScalarValue());
    }

    [Fact]
    public void Composer_MappingWithSequence_ProducesYamlNode()
    {
        var yaml = "name: Ada\ntags:\n  - yaml\n  - json\n";
        var composer = new YamlNodeComposer(yaml, default);
        var results = composer.ComposeAll();

        Assert.Single(results);
        var root = results[0].Root!;
        Assert.Equal("Ada", root["name"]!.GetScalarValue());
        Assert.Equal(2, root["tags"]!.AsSequence().Count);
    }

    [Fact]
    public void Composer_BlockScalars_ProduceCorrectValues()
    {
        var yaml = "Message: |\n  hello\n  yaml\nSummary: >\n  folded\n  text\n";
        var composer = new YamlNodeComposer(yaml, default);
        var results = composer.ComposeAll();

        Assert.Single(results);
        var root = results[0].Root!;
        Assert.Equal("hello\nyaml\n", root["Message"]!.GetScalarValue());
        Assert.Equal("folded text\n", root["Summary"]!.GetScalarValue());
    }
}
