using System.Globalization;
using System.Text;

namespace System.Text.Yaml;

/// <summary>
/// Represents a node in a YAML document graph.
/// </summary>
public abstract class YamlNode
{
    /// <summary>The YAML tag (e.g., "tag:yaml.org,2002:str", or shorthand like "!!int").</summary>
    public string? Tag { get; set; }

    /// <summary>The anchor name defined on this node (e.g., "anchor" from "&amp;anchor").</summary>
    public string? Anchor { get; set; }

    /// <summary>If this node was produced from an alias, the alias name (e.g., "anchor" from "*anchor").</summary>
    public string? Alias { get; set; }

    /// <summary>The kind of YAML node.</summary>
    public abstract YamlNodeKind Kind { get; }

    internal abstract void WriteTo(StringBuilder sb);

    public override string ToString()
    {
        var sb = new StringBuilder();
        WriteTo(sb);
        return sb.ToString();
    }

    // -----------------------------------------------------------------------
    // Convenience navigation (similar to JsonNode)
    // -----------------------------------------------------------------------

    /// <summary>Index into a mapping node by string key.</summary>
    public virtual YamlNode? this[string key] =>
        throw new InvalidOperationException($"Cannot index a {Kind} node by string key.");

    /// <summary>Index into a sequence node by integer index.</summary>
    public virtual YamlNode? this[int index] =>
        throw new InvalidOperationException($"Cannot index a {Kind} node by integer index.");

    /// <summary>Get the scalar string value. Throws if not a scalar.</summary>
    public string GetScalarValue() => this is YamlScalarNode s ? s.Value
        : throw new InvalidOperationException($"Cannot get scalar value from a {Kind} node.");

    /// <summary>Get the scalar value parsed as int.</summary>
    public int GetScalarInt() => int.Parse(GetScalarValue(), Globalization.CultureInfo.InvariantCulture);

