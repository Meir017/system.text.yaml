namespace System.Text.Yaml;

/// <summary>
/// Configures formatting for <see cref="Utf8YamlWriter"/>.
/// </summary>
public struct YamlWriterOptions
{
    private int _indentationSize;
    private string? _newLine;

    /// <summary>
    /// Gets or sets a value indicating whether block-style indentation should be used.
    /// </summary>
    public bool Indented { get; set; }

    /// <summary>
    /// Gets or sets the number of spaces to use for each indentation level when <see cref="Indented"/> is enabled.
    /// </summary>
    public int IndentationSize
    {
        get => _indentationSize == 0 ? 2 : _indentationSize;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Indentation size must be at least 1.");
            }

            _indentationSize = value;
        }
    }

    /// <summary>
    /// Gets or sets the newline sequence to use when <see cref="Indented"/> is enabled.
    /// </summary>
    public string NewLine
    {
        get => _newLine ?? "\n";
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _newLine = value;
        }
    }
}
