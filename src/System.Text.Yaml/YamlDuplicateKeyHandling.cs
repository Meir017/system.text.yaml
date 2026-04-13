namespace System.Text.Yaml;

/// <summary>
/// Controls how duplicate keys inside a YAML mapping are handled.
/// </summary>
public enum YamlDuplicateKeyHandling
{
    /// <summary>
    /// The last occurrence wins, matching the behavior of many YAML implementations.
    /// </summary>
    Replace = 0,

    /// <summary>
    /// Duplicate keys are rejected with a <see cref="YamlException"/>.
    /// </summary>
    Disallow = 1
}
