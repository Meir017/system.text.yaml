namespace System.Text.Yaml;

/// <summary>
/// Token types produced by the character-level YAML scanner.
/// These are low-level tokens that the parser/composer consumes to build the DOM.
/// Modeled after libyaml's token types.
/// </summary>
internal enum YamlScannerTokenType
{
    StreamStart,
    StreamEnd,
    VersionDirective,
    TagDirective,
    DocumentStart,
    DocumentEnd,
    BlockSequenceStart,
    BlockMappingStart,
    BlockEnd,
    FlowSequenceStart,
    FlowSequenceEnd,
    FlowMappingStart,
    FlowMappingEnd,
    BlockEntry,
    FlowEntry,
    Key,
    Value,
    Anchor,
    Alias,
    Tag,
    Scalar,
}

/// <summary>
/// A token produced by the scanner with its metadata.
/// </summary>
internal readonly struct YamlScannerToken
{
    public YamlScannerToken(YamlScannerTokenType type, YamlMark start, YamlMark end, string? value = null, ScalarStyle style = ScalarStyle.Plain)
    {
        Type = type;
        Start = start;
        End = end;
        Value = value;
        Style = style;
    }

    public YamlScannerTokenType Type { get; }
    public YamlMark Start { get; }
    public YamlMark End { get; }
    public string? Value { get; }
    public ScalarStyle Style { get; }
}

/// <summary>
/// A position in the YAML input stream.
/// </summary>
internal readonly struct YamlMark
{
    public YamlMark(int index, int line, int column)
    {
        Index = index;
        Line = line;
        Column = column;
    }

    public int Index { get; }
    public int Line { get; }
    public int Column { get; }

    public override string ToString() => $"line {Line + 1}, column {Column + 1}";
}

/// <summary>
/// Scalar presentation style.
/// </summary>
internal enum ScalarStyle
{
    Plain,
    SingleQuoted,
    DoubleQuoted,
    Literal,
    Folded
}

/// <summary>
/// Tracks a potential simple key in the scanner.
/// A simple key is a key that can appear without the '?' indicator.
/// </summary>
internal struct SimpleKeyState
{
    public bool IsPossible;
    public bool IsRequired;
    public int TokenNumber;
    public YamlMark Mark;

    public static SimpleKeyState Impossible => new() { IsPossible = false };
}
