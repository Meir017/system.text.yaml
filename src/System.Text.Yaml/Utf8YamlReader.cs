using System.Globalization;
using System.Text;

namespace System.Text.Yaml;

/// <summary>
/// A token produced by the Utf8YamlReader.
/// </summary>
public readonly struct YamlReaderToken
{
    public YamlReaderToken(YamlTokenType type, int depth, string? value, string? tag = null, string? anchor = null, string? alias = null)
    {
        TokenType = type;
        Depth = depth;
        Value = value;
        Tag = tag;
        Anchor = anchor;
        Alias = alias;
    }

    public YamlTokenType TokenType { get; }
    public int Depth { get; }
    public string? Value { get; }
    public string? Tag { get; }
    public string? Anchor { get; }
    public string? Alias { get; }
}

/// <summary>
/// Reads UTF-8 YAML input as a forward-only token stream.
/// </summary>
public ref struct Utf8YamlReader
{
    private readonly YamlReaderToken[] _tokens;
    private int _index;

    public Utf8YamlReader(ReadOnlySpan<byte> utf8Yaml, YamlReaderOptions options = default)
    {
        _tokens = Tokenize(Encoding.UTF8.GetString(utf8Yaml), options);
        _index = -1;
        TokenType = YamlTokenType.None;
        CurrentDepth = 0;
        ValueText = null;
        Tag = null;
        Anchor = null;
        Alias = null;
    }

    public YamlTokenType TokenType { get; private set; }
    public int CurrentDepth { get; private set; }
    public string? ValueText { get; private set; }
    public string? Tag { get; private set; }
    public string? Anchor { get; private set; }
    public string? Alias { get; private set; }

    public bool Read()
    {
        _index++;
        if (_index >= _tokens.Length)
        {
            return false;
        }

        var token = _tokens[_index];
        TokenType = token.TokenType;
        CurrentDepth = token.Depth;
        ValueText = token.Value;
        Tag = token.Tag;
        Anchor = token.Anchor;
        Alias = token.Alias;
        return true;
    }

    internal static YamlReaderToken[] Tokenize(string yaml, YamlReaderOptions options)
    {
        var result = YamlParser.ParseToNodeDocument(yaml, options);
        var tokens = new List<YamlReaderToken>();
        AppendTokens(result.Root, 0, tokens);
        return tokens.ToArray();
    }

    private static void AppendTokens(YamlNode? node, int depth, List<YamlReaderToken> tokens)
    {
        if (node is null)
        {
            tokens.Add(new YamlReaderToken(YamlTokenType.Null, depth, null, node?.Tag, node?.Anchor, node?.Alias));
            return;
        }

        if (node is YamlMappingNode map)
        {
            tokens.Add(new YamlReaderToken(YamlTokenType.StartObject, depth, null, map.Tag, map.Anchor, map.Alias));
            foreach (var entry in map.Entries)
            {
                var key = entry.Key is YamlScalarNode s ? s.Value : YamlMappingNode.GetStringKey(entry.Key);
                tokens.Add(new YamlReaderToken(YamlTokenType.PropertyName, depth + 1, key));
                AppendTokens(entry.Value, depth + 1, tokens);
            }
            tokens.Add(new YamlReaderToken(YamlTokenType.EndObject, depth, null));
        }
        else if (node is YamlSequenceNode seq)
        {
            tokens.Add(new YamlReaderToken(YamlTokenType.StartArray, depth, null, seq.Tag, seq.Anchor, seq.Alias));
            foreach (var item in seq.Children)
            {
                AppendTokens(item, depth + 1, tokens);
            }
            tokens.Add(new YamlReaderToken(YamlTokenType.EndArray, depth, null));
        }
        else if (node is YamlScalarNode scalar)
        {
            var tokenType = ClassifyScalar(scalar);
            tokens.Add(new YamlReaderToken(tokenType, depth, scalar.Value, scalar.Tag, scalar.Anchor, scalar.Alias));
        }
    }

    private static YamlTokenType ClassifyScalar(YamlScalarNode scalar)
    {
        if (scalar.IsNull) return YamlTokenType.Null;

        // Explicit !!str tag or quoted/block style → always string
        if (scalar.Style is YamlScalarStyle.SingleQuoted or YamlScalarStyle.DoubleQuoted
            or YamlScalarStyle.Literal or YamlScalarStyle.Folded)
            return YamlTokenType.String;
        if (scalar.Tag is not null && scalar.Tag.EndsWith(":str"))
            return YamlTokenType.String;

        var v = scalar.Value;
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase))
            return YamlTokenType.True;
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase))
            return YamlTokenType.False;
        if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return YamlTokenType.Number;
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || v.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            return YamlTokenType.Number;
        if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            return YamlTokenType.Number;
        if (v is ".inf" or "-.inf" or ".nan" or ".Inf" or "-.Inf" or ".NaN")
            return YamlTokenType.Number;

        return YamlTokenType.String;
    }
}