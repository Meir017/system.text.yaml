namespace System.Text.Yaml;

/// <summary>
/// Determines the naming convention used when mapping YAML keys to CLR property names.
/// </summary>
public enum YamlNamingPolicy
{
    /// <summary>Properties map to PascalCase keys (e.g., <c>MyProperty</c>). This is the default.</summary>
    PascalCase,
    /// <summary>Properties map to camelCase keys (e.g., <c>myProperty</c>).</summary>
    CamelCase,
    /// <summary>Properties map to snake_case keys (e.g., <c>my_property</c>).</summary>
    SnakeCase,
    /// <summary>Properties map to kebab-case keys (e.g., <c>my-property</c>).</summary>
    KebabCase,
}
