namespace System.Text.Yaml;

/// <summary>
/// Represents errors encountered while reading or writing YAML content.
/// </summary>
public sealed class YamlException : Exception
{
    public YamlException(string message)
        : base(message)
    {
    }

    public YamlException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
