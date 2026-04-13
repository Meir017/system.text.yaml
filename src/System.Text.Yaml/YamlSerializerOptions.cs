namespace System.Text.Yaml;

/// <summary>
/// Configures <see cref="YamlSerializer"/> behavior.
/// </summary>
public sealed class YamlSerializerOptions
{
    private int _indentationSize = 2;
    private string _newLine = "\n";
    private int _maxDepth = 64;
    private int _maxAliasCount = 128;

    /// <summary>
    /// Controls how many spaces are emitted per indentation level for block-style YAML output.
    /// </summary>
    public int IndentationSize
    {
        get => _indentationSize;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Indentation size must be at least 1.");
            _indentationSize = value;
        }
    }

    /// <summary>
    /// Controls the newline sequence emitted for block-style YAML output.
    /// </summary>
    public string NewLine
    {
        get => _newLine;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _newLine = value;
        }
    }

    /// <summary>
    /// Controls whether document markers (<c>---</c> / <c>...</c>) are emitted.
    /// </summary>
    public bool WriteDocumentMarkers { get; set; }

    /// <summary>
    /// Gets or sets the schema used for resolving plain YAML scalars during deserialization.
    /// </summary>
    public YamlSchema Schema { get; set; } = YamlSchema.Core;

    /// <summary>
    /// Gets or sets how duplicate YAML mapping keys are handled during deserialization.
    /// </summary>
    public YamlDuplicateKeyHandling DuplicateKeyHandling { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether merge keys (<c>&lt;&lt;</c>) should be processed.
    /// </summary>
    public bool AllowMergeKeys { get; set; } = true;

    /// <summary>
    /// Gets or sets the naming policy for property name mapping during serialization/deserialization.
    /// </summary>
    public YamlNamingPolicy PropertyNamingPolicy { get; set; } = YamlNamingPolicy.PascalCase;

    /// <summary>
    /// Gets or sets the maximum supported nesting depth.
    /// </summary>
    public int MaxDepth
    {
        get => _maxDepth;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum depth must be at least 1.");
            _maxDepth = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of aliases that can be resolved while parsing a document.
    /// </summary>
    public int MaxAliasCount
    {
        get => _maxAliasCount;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum alias count cannot be negative.");
            _maxAliasCount = value;
        }
    }

    internal YamlReaderOptions CreateReaderOptions()
        => new()
        {
            Schema = Schema,
            DuplicateKeyHandling = DuplicateKeyHandling,
            AllowMergeKeys = AllowMergeKeys,
            MaxDepth = MaxDepth,
            MaxAliasCount = MaxAliasCount
        };
}