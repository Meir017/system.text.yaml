namespace System.Text.Yaml;

/// <summary>
/// Controls how plain YAML scalars are resolved when no explicit tag is provided.
/// </summary>
public enum YamlSchema
{
    /// <summary>
    /// YAML 1.2 core schema resolution for common configuration-style documents.
    /// </summary>
    Core = 0,

    /// <summary>
    /// JSON-compatible resolution for booleans, null, and numbers.
    /// </summary>
    Json = 1,

    /// <summary>
    /// Failsafe schema where plain scalars remain strings unless explicitly tagged.
    /// </summary>
    Failsafe = 2,

    /// <summary>
    /// A compatibility mode that accepts common YAML 1.1 legacy scalars such as yes/no/on/off.
    /// </summary>
    Yaml11 = 3
}
