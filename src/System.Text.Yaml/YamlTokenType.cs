namespace System.Text.Yaml;

/// <summary>
/// Represents the token kinds returned by <see cref="Utf8YamlReader"/>.
/// </summary>
public enum YamlTokenType
{
    None = 0,
    StartObject,
    EndObject,
    StartArray,
    EndArray,
    PropertyName,
    String,
    Number,
    True,
    False,
    Null
}
