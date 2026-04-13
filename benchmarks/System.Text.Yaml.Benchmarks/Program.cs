using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SystemTextYamlSerializer = System.Text.Yaml.YamlSerializer;
using SystemTextYamlDocument = System.Text.Yaml.YamlDocument;
using SystemTextYamlSerializerOptions = System.Text.Yaml.YamlSerializerOptions;
using VYamlSerializer = VYaml.Serialization.YamlSerializer;
using VYaml.Annotations;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

// ---------------------------------------------------------------------------
// Benchmark 1: POCO serialization (JSON-compatible output)
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class PocoSerializationBenchmarks
{
    private readonly SampleDocument _value = SampleDocument.Create();
    private readonly ISerializer _yamlDotNetSerializer = new SerializerBuilder()
        .JsonCompatible()
        .Build();

    [Benchmark(Baseline = true)]
    public string SystemTextYaml() => SystemTextYamlSerializer.Serialize(_value);

    [Benchmark]
    public string YamlDotNet() => _yamlDotNetSerializer.Serialize(_value);

    [Benchmark]
    public string VYaml() => VYamlSerializer.SerializeToString(_value);
}

// ---------------------------------------------------------------------------
// Benchmark 2: POCO deserialization from JSON-subset YAML
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class PocoDeserializationBenchmarks
{
    private readonly string _yaml = SampleDocument.JsonSubsetYaml;
    private readonly byte[] _yamlUtf8 = Encoding.UTF8.GetBytes(SampleDocument.JsonSubsetYaml);
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public SampleDocument? SystemTextYaml() => SystemTextYamlSerializer.Deserialize<SampleDocument>(_yamlUtf8);

    [Benchmark]
    public SampleDocument YamlDotNet() => _yamlDotNetDeserializer.Deserialize<SampleDocument>(_yaml);

    [Benchmark]
    public SampleDocument VYaml() => VYamlSerializer.Deserialize<SampleDocument>(_yamlUtf8);
}

// ---------------------------------------------------------------------------
// Benchmark 3: Block YAML serialization (idiomatic YAML output)
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class BlockYamlSerializationBenchmarks
{
    private readonly SampleDocument _value = SampleDocument.Create();
    private readonly ISerializer _yamlDotNetBlockSerializer = new SerializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .Build();

    [Benchmark(Baseline = true)]
    public string SystemTextYaml_Block() => SystemTextYamlSerializer.Serialize(_value);

    [Benchmark]
    public string YamlDotNet_Block() => _yamlDotNetBlockSerializer.Serialize(_value);
}

// ---------------------------------------------------------------------------
// Benchmark 4: Block YAML deserialization (idiomatic YAML input)
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class BlockYamlDeserializationBenchmarks
{
    private const string BlockYaml = """
        Id: catalog-2026-04
        Version: 3
        IsPublished: true
        Maintainer:
          Name: Avery Lee
          Email: avery@example.com
        Packages:
          - Name: System.Text.Yaml.Core
            Downloads: 12400
            Tags:
              - yaml
              - json
              - serializer
          - Name: System.Text.Yaml.Analyzers
            Downloads: 2750
            Tags:
              - roslyn
              - analyzers
        """;

    private readonly string _blockYaml = BlockYaml;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder()
        .WithNamingConvention(PascalCaseNamingConvention.Instance)
        .Build();

    [Benchmark(Baseline = true)]
    public SampleDocument? SystemTextYaml() => SystemTextYamlSerializer.Deserialize<SampleDocument>(_blockYaml);

    [Benchmark]
    public SampleDocument YamlDotNet() => _yamlDotNetDeserializer.Deserialize<SampleDocument>(_blockYaml);
}

// ---------------------------------------------------------------------------
// Benchmark 5: Read-only DOM parsing
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class DomParsingBenchmarks
{
    private const string ComplexYaml = """
        server:
          host: localhost
          port: 8080
          ssl:
            enabled: true
            cert: /etc/ssl/cert.pem
            key: /etc/ssl/key.pem
          timeouts:
            read: 30
            write: 60
            idle: 120
        database:
          primary:
            host: db-primary.internal
            port: 5432
            name: myapp_production
            pool:
              min: 5
              max: 20
          replicas:
            - host: db-replica-1.internal
              port: 5432
            - host: db-replica-2.internal
              port: 5432
        logging:
          level: info
          outputs:
            - type: console
              format: json
            - type: file
              path: /var/log/app.log
              rotate: daily
        features:
          enable_caching: true
          enable_metrics: true
          experimental:
            - dark_mode
            - ai_suggestions
            - real_time_sync
        """;

    private readonly string _yaml = ComplexYaml;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public object SystemTextYaml_Dom() => SystemTextYamlDocument.Parse(_yaml);

    [Benchmark]
    public object YamlDotNet_Dict() => _yamlDotNetDeserializer.Deserialize<Dictionary<string, object>>(_yaml)!;
}

// ---------------------------------------------------------------------------
// Benchmark 6: Large document deserialization
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class LargeDocumentBenchmarks
{
    private string _largeYaml = string.Empty;
    private byte[] _largeYamlUtf8 = [];
    private IDeserializer _yamlDotNetDeserializer = null!;

    [GlobalSetup]
    public void Setup()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 200; i++)
        {
            sb.AppendLine($"item_{i}:");
            sb.AppendLine($"  name: \"Item {i}\"");
            sb.AppendLine($"  value: {i * 17}");
            sb.AppendLine($"  enabled: {(i % 2 == 0 ? "true" : "false")}");
            sb.AppendLine($"  tags:");
            sb.AppendLine($"    - tag_{i}_a");
            sb.AppendLine($"    - tag_{i}_b");
        }

        _largeYaml = sb.ToString();
        _largeYamlUtf8 = Encoding.UTF8.GetBytes(_largeYaml);
        _yamlDotNetDeserializer = new DeserializerBuilder().Build();
    }

    [Benchmark(Baseline = true)]
    public object SystemTextYaml() => SystemTextYamlSerializer.Deserialize<Dictionary<string, object>>(_largeYaml)!;

    [Benchmark]
    public object YamlDotNet() => _yamlDotNetDeserializer.Deserialize<Dictionary<string, object>>(_largeYaml)!;
}

// ---------------------------------------------------------------------------
// Benchmark 7: Anchor/alias resolution
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class AnchorAliasBenchmarks
{
    private const string YamlWithAnchors = """
        defaults: &defaults
          adapter: postgres
          host: localhost
          port: 5432

        development:
          database: myapp_dev
          <<: *defaults

        staging:
          database: myapp_staging
          <<: *defaults

        production:
          database: myapp_prod
          <<: *defaults
          host: db.example.com
        """;

    private readonly string _yaml = YamlWithAnchors;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public object SystemTextYaml() => SystemTextYamlDocument.Parse(_yaml);

    [Benchmark]
    public object YamlDotNet() => _yamlDotNetDeserializer.Deserialize<Dictionary<string, object>>(_yaml)!;
}

// ---------------------------------------------------------------------------
// Shared model types
// ---------------------------------------------------------------------------
[YamlObject(NamingConvention.UpperCamelCase)]
public sealed partial class SampleDocument
{
    public string Id { get; set; } = string.Empty;

    public int Version { get; set; }

    public bool IsPublished { get; set; }

    public Maintainer Maintainer { get; set; } = new();

    public List<Package> Packages { get; set; } = new();

    public static string JsonSubsetYaml =>
        """
        {
          "Id": "catalog-2026-04",
          "Version": 3,
          "IsPublished": true,
          "Maintainer": {
            "Name": "Avery Lee",
            "Email": "avery@example.com"
          },
          "Packages": [
            {
              "Name": "System.Text.Yaml.Core",
              "Downloads": 12400,
              "Tags": ["yaml", "json", "serializer"]
            },
            {
              "Name": "System.Text.Yaml.Analyzers",
              "Downloads": 2750,
              "Tags": ["roslyn", "analyzers"]
            }
          ]
        }
        """;

