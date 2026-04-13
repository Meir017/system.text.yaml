# System.Text.Yaml

A high-performance YAML 1.2 library for .NET with **zero external dependencies**.

[![CI](https://github.com/Meir017/system.text.yaml/actions/workflows/ci.yml/badge.svg)](https://github.com/Meir017/system.text.yaml/actions/workflows/ci.yml)

## Features

- **100% YAML 1.2.2 spec compliance** — 267/267 [yaml-test-suite](https://github.com/yaml/yaml-test-suite) load cases pass
- **Zero dependencies** — pure .NET, no System.Text.Json or third-party packages in the library
- **Fast** — 2–15× faster than YamlDotNet, competitive with VYaml
- **Familiar API** — `YamlSerializer.Serialize<T>()` / `Deserialize<T>()` just like System.Text.Json
- **Native YAML DOM** — `YamlDocument`, `YamlNode`, `YamlMappingNode`, `YamlSequenceNode`, `YamlScalarNode`
- **Character-level scanner** — libyaml-inspired tokenizer with simple-key tracking
- **All YAML features** — block/flow collections, anchors/aliases, merge keys, tags, multi-document streams, block scalars, complex keys, directives

## Quick Start

### Serialize an object to YAML

```csharp
using System.Text.Yaml;

var config = new AppConfig
{
    Name = "my-service",
    Port = 8080,
    Debug = false,
    Tags = ["web", "api"]
};

string yaml = YamlSerializer.Serialize(config);
```

Output:
```yaml
Name: my-service
Port: 8080
Debug: false
Tags:
  - web
  - api
```

### Deserialize YAML to an object

```csharp
var yaml = """
    Name: my-service
    Port: 8080
    Debug: false
    Tags:
      - web
      - api
    """;

var config = YamlSerializer.Deserialize<AppConfig>(yaml);
```

### Parse YAML into a DOM

```csharp
var doc = YamlDocument.Parse("""
    apiVersion: apps/v1
    kind: Deployment
    metadata:
      name: web
      labels:
        app: web
    spec:
      replicas: 3
    """);

var root = doc.RootNode as YamlMappingNode;
string kind = root!["kind"]!.GetScalarValue();     // "Deployment"
string name = root["metadata"]!["name"]!.GetScalarValue(); // "web"
int replicas = root["spec"]!["replicas"]!.GetScalarInt();  // 3
```

### Multi-document streams

```csharp
var yaml = """
    apiVersion: v1
    kind: Namespace
    metadata:
      name: staging
    ---
    apiVersion: v1
    kind: Service
    metadata:
      name: web
    """;

var results = YamlSerializer.DeserializeAll<Dictionary<string, object>>(yaml);
// results[0]["kind"] == "Namespace"
// results[1]["kind"] == "Service"
```

### Anchors, aliases, and merge keys

```csharp
var yaml = """
    defaults: &defaults
      timeout: 30
      retries: 3

    production:
      <<: *defaults
      retries: 5
    """;

var config = YamlSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(yaml);
// config["production"]["timeout"] == 30  (inherited)
// config["production"]["retries"] == 5   (overridden)
```

### Block scalars

```csharp
var yaml = """
    literal: |
      Line 1
      Line 2
    folded: >
      This is a long
      paragraph.
    """;

var result = YamlSerializer.Deserialize<Dictionary<string, string>>(yaml);
// result["literal"] == "Line 1\nLine 2\n"
// result["folded"]  == "This is a long paragraph.\n"
```

### Configuration options

```csharp
// Custom naming policy
var options = new YamlSerializerOptions
{
    PropertyNamingPolicy = YamlNamingPolicy.CamelCase,
    IndentationSize = 4,
    WriteDocumentMarkers = true,
};

string yaml = YamlSerializer.Serialize(config, options);

// Schema modes
var failsafe = new YamlSerializerOptions { Schema = YamlSchema.Failsafe };
var yaml11 = new YamlSerializerOptions { Schema = YamlSchema.Yaml11 };

// Reject duplicate keys
var strict = new YamlSerializerOptions
{
    DuplicateKeyHandling = YamlDuplicateKeyHandling.Disallow
};
```

### Custom attributes

```csharp
public class ServerConfig
{
    [YamlPropertyName("listen-port")]
    public int Port { get; set; }

    [YamlIgnore]
    public string InternalSecret { get; set; }
}
```

## Performance

Benchmarked on .NET 10 against [YamlDotNet](https://github.com/aaubry/YamlDotNet) and [VYaml](https://github.com/hadashiA/VYaml):

| Scenario | System.Text.Yaml | YamlDotNet | Ratio |
|---|---:|---:|---:|
| **POCO Serialize** | 2.9 μs | 45.0 μs | **15× faster** |
| **POCO Deserialize** | 21.6 μs | 39.1 μs | **1.8× faster** |
| **K8s Deployment** | 59 μs | 169 μs | **2.9× faster** |
| **Large Document (200 entries)** | 876 μs | 2,249 μs | **2.6× faster** |
| **Docker Compose** | 56 μs | 148 μs | **2.6× faster** |
| **Multi-Document (5 K8s resources)** | 44 μs | 122 μs | **2.8× faster** |

Memory allocation is 2–5× lower across all benchmarks.

## YAML Spec Conformance

Tested against the official [yaml-test-suite](https://github.com/yaml/yaml-test-suite):

| Library | Pass Rate |
|---|---|
| **System.Text.Yaml** | **267/267 (100%)** |
| libfyaml (C) | 278/279 (99.6%) |
| YAML::PP (Perl) | 277/279 (99.3%) |
| eemeli/yaml (JS) | 276/279 (98.9%) |
| YamlDotNet (.NET) | 173/249 (69.5%) |

## Architecture

```
YAML string
    ↓
Utf8YamlScanner          Character-level tokenizer (libyaml-inspired)
    ↓
YamlNodeComposer         Token stream → YamlNode tree
    ↓
┌─────────────┬──────────────────┐
│ YamlDocument│  YamlObjectMapper│
│ (DOM access)│  (POCO binding)  │
└─────────────┴──────────────────┘
```

No intermediate `JsonNode` or `JsonElement` — YAML is parsed directly into native `YamlNode` types and mapped to CLR objects via reflection.

## Requirements

- .NET 10+

## License

MIT
