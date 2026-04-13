# Migrating from VYaml to System.Text.Yaml

This guide covers the most common VYaml patterns and their System.Text.Yaml equivalents.

## Serialization & Deserialization

### VYaml
```csharp
using VYaml.Serialization;

// Deserialize (UTF-8 bytes)
var bytes = Encoding.UTF8.GetBytes(yaml);
var result = YamlSerializer.Deserialize<MyClass>(bytes);

// Serialize
var yaml = YamlSerializer.SerializeToString(myObject);
```

### System.Text.Yaml
```csharp
using System.Text.Yaml;

// Deserialize (string or UTF-8 bytes)
var result = YamlSerializer.Deserialize<MyClass>(yaml);
var result = YamlSerializer.Deserialize<MyClass>(utf8Bytes);

// Serialize
var yaml = YamlSerializer.Serialize(myObject);
```

The API shape is nearly identical — both use static `YamlSerializer` methods.

---

## Source Generation

### VYaml
```csharp
[YamlObject]
public partial class Config
{
    [YamlMember("server-port")]
    public int Port { get; set; }

    [YamlIgnore]
    public string Secret { get; set; }
}

// Deserialization uses source-gen automatically
var result = YamlSerializer.Deserialize<Config>(bytes);
```

### System.Text.Yaml
```csharp
public class Config
{
    [YamlPropertyName("server-port")]
    public int Port { get; set; }

    [YamlIgnore]
    public string Secret { get; set; }
}

// Reflection-based (no source-gen yet)
var result = YamlSerializer.Deserialize<Config>(yaml);
```

> **Note**: System.Text.Yaml does not yet have a source generator. The reflection-based mapper uses cached type metadata for good performance. Source generation is planned for a future release.

---

## Naming Conventions

### VYaml
```csharp
[YamlObject(NamingConvention.SnakeCase)]
public partial class Config { ... }
```

### System.Text.Yaml
```csharp
var options = new YamlSerializerOptions
{
    PropertyNamingPolicy = YamlNamingPolicy.SnakeCase
};
var result = YamlSerializer.Deserialize<Config>(yaml, options);
```

| VYaml | System.Text.Yaml |
|---|---|
| `NamingConvention.LowerCamelCase` | `YamlNamingPolicy.CamelCase` |
| `NamingConvention.UpperCamelCase` | `YamlNamingPolicy.PascalCase` |
| `NamingConvention.SnakeCase` | `YamlNamingPolicy.SnakeCase` |
| `NamingConvention.KebabCase` | `YamlNamingPolicy.KebabCase` |

System.Text.Yaml applies naming policy via options rather than per-type attributes.

---

## Attributes

| VYaml | System.Text.Yaml |
|---|---|
| `[YamlObject]` | Not needed (reflection-based) |
| `[YamlMember("name")]` | `[YamlPropertyName("name")]` |
| `[YamlIgnore]` | `[YamlIgnore]` |
| `[YamlConstructor]` | Not yet available |

---

## Multi-Document

### VYaml
```csharp
var results = YamlSerializer.DeserializeMultipleDocuments<MyClass>(bytes);
```

### System.Text.Yaml
```csharp
var results = YamlSerializer.DeserializeAll<MyClass>(yaml);
```

---

## Low-Level Parser

### VYaml
```csharp
var parser = YamlParser.FromBytes(bytes);
while (parser.Read())
{
    switch (parser.CurrentEventType)
    {
        case ParseEventType.MappingStart: ...
        case ParseEventType.Scalar: var value = parser.GetScalarAsString(); ...
    }
}
```

### System.Text.Yaml
```csharp
var reader = new Utf8YamlReader(bytes);
while (reader.Read())
{
    switch (reader.TokenType)
    {
        case YamlTokenType.StartObject: ...
        case YamlTokenType.String: var value = reader.ValueText; ...
    }
}
```

---

## DOM Access

### VYaml
VYaml does not have a DOM model — it's designed for direct deserialization to types.

### System.Text.Yaml
```csharp
var doc = YamlDocument.Parse(yaml);
var root = doc.RootNode as YamlMappingNode;
var name = root!["name"]!.GetScalarValue();
var items = root["list"]!.AsSequence();
```

System.Text.Yaml provides a full DOM (`YamlDocument`, `YamlNode`, `YamlMappingNode`, etc.) which VYaml does not offer.

---

## Schema & Type Resolution

Both VYaml and System.Text.Yaml default to YAML 1.2 Core schema.

```csharp
// System.Text.Yaml supports multiple schemas
var options = new YamlSerializerOptions { Schema = YamlSchema.Core };     // default
var options = new YamlSerializerOptions { Schema = YamlSchema.Yaml11 };   // legacy
var options = new YamlSerializerOptions { Schema = YamlSchema.Failsafe }; // all strings
```

---

## Performance Comparison

System.Text.Yaml is competitive with VYaml on serialization and offers features VYaml doesn't:

| Feature | VYaml | System.Text.Yaml |
|---|---|---|
| POCO serialize | 2.5 μs | 2.9 μs |
| POCO deserialize | 8.8 μs | 21.6 μs |
| Source generation | ✅ | 🔜 Planned |
| DOM model | ❌ | ✅ |
| Anchors/aliases | ❌ | ✅ |
| Merge keys | ❌ | ✅ |
| Multi-document | ✅ | ✅ |
| Block scalars | Limited | ✅ Full |
| Complex keys | ❌ | ✅ |
| YAML 1.2 conformance | ~95% | **100%** (267/267) |

VYaml is faster for raw deserialization due to its zero-copy UTF-8 byte approach and source-generated codecs. System.Text.Yaml offers broader YAML spec support and a richer feature set.

---

## Features Not Yet Available

| Feature | Status |
|---|---|
| Source generation (`[YamlSerializable]`) | Planned |
| Zero-copy UTF-8 parsing | Scanner works on string; UTF-8 span planned |
| Custom formatters/resolvers | Not yet |