    public static SampleDocument Create() =>
        new()
        {
            Id = "catalog-2026-04",
            Version = 3,
            IsPublished = true,
            Maintainer = new Maintainer
            {
                Name = "Avery Lee",
                Email = "avery@example.com"
            },
            Packages =
            [
                new Package
                {
                    Name = "System.Text.Yaml.Core",
                    Downloads = 12400,
                    Tags = ["yaml", "json", "serializer"]
                },
                new Package
                {
                    Name = "System.Text.Yaml.Analyzers",
                    Downloads = 2750,
                    Tags = ["roslyn", "analyzers"]
                }
            ]
        };
}

[YamlObject(NamingConvention.UpperCamelCase)]
public sealed partial class Maintainer
{
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}

[YamlObject(NamingConvention.UpperCamelCase)]
public sealed partial class Package
{
    public string Name { get; set; } = string.Empty;

    public int Downloads { get; set; }

    public List<string> Tags { get; set; } = new();
}

// ---------------------------------------------------------------------------
// Source-generated context for System.Text.Yaml
// ---------------------------------------------------------------------------
[JsonSerializable(typeof(SampleDocument))]
internal partial class BenchmarkYamlContext : System.Text.Yaml.YamlSerializerContext
{
}

// ---------------------------------------------------------------------------
// Benchmark 8: Source-gen vs reflection serialization
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class SourceGenVsReflectionBenchmarks
{
    private readonly SampleDocument _value = SampleDocument.Create();
    private readonly JsonTypeInfo<SampleDocument> _typeInfo = BenchmarkYamlContext.Default.SampleDocument;

    [Benchmark(Baseline = true)]
    public string Reflection() => SystemTextYamlSerializer.Serialize(_value);

    [Benchmark]
    public string SourceGen() => SystemTextYamlSerializer.Serialize(_value, _typeInfo);

    [Benchmark]
    public string VYaml_SourceGen() => VYamlSerializer.SerializeToString(_value);
}

// ---------------------------------------------------------------------------
// Benchmark 9: Source-gen vs reflection deserialization
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class SourceGenVsReflectionDeserializationBenchmarks
{
    private const string BlockYaml = """
        Id: catalog-2026-04
        Version: 3
        IsPublished: true
        Maintainer:
          Name: Avery Lee
          Email: avery@example.com
        Packages:
          - Name: System.Text.Yaml.Core
            Downloads: 12400
            Tags:
              - yaml
              - json
              - serializer
          - Name: System.Text.Yaml.Analyzers
            Downloads: 2750
            Tags:
              - roslyn
              - analyzers
        """;

    private readonly string _yaml = BlockYaml;
    private readonly JsonTypeInfo<SampleDocument> _typeInfo = BenchmarkYamlContext.Default.SampleDocument;

    [Benchmark(Baseline = true)]
    public SampleDocument? Reflection() => SystemTextYamlSerializer.Deserialize<SampleDocument>(_yaml);

    [Benchmark]
    public SampleDocument? SourceGen() => SystemTextYamlSerializer.Deserialize(_yaml, _typeInfo);
}

