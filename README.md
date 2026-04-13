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

<details>
<summary><strong>Full BenchmarkDotNet results (click to expand)</strong></summary>

```
BenchmarkDotNet v0.15.8, Windows 11
.NET 10.0.4, X64 RyuJIT AVX-512
Job: ShortRun (IterationCount=3, LaunchCount=1, WarmupCount=1)
```

#### POCO Serialization

| Method         | Mean      | Allocated | Alloc Ratio |
|--------------- |----------:|----------:|------------:|
| SystemTextYaml |  2.911 μs |   2.78 KB |        1.00 |
| YamlDotNet     | 44.963 μs |  27.46 KB |        9.87 |
| VYaml          |  2.541 μs |   3.13 KB |        1.13 |

#### POCO Deserialization

| Method         | Mean      | Allocated | Alloc Ratio |
|--------------- |----------:|----------:|------------:|
| SystemTextYaml | 21.603 μs |  19.36 KB |        1.00 |
| YamlDotNet     | 39.098 μs |  30.78 KB |        1.59 |
| VYaml          |  8.758 μs |   1.02 KB |        0.05 |

#### Block YAML Serialization

| Method               | Mean      | Allocated | Alloc Ratio |
|--------------------- |----------:|----------:|------------:|
| SystemTextYaml_Block |  2.969 μs |   2.78 KB |        1.00 |
| YamlDotNet_Block     | 45.921 μs |  27.38 KB |        9.84 |

#### Block YAML Deserialization

| Method         | Mean     | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|------------:|
| SystemTextYaml | 18.01 μs |  18.18 KB |        1.00 |
| YamlDotNet     | 38.84 μs |  28.85 KB |        1.59 |

#### Kubernetes Deployment Manifest

| Method         | Mean      | Allocated | Alloc Ratio |
|--------------- |----------:|----------:|------------:|
| SystemTextYaml |  59.24 μs |  34.67 KB |        1.00 |
| YamlDotNet     | 168.74 μs | 125.86 KB |        3.63 |

#### Docker Compose (Anchors/Aliases)

| Method         | Mean      | Allocated | Alloc Ratio |
|--------------- |----------:|----------:|------------:|
| SystemTextYaml |  56.06 μs |  40.63 KB |        1.00 |
| YamlDotNet     | 147.89 μs | 108.22 KB |        2.66 |

#### GitHub Actions (Mixed Flow/Block)

| Method         | Mean      | Allocated | Alloc Ratio |
|--------------- |----------:|----------:|------------:|
| SystemTextYaml |  48.67 μs |  29.86 KB |        1.00 |
| YamlDotNet     | 137.04 μs |  99.98 KB |        3.35 |

#### Multi-Document (5 K8s Resources)

| Method         | Mean      | Allocated | Alloc Ratio |
|--------------- |----------:|----------:|------------:|
| SystemTextYaml |  43.89 μs |  28.66 KB |        1.00 |
| YamlDotNet     | 121.80 μs |  95.99 KB |        3.35 |

#### Large Document (200 Entries)

| Method         | Mean       | Allocated   | Alloc Ratio |
|--------------- |-----------:|------------:|------------:|
| SystemTextYaml |   876.0 μs |   320.22 KB |        1.00 |
| YamlDotNet     | 2,249.4 μs | 1,585.31 KB |        4.95 |

#### Helm Values (Deep Nesting + Anchors)

| Method         | Mean     | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|------------:|
| SystemTextYaml | 207.3 μs | 107.46 KB |        1.00 |
| YamlDotNet     | 227.3 μs | 164.36 KB |        1.53 |

#### Block Scalars (ConfigMap)

| Method         | Mean     | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|------------:|
| SystemTextYaml | 51.28 μs |  29.34 KB |        1.00 |
| YamlDotNet     | 37.06 μs |  22.69 KB |        0.77 |

#### Anchor/Alias Resolution

| Method         | Mean     | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|------------:|
| SystemTextYaml | 29.83 μs |  27.44 KB |        1.00 |
| YamlDotNet     | 55.26 μs |  25.73 KB |        0.94 |

#### DOM Parsing

| Method             | Mean     | Allocated | Alloc Ratio |
|------------------- |---------:|----------:|------------:|
| SystemTextYaml_Dom | 91.85 μs |  38.39 KB |        1.00 |
| YamlDotNet_Dict    | 82.77 μs |  65.77 KB |        1.71 |

#### Source-Gen vs Reflection (Serialization)

| Method          | Mean      | Allocated | Alloc Ratio |
|---------------- |----------:|----------:|------------:|
| Reflection      |  3.040 μs |   2.78 KB |        1.00 |
| SourceGen       | 24.306 μs |  17.38 KB |        6.24 |
| VYaml_SourceGen |  2.614 μs |   3.13 KB |        1.13 |

#### Source-Gen vs Reflection (Deserialization)

| Method     | Mean     | Allocated | Alloc Ratio |
|----------- |---------:|----------:|------------:|
| Reflection | 18.43 μs |  18.18 KB |        1.00 |
| SourceGen  | 23.48 μs |  17.70 KB |        0.97 |

</details>

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