    /// <summary>Get the scalar value parsed as long.</summary>
    public long GetScalarLong()
    {
        var v = GetScalarValue();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.Parse(v.AsSpan(2), Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture);
        if (v.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt64(v[2..], 8);
        return long.Parse(v, Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Get the scalar value parsed as bool.</summary>
    public bool GetScalarBool()
    {
        var v = GetScalarValue();
        if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        throw new InvalidOperationException($"Cannot parse '{v}' as boolean.");
    }

    /// <summary>Cast to YamlSequenceNode.</summary>
    public YamlSequenceNode AsSequence() => this as YamlSequenceNode
        ?? throw new InvalidOperationException($"Cannot cast {Kind} to Sequence.");

    /// <summary>Cast to YamlMappingNode.</summary>
    public YamlMappingNode AsMapping() => this as YamlMappingNode
        ?? throw new InvalidOperationException($"Cannot cast {Kind} to Mapping.");
}

/// <summary>
/// A YAML scalar node (string, number, boolean, null, etc.).
/// The raw value is always stored as a <see cref="string"/>; type resolution is performed separately.
/// </summary>
public sealed class YamlScalarNode : YamlNode
{
    public YamlScalarNode(string value, YamlScalarStyle style = YamlScalarStyle.Plain)
    {
        Value = value;
        Style = style;
    }

    /// <summary>The raw string value of the scalar.</summary>
    public string Value { get; }

    /// <summary>The presentation style used in the source document.</summary>
    public YamlScalarStyle Style { get; }

    public override YamlNodeKind Kind => YamlNodeKind.Scalar;

    /// <summary>Returns true if this scalar represents a YAML null value.</summary>
    public bool IsNull => Tag is null && Style == YamlScalarStyle.Plain &&
        (Value.Length == 0 || Value == "~" || Value.Equals("null", StringComparison.OrdinalIgnoreCase));

    internal override void WriteTo(StringBuilder sb)
    {
        if (Style == YamlScalarStyle.DoubleQuoted)
            sb.Append('"').Append(Value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
        else if (Style == YamlScalarStyle.SingleQuoted)
            sb.Append('\'').Append(Value.Replace("'", "''")).Append('\'');
        else
            sb.Append(Value);
    }
}

/// <summary>
/// A YAML sequence node (ordered list of nodes).
/// </summary>
public sealed class YamlSequenceNode : YamlNode, System.Collections.Generic.IEnumerable<YamlNode?>
{
    public YamlSequenceNode() { }
    public YamlSequenceNode(IEnumerable<YamlNode?> items)
    {
        foreach (var item in items) Children.Add(item);
    }

    /// <summary>The ordered child nodes.</summary>
    public List<YamlNode?> Children { get; } = new();

    public int Count => Children.Count;
    public override YamlNode? this[int index] => Children[index];

    public override YamlNodeKind Kind => YamlNodeKind.Sequence;

    public void Add(YamlNode? node) => Children.Add(node);

    public System.Collections.Generic.IEnumerator<YamlNode?> GetEnumerator() => Children.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    internal override void WriteTo(StringBuilder sb)
    {
        sb.Append('[');
        for (var i = 0; i < Children.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            if (Children[i] is not null) Children[i]!.WriteTo(sb);
            else sb.Append("null");
        }
        sb.Append(']');
    }
}

/// <summary>
/// A YAML mapping node (ordered collection of key-value pairs where keys are also YAML nodes).
/// </summary>
public sealed class YamlMappingNode : YamlNode
{
    /// <summary>The ordered key-value entries. Keys are YamlNode to support complex keys.</summary>
    public List<KeyValuePair<YamlNode, YamlNode?>> Entries { get; } = new();

    /// <summary>Whether this mapping contains complex (non-scalar) keys.</summary>
    public bool HasComplexKeys { get; internal set; }

    public int Count => Entries.Count;

    public override YamlNodeKind Kind => YamlNodeKind.Mapping;

    public void Add(YamlNode key, YamlNode? value) =>
        Entries.Add(new KeyValuePair<YamlNode, YamlNode?>(key, value));

    public void Add(string key, YamlNode? value) =>
        Add(new YamlScalarNode(key), value);

    /// <summary>
    /// Look up a value by string key. Returns null if not found.
    /// </summary>
    public override YamlNode? this[string key]
    {
        get
        {
            foreach (var entry in Entries)
            {
                if (entry.Key is YamlScalarNode scalar && scalar.Value == key)
                    return entry.Value;
            }
            return null;
        }
    }

    /// <summary>Try to find a value by string key.</summary>
    public bool TryGetValue(string key, out YamlNode? value)
    {
        foreach (var entry in Entries)
        {
            if (entry.Key is YamlScalarNode scalar && scalar.Value == key)
            {
                value = entry.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>Check if the mapping contains a string key.</summary>
    public bool ContainsKey(string key)
    {
        foreach (var entry in Entries)
        {
            if (entry.Key is YamlScalarNode scalar && scalar.Value == key)
                return true;
        }
        return false;
    }

    /// <summary>Gets the string key for an entry, projecting complex keys to a stable name.</summary>
    public static string GetStringKey(YamlNode keyNode)
    {
        if (keyNode is YamlScalarNode scalar)
            return scalar.Value;
        // Complex keys: use a deterministic string representation
        return keyNode.ToString();
    }

    internal override void WriteTo(StringBuilder sb)
    {
        sb.Append('{');
        for (var i = 0; i < Entries.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            Entries[i].Key.WriteTo(sb);
            sb.Append(": ");
            if (Entries[i].Value is not null) Entries[i].Value!.WriteTo(sb);
            else sb.Append("null");
        }
        sb.Append('}');
    }
}

/// <summary>Describes the kind of a <see cref="YamlNode"/>.</summary>
public enum YamlNodeKind
{
    Scalar,
    Sequence,
    Mapping,
}

/// <summary>Presentation style of a YAML scalar in the source document.</summary>
public enum YamlScalarStyle
{
    /// <summary>No quoting (e.g., <c>value</c>).</summary>
    Plain,
    /// <summary>Single-quoted (e.g., <c>'value'</c>).</summary>
    SingleQuoted,
    /// <summary>Double-quoted (e.g., <c>"value"</c>).</summary>
    DoubleQuoted,
    /// <summary>Literal block scalar (<c>|</c>).</summary>
    Literal,
    /// <summary>Folded block scalar (<c>&gt;</c>).</summary>
    Folded,
}
