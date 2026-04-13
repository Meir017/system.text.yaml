# Migrating from SharpYaml to System.Text.Yaml

This guide covers the main SharpYaml patterns and their System.Text.Yaml equivalents.

## Overview

SharpYaml has an API design similar to System.Text.Json with static `Serialize`/`Deserialize` methods and an options class. System.Text.Yaml follows the same philosophy, making migration straightforward.

## Serialization & Deserialization

### SharpYaml
```csharp
using SharpYaml.Serialization;

var serializer = new Serializer();
var yaml = serializer.Serialize(myObject);
var result = serializer.Deserialize<MyClass>(yaml);

// Or with settings
var settings = new SerializerSettings
{
    EmitAlias = false,
    IndentSize = 4
};
var serializer = new Serializer(settings);
```

### System.Text.Yaml
```csharp
using System.Text.Yaml;

var yaml = YamlSerializer.Serialize(myObject);
var result = YamlSerializer.Deserialize<MyClass>(yaml);

// With options
var options = new YamlSerializerOptions
{
    IndentationSize = 4
};
var yaml = YamlSerializer.Serialize(myObject, options);
```

Key difference: System.Text.Yaml uses **static methods** — no serializer instance to create.

---

## Options / Settings

| SharpYaml `SerializerSettings` | System.Text.Yaml `YamlSerializerOptions` |
|---|---|
| `IndentSize = 4` | `IndentationSize = 4` |
| `EmitAlias = true/false` | Anchors/aliases handled automatically |
| `EmitDefaultValues = false` | Null values omitted by default |
| `EmitTags = false` | Tags preserved on DOM, not emitted on POCO serialize |
| `DefaultStyle = YamlStyle.Block` | Block style is the default |
| `NamingConvention` | `PropertyNamingPolicy` |

---

## Naming Conventions

### SharpYaml
```csharp
var settings = new SerializerSettings
{
    NamingConvention = new CamelCaseNamingConvention()
};
```

### System.Text.Yaml
```csharp
var options = new YamlSerializerOptions
{
    PropertyNamingPolicy = YamlNamingPolicy.CamelCase
};
```

| SharpYaml | System.Text.Yaml |
|---|---|
| `CamelCaseNamingConvention` | `YamlNamingPolicy.CamelCase` |
| `FlatNamingConvention` | `YamlNamingPolicy.PascalCase` |
| Custom `INamingConvention` | `YamlNamingPolicy` enum (4 built-in policies) |

---

## Attributes

### SharpYaml
```csharp
public class Config
{
    [YamlMember("server-port")]
    public int Port { get; set; }

    [YamlIgnore]
    public string Secret { get; set; }
}
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
```

| SharpYaml | System.Text.Yaml |
|---|---|
| `[YamlMember("name")]` | `[YamlPropertyName("name")]` |
| `[YamlIgnore]` | `[YamlIgnore]` |
| `[YamlTag]` | Tags preserved on DOM nodes |

---

## DOM Model

### SharpYaml
```csharp
var stream = new YamlStream();
stream.Load(yaml);
var doc = stream.Documents[0];
var root = (YamlMappingNode)doc.RootNode;
```

### System.Text.Yaml
```csharp
var doc = YamlDocument.Parse(yaml);
var root = doc.RootNode as YamlMappingNode;
var name = root!["name"]!.GetScalarValue();

// Multi-document
var docs = YamlDocument.ParseAll(yaml);
```

The DOM types share the same names but System.Text.Yaml adds convenience navigation (string indexers, `GetScalarValue()`, etc.).

---

## Type Resolution

### SharpYaml
```csharp
// SharpYaml uses YAML 1.1 compatible resolution by default
```

### System.Text.Yaml
```csharp
// Default: YAML 1.2 Core schema
var options = new YamlSerializerOptions { Schema = YamlSchema.Core };

// YAML 1.1 compatibility (yes/no → bool, etc.)
var options = new YamlSerializerOptions { Schema = YamlSchema.Yaml11 };

// Failsafe (everything is string)
var options = new YamlSerializerOptions { Schema = YamlSchema.Failsafe };
```

---

## Merge Keys & Anchors

Both libraries support YAML anchors and aliases. System.Text.Yaml also supports merge keys (`<<`):

```csharp
var yaml = """
    defaults: &defaults
      timeout: 30
    production:
      <<: *defaults
      retries: 5
    """;

var result = YamlSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(yaml);
// result["production"]["timeout"] == 30 (inherited via merge)
```

---

## Key Differences

| Feature | SharpYaml | System.Text.Yaml |
|---|---|---|
| API style | Instance-based `Serializer` | Static `YamlSerializer` |
| .NET target | .NET Standard 2.0+ | .NET 10+ |
| Dependencies | None | None |
| YAML spec | ~90% | **100%** (267/267) |
| Performance | Moderate | 2–15× faster than YamlDotNet |
| Merge keys | Limited | ✅ Full support |
| Complex keys | ❌ | ✅ |
| Schema selection | Limited | Core, Json, Failsafe, Yaml11 |

---

## Features Not Yet Available

| Feature | Status |
|---|---|
| `IYamlSerializable` (custom converters) | Not yet |
| Polymorphic type handling | Not yet |
| Source generation | Planned |
| Custom tag resolution | Tags preserved but not extensible |
