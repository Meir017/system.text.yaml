# Migrating from YamlDotNet to System.Text.Yaml

This guide covers the most common YamlDotNet patterns and their System.Text.Yaml equivalents.

## Serializer & Deserializer Setup

### YamlDotNet (builder pattern)
```csharp
var serializer = new SerializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .Build();

var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .Build();

var yaml = serializer.Serialize(myObject);
var result = deserializer.Deserialize<MyClass>(yaml);
```

### System.Text.Yaml (static API)
```csharp
var options = new YamlSerializerOptions
{
    PropertyNamingPolicy = YamlNamingPolicy.CamelCase
};

var yaml = YamlSerializer.Serialize(myObject, options);
var result = YamlSerializer.Deserialize<MyClass>(yaml, options);
```

No builder pattern needed — pass options directly to the static methods.

---

## Naming Conventions

| YamlDotNet | System.Text.Yaml |
|---|---|
| `CamelCaseNamingConvention.Instance` | `YamlNamingPolicy.CamelCase` |
| `PascalCaseNamingConvention.Instance` | `YamlNamingPolicy.PascalCase` (default) |
| `UnderscoredNamingConvention.Instance` | `YamlNamingPolicy.SnakeCase` |
| `HyphenatedNamingConvention.Instance` | `YamlNamingPolicy.KebabCase` |
| `NullNamingConvention.Instance` | `YamlNamingPolicy.PascalCase` |

---

## Attributes

### YamlDotNet
```csharp
public class Config
{
    [YamlMember(Alias = "server-port")]
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

| YamlDotNet | System.Text.Yaml |
|---|---|
| `[YamlMember(Alias = "name")]` | `[YamlPropertyName("name")]` |
| `[YamlIgnore]` | `[YamlIgnore]` |
| `[YamlMember(ApplyNamingConventions = false)]` | `[YamlPropertyName("ExactName")]` |

---

## Deserialization

### Basic
```csharp
// YamlDotNet
var deserializer = new DeserializerBuilder().Build();
var result = deserializer.Deserialize<MyClass>(yamlString);

// System.Text.Yaml
var result = YamlSerializer.Deserialize<MyClass>(yamlString);
```

### From TextReader / Stream
```csharp
// YamlDotNet
using var reader = new StreamReader("config.yaml");
var result = deserializer.Deserialize<MyClass>(reader);

// System.Text.Yaml
var yaml = File.ReadAllText("config.yaml");
var result = YamlSerializer.Deserialize<MyClass>(yaml);

// Or with UTF-8 bytes (avoids string allocation)
var bytes = File.ReadAllBytes("config.yaml");
var result = YamlSerializer.Deserialize<MyClass>(bytes);
```

### To Dictionary
```csharp
// YamlDotNet
var result = deserializer.Deserialize<Dictionary<string, object>>(yaml);

// System.Text.Yaml (identical)
var result = YamlSerializer.Deserialize<Dictionary<string, object>>(yaml);
```

---

## Serialization

### Basic
```csharp
// YamlDotNet
var serializer = new SerializerBuilder().Build();
var yaml = serializer.Serialize(myObject);

// System.Text.Yaml
var yaml = YamlSerializer.Serialize(myObject);
```

### With document markers
```csharp
// YamlDotNet — always emits document markers by default

// System.Text.Yaml
var yaml = YamlSerializer.Serialize(myObject, new YamlSerializerOptions
{
    WriteDocumentMarkers = true
});
```

---

## DOM / Representation Model

### YamlDotNet
```csharp
var stream = new YamlStream();
stream.Load(new StringReader(yaml));
var doc = stream.Documents[0];
var root = (YamlMappingNode)doc.RootNode;
var name = ((YamlScalarNode)root.Children[new YamlScalarNode("name")]).Value;
```

### System.Text.Yaml
```csharp
var doc = YamlDocument.Parse(yaml);
var root = doc.RootNode as YamlMappingNode;
var name = root!["name"]!.GetScalarValue();
```

Key differences:
- No `YamlStream` — use `YamlDocument.ParseAll()` for multi-document
- String indexer on `YamlMappingNode` — no need to create `YamlScalarNode` keys
- Convenience methods: `GetScalarValue()`, `GetScalarInt()`, `GetScalarBool()`
- `AsSequence()` / `AsMapping()` for type casting

### DOM type mapping

| YamlDotNet | System.Text.Yaml |
|---|---|
| `YamlStream` | `YamlDocument.ParseAll()` |
| `YamlDocument` | `YamlDocument` |
| `YamlMappingNode` | `YamlMappingNode` |
| `YamlSequenceNode` | `YamlSequenceNode` |
| `YamlScalarNode` | `YamlScalarNode` |
| `node.Children[key]` | `node["key"]` |
| `((YamlScalarNode)n).Value` | `n.GetScalarValue()` |

---

## Multi-Document Streams

### YamlDotNet
```csharp
var stream = new YamlStream();
stream.Load(new StringReader(yaml));
foreach (var doc in stream.Documents)
{
    var root = (YamlMappingNode)doc.RootNode;
    // ...
}
```

### System.Text.Yaml
```csharp
// Typed deserialization
var items = YamlSerializer.DeserializeAll<MyClass>(yaml);

// DOM
var docs = YamlDocument.ParseAll(yaml);
foreach (var doc in docs)
{
    var root = doc.RootNode as YamlMappingNode;
    // ...
}
```

---

## Merge Keys & Anchors

Both libraries support anchors, aliases, and merge keys out of the box:

```yaml
defaults: &defaults
  timeout: 30
  retries: 3

production:
  <<: *defaults
  retries: 5
```

```csharp
// System.Text.Yaml — merge keys enabled by default
var result = YamlSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(yaml);
// result["production"]["timeout"] == 30

// Disable merge keys
var options = new YamlSerializerOptions { AllowMergeKeys = false };
```

---

## Schema / Type Resolution

### YamlDotNet
```csharp
// YamlDotNet uses YAML 1.1 schema by default ("yes"/"no" → bool)
```

### System.Text.Yaml
```csharp
// Default: YAML 1.2 Core schema ("true"/"false" only)
var result = YamlSerializer.Deserialize<bool>("true"); // true

// YAML 1.1 compatibility
var options = new YamlSerializerOptions { Schema = YamlSchema.Yaml11 };
var result = YamlSerializer.Deserialize<bool>("yes", options); // true

// Failsafe (everything is a string)
var options = new YamlSerializerOptions { Schema = YamlSchema.Failsafe };
```

---

## Duplicate Key Handling

### YamlDotNet
```csharp
// YamlDotNet throws on duplicate keys by default
```

### System.Text.Yaml
```csharp
// Default: last value wins (silent)
var result = YamlSerializer.Deserialize<Dictionary<string, int>>(yaml);

// Strict mode: reject duplicates
var options = new YamlSerializerOptions
{
    DuplicateKeyHandling = YamlDuplicateKeyHandling.Disallow
};
```

---

## Features Not Yet Available

The following YamlDotNet features are not yet implemented in System.Text.Yaml:

| Feature | Status |
|---|---|
| `IYamlTypeConverter` | Not yet — use `[YamlPropertyName]` for simple cases |
| `IYamlConvertible` | Not yet |
| Custom tag resolution | Tags are preserved but not extensible |
| Emitter-level control | Use `Utf8YamlWriter` for low-level output |
| Source generation | Planned (Roslyn generator for `[YamlSerializable]`) |
