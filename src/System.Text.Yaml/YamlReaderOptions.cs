namespace System.Text.Yaml;

/// <summary>
/// Configures behavior for <see cref="Utf8YamlReader"/> and the current parser core.
/// </summary>
public struct YamlReaderOptions
{
    private int _maxDepth;
    private int _maxAliasCount;
    private long _maxAliasExpansionNodes;
    private bool? _allowMergeKeys;

    /// <summary>
    /// Gets or sets the maximum nesting depth the parser will accept. A value of 0 uses the default depth of 64.
    /// </summary>
    public int MaxDepth
    {
        readonly get => _maxDepth == 0 ? 64 : _maxDepth;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _maxDepth = value;
        }
    }

    /// <summary>
    /// Gets or sets the schema used for resolving plain scalars.
    /// </summary>
    public YamlSchema Schema { get; set; }

    /// <summary>
    /// Gets or sets how duplicate keys inside a mapping should be handled.
    /// </summary>
    public YamlDuplicateKeyHandling DuplicateKeyHandling { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether YAML merge keys (<c>&lt;&lt;</c>) should be processed.
    /// </summary>
    public bool AllowMergeKeys
    {
        readonly get => _allowMergeKeys ?? true;
        set => _allowMergeKeys = value;
    }

    /// <summary>
    /// Gets or sets the maximum number of aliases that can be resolved while parsing a document.
    /// A value of 0 uses the default limit of 128.
    /// </summary>
    public int MaxAliasCount
    {
        readonly get => _maxAliasCount == 0 ? 128 : _maxAliasCount;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "MaxAliasCount must be at least 1.");
            }

            _maxAliasCount = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum cumulative number of nodes that alias expansion can create.
    /// Limits the "billion laughs" amplification attack. A value of 0 uses the default limit of 10,000.
    /// </summary>
    public long MaxAliasExpansionNodes
    {
        readonly get => _maxAliasExpansionNodes == 0 ? 10_000 : _maxAliasExpansionNodes;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _maxAliasExpansionNodes = value;
        }
    }
}