// ---------------------------------------------------------------------------
// Benchmark 10: Kubernetes Deployment manifest (real-world complex YAML)
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class KubernetesDeploymentBenchmarks
{
    private const string K8sDeploymentYaml = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: web-frontend
          namespace: production
          labels:
            app: web-frontend
            tier: frontend
            release: stable
          annotations:
            deployment.kubernetes.io/revision: "3"
        spec:
          replicas: 3
          revisionHistoryLimit: 10
          selector:
            matchLabels:
              app: web-frontend
          strategy:
            type: RollingUpdate
            rollingUpdate:
              maxSurge: 1
              maxUnavailable: 0
          template:
            metadata:
              labels:
                app: web-frontend
                tier: frontend
            spec:
              serviceAccountName: web-frontend
              terminationGracePeriodSeconds: 30
              containers:
              - name: web
                image: registry.example.com/web:v2.1.0
                ports:
                - containerPort: 8080
                  protocol: TCP
                env:
                - name: DATABASE_URL
                  value: "postgres://db:5432/app"
                - name: LOG_LEVEL
                  value: "info"
                resources:
                  requests:
                    cpu: 250m
                    memory: 256Mi
                  limits:
                    cpu: "1"
                    memory: 512Mi
                livenessProbe:
                  httpGet:
                    path: /healthz
                    port: 8080
                  initialDelaySeconds: 15
                  periodSeconds: 10
                readinessProbe:
                  httpGet:
                    path: /ready
                    port: 8080
                  initialDelaySeconds: 5
                  periodSeconds: 5
                volumeMounts:
                - name: config-volume
                  mountPath: /etc/config
                  readOnly: true
              - name: sidecar
                image: envoyproxy/envoy:v1.28
                ports:
                - containerPort: 9901
              volumes:
              - name: config-volume
                configMap:
                  name: web-config
              nodeSelector:
                kubernetes.io/os: linux
              tolerations:
              - key: dedicated
                operator: Equal
                value: frontend
                effect: NoSchedule
        """;

    private readonly string _yaml = K8sDeploymentYaml;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public object SystemTextYaml() => SystemTextYamlSerializer.Deserialize<Dictionary<string, object>>(_yaml)!;

    [Benchmark]
    public object YamlDotNet() => _yamlDotNetDeserializer.Deserialize<Dictionary<string, object>>(_yaml)!;
}

// ---------------------------------------------------------------------------
// Benchmark 11: Multi-document Kubernetes manifests
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class MultiDocumentBenchmarks
{
    private const string MultiDocYaml = """
        apiVersion: v1
        kind: Namespace
        metadata:
          name: staging
          labels:
            env: staging
        ---
        apiVersion: v1
        kind: ServiceAccount
        metadata:
          name: app-sa
          namespace: staging
        ---
        apiVersion: v1
        kind: ConfigMap
        metadata:
          name: app-config
          namespace: staging
        data:
          LOG_LEVEL: info
          MAX_CONNECTIONS: "100"
          FEATURES: "cache,metrics,tracing"
        ---
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: api-server
          namespace: staging
        spec:
          replicas: 2
          selector:
            matchLabels:
              app: api-server
          template:
            metadata:
              labels:
                app: api-server
            spec:
              serviceAccountName: app-sa
              containers:
              - name: api
                image: myapp/api:v1.5
                ports:
                - containerPort: 8080
        ---
        apiVersion: v1
        kind: Service
        metadata:
          name: api-server
          namespace: staging
        spec:
          type: ClusterIP
          selector:
            app: api-server
          ports:
          - port: 80
            targetPort: 8080
        """;

    private readonly string _yaml = MultiDocYaml;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public object SystemTextYaml() => SystemTextYamlSerializer.DeserializeAll<object?>(_yaml);

    [Benchmark]
    public object YamlDotNet()
    {
        // YamlDotNet multi-document uses MergingParser or LoadAll
        var parser = new YamlDotNet.Core.Parser(new StringReader(_yaml));
        var results = new List<object>();
        var deserializer = _yamlDotNetDeserializer;
        while (parser.MoveNext())
        {
            if (parser.Current is YamlDotNet.Core.Events.StreamStart) continue;
            if (parser.Current is YamlDotNet.Core.Events.StreamEnd) break;
            results.Add(deserializer.Deserialize<Dictionary<string, object>>(parser)!);
        }
        return results;
    }
}

// ---------------------------------------------------------------------------
// Benchmark 12: Docker Compose with anchors/aliases/merge keys
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class DockerComposeAliasBenchmarks
{
    private const string ComposeYaml = """
        version: "3.8"

        x-logging: &logging
          driver: json-file
          options:
            max-size: "10m"
            max-file: "3"

        x-resources: &resources
          limits:
            cpus: "0.5"
            memory: 512M
          reservations:
            cpus: "0.25"
            memory: 256M

        x-common-env: &common-env
          TZ: UTC
          LOG_FORMAT: json
          OTEL_EXPORTER: otlp

        services:
          web:
            image: myapp/web:latest
            ports:
              - "8080:3000"
            environment:
              <<: *common-env
              DATABASE_URL: postgres://user:pass@db:5432/myapp
              REDIS_URL: redis://cache:6379
            logging: *logging
            deploy:
              replicas: 2
              resources: *resources
            depends_on:
              - db
              - cache

          worker:
            image: myapp/worker:latest
            environment:
              <<: *common-env
              QUEUE: default,priority,emails
              CONCURRENCY: "8"
            logging: *logging
            deploy:
              replicas: 3
              resources: *resources
            depends_on:
              - db
              - cache

          scheduler:
            image: myapp/scheduler:latest
            environment:
              <<: *common-env
              SCHEDULE_INTERVAL: "60"
            logging: *logging
            deploy:
              replicas: 1
              resources: *resources

          db:
            image: postgres:16-alpine
            environment:
              POSTGRES_USER: user
              POSTGRES_PASSWORD: pass
              POSTGRES_DB: myapp
            volumes:
              - pgdata:/var/lib/postgresql/data
            logging: *logging

          cache:
            image: redis:7-alpine
            command: redis-server --maxmemory 256mb --maxmemory-policy allkeys-lru
            logging: *logging

        volumes:
          pgdata:

        networks:
          default:
            driver: bridge
        """;

    private readonly string _yaml = ComposeYaml;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public object SystemTextYaml() => SystemTextYamlSerializer.Deserialize<Dictionary<string, object>>(_yaml)!;

    [Benchmark]
    public object YamlDotNet() => _yamlDotNetDeserializer.Deserialize<Dictionary<string, object>>(_yaml)!;
}

// ---------------------------------------------------------------------------
// Benchmark 13: Mixed flow & block collections (GitHub Actions style)
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class MixedFlowBlockBenchmarks
{
    private const string GhaYaml = """
        name: CI Pipeline
        on:
          push:
            branches: [main, "release/*"]
            paths-ignore: ["**.md", "docs/**"]
          pull_request:
            branches: [main]
        concurrency:
          group: ci-${{ github.ref }}
          cancel-in-progress: true
        env:
          DOTNET_VERSION: "8.0.x"
          REGISTRY: ghcr.io
        jobs:
          build:
            runs-on: ubuntu-latest
            permissions: {contents: read, packages: write}
            strategy:
              matrix:
                os: [ubuntu-latest, windows-latest, macos-latest]
                dotnet: ["8.0.x", "9.0.x"]
                exclude:
                  - os: macos-latest
                    dotnet: "9.0.x"
            steps:
              - uses: actions/checkout@v4
                with:
                  fetch-depth: 0
              - name: Setup .NET
                uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: ${{ matrix.dotnet }}
              - run: dotnet restore
              - run: dotnet build -c Release --no-restore
              - run: dotnet test -c Release --no-build --logger "trx"
              - name: Upload results
                if: always()
                uses: actions/upload-artifact@v4
                with:
                  name: test-results-${{ matrix.os }}-${{ matrix.dotnet }}
                  path: "**/*.trx"
          publish:
            needs: [build]
            if: github.event_name == 'push' && github.ref == 'refs/heads/main'
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4
              - run: dotnet pack -c Release -o ./artifacts
              - name: Push to NuGet
                run: dotnet nuget push ./artifacts/*.nupkg --api-key ${{ secrets.NUGET_KEY }}
        """;

    private readonly string _yaml = GhaYaml;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public object SystemTextYaml() => SystemTextYamlSerializer.Deserialize<Dictionary<string, object>>(_yaml)!;

    [Benchmark]
    public object YamlDotNet() => _yamlDotNetDeserializer.Deserialize<Dictionary<string, object>>(_yaml)!;
}

// ---------------------------------------------------------------------------
// Benchmark 14: Block scalars (ConfigMap with embedded configs)
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class BlockScalarBenchmarks
{
    private const string ConfigMapYaml = """
        apiVersion: v1
        kind: ConfigMap
        metadata:
          name: app-config
        data:
          application.yaml: |
            server:
              port: 8080
              shutdown: graceful
            spring:
              datasource:
                url: jdbc:postgresql://db:5432/app
                username: appuser
                password: ${DB_PASSWORD}
                hikari:
                  maximum-pool-size: 20
                  minimum-idle: 5
              cache:
                type: redis
                redis:
                  host: cache
                  port: 6379
            management:
              endpoints:
                web:
                  exposure:
                    include: health,metrics,prometheus
          nginx.conf: |
            upstream backend {
                server 127.0.0.1:8080;
                server 127.0.0.1:8081;
                keepalive 32;
            }
            server {
                listen 80;
                server_name api.example.com;
                location / {
                    proxy_pass http://backend;
                    proxy_set_header Host $host;
                    proxy_set_header X-Real-IP $remote_addr;
                    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
                    proxy_connect_timeout 5s;
                    proxy_read_timeout 60s;
                }
                location /health {
                    access_log off;
                    return 200 "OK";
                }
            }
          startup.sh: |
            #!/bin/bash
            set -euo pipefail
            echo "Starting application..."
            java -XX:+UseG1GC \
                 -XX:MaxGCPauseMillis=200 \
                 -Xms512m -Xmx2g \
                 -jar /app/application.jar
          description: >
            This ConfigMap contains all the configuration files
            needed to run the application stack. It includes
            the Spring Boot application config, the nginx
            reverse proxy config, and the startup script.
        """;

    private readonly string _yaml = ConfigMapYaml;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public object SystemTextYaml() => SystemTextYamlDocument.Parse(_yaml);

    [Benchmark]
    public object YamlDotNet() => _yamlDotNetDeserializer.Deserialize<Dictionary<string, object>>(_yaml)!;
}

// ---------------------------------------------------------------------------
// Benchmark 15: Deeply nested Helm values
// ---------------------------------------------------------------------------
[MemoryDiagnoser]
[ShortRunJob]
public class HelmValuesBenchmarks
{
    private const string HelmYaml = """
        global:
          imageRegistry: registry.example.com
          imagePullSecrets:
            - name: regcred
          storageClass: gp3

        defaults: &defaults
          replicaCount: 2
          podSecurityContext:
            runAsNonRoot: true
            runAsUser: 1000
            fsGroup: 2000
          securityContext:
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem: true
            capabilities:
              drop: [ALL]
          resources:
            requests:
              cpu: 250m
              memory: 256Mi
            limits:
              cpu: 500m
              memory: 512Mi

        api:
          <<: *defaults
          replicaCount: 3
          image:
            repository: myapp/api
            tag: "2.1.0"
            pullPolicy: IfNotPresent
          service:
            type: ClusterIP
            port: 8080
          ingress:
            enabled: true
            className: nginx
            annotations:
              nginx.ingress.kubernetes.io/ssl-redirect: "true"
              nginx.ingress.kubernetes.io/proxy-body-size: "50m"
              cert-manager.io/cluster-issuer: letsencrypt-prod
            hosts:
              - host: api.example.com
                paths:
                  - path: /
                    pathType: Prefix
              - host: api-internal.example.com
                paths:
                  - path: /internal
                    pathType: Prefix
            tls:
              - secretName: api-tls
                hosts:
                  - api.example.com
                  - api-internal.example.com
          autoscaling:
            enabled: true
            minReplicas: 3
            maxReplicas: 15
            metrics:
              - type: Resource
                resource:
                  name: cpu
                  target:
                    type: Utilization
                    averageUtilization: 70
              - type: Resource
                resource:
                  name: memory
                  target:
                    type: Utilization
                    averageUtilization: 80

        worker:
          <<: *defaults
          replicaCount: 5
          image:
            repository: myapp/worker
            tag: "2.1.0"
          resources:
            requests:
              cpu: 500m
              memory: 1Gi
            limits:
              cpu: "2"
              memory: 4Gi

        postgresql:
          enabled: true
          auth:
            postgresPassword: "${POSTGRES_PASSWORD}"
            database: myapp
          primary:
            persistence:
              enabled: true
              size: 100Gi
              storageClass: gp3
            resources:
              requests:
                cpu: "1"
                memory: 2Gi

        redis:
          enabled: true
          architecture: standalone
          auth:
            enabled: false
          master:
            persistence:
              enabled: true
              size: 10Gi
        """;

    private readonly string _yaml = HelmYaml;
    private readonly IDeserializer _yamlDotNetDeserializer = new DeserializerBuilder().Build();

    [Benchmark(Baseline = true)]
    public object SystemTextYaml() => SystemTextYamlDocument.Parse(_yaml);

    [Benchmark]
    public object YamlDotNet() => _yamlDotNetDeserializer.Deserialize<Dictionary<string, object>>(_yaml)!;
}
