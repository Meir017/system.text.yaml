using System.Text.Json.Serialization;

namespace System.Text.Yaml.Tests;

/// <summary>
/// Tests parsing real-world YAML from Kubernetes, Docker Compose, GitHub Actions,
/// Helm, and other common infrastructure tools.
/// </summary>
public class RealWorldYamlTests
{
    // -----------------------------------------------------------------------
    // Kubernetes manifests
    // -----------------------------------------------------------------------

    [Fact]
    public void Kubernetes_Deployment_FullManifest()
    {
        const string yaml = """
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
                kubectl.kubernetes.io/last-applied-configuration: |
                  {"apiVersion":"apps/v1","kind":"Deployment"}
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
                      valueFrom:
                        secretKeyRef:
                          name: db-credentials
                          key: connection-string
                    - name: LOG_LEVEL
                      value: "info"
                    - name: FEATURE_FLAGS
                      value: ""
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
                    args:
                    - --config-path
                    - /etc/envoy/config.yaml
                    - --log-level
                    - warning
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
                  affinity:
                    podAntiAffinity:
                      preferredDuringSchedulingIgnoredDuringExecution:
                      - weight: 100
                        podAffinityTerm:
                          labelSelector:
                            matchExpressions:
                            - key: app
                              operator: In
                              values:
                              - web-frontend
                          topologyKey: kubernetes.io/hostname
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        Assert.Equal("apps/v1", result!["apiVersion"]!.GetScalarValue());
        Assert.Equal("Deployment", result["kind"]!.GetScalarValue());
        Assert.Equal("web-frontend", result["metadata"]!["name"]!.GetScalarValue());
        Assert.Equal("production", result["metadata"]!["namespace"]!.GetScalarValue());
        Assert.Equal(3, result["spec"]!["replicas"]!.GetScalarInt());

        // Check nested container spec
        var containers = result["spec"]!["template"]!["spec"]!["containers"]!.AsSequence();
        Assert.Equal(2, containers.Count);
        Assert.Equal("web", containers[0]!["name"]!.GetScalarValue());
        Assert.Equal("sidecar", containers[1]!["name"]!.GetScalarValue());

        // Check env vars (includes valueFrom and empty string value)
        var env = containers[0]!["env"]!.AsSequence();
        Assert.Equal(3, env.Count);
        Assert.Equal("DATABASE_URL", env[0]!["name"]!.GetScalarValue());
        Assert.NotNull(env[0]!["valueFrom"]);
        Assert.Equal("info", env[1]!["value"]!.GetScalarValue());
        Assert.Equal("", env[2]!["value"]!.GetScalarValue());

        // Check resources
        Assert.Equal("250m", result["spec"]!["template"]!["spec"]!["containers"]![0]!["resources"]!["requests"]!["cpu"]!.GetScalarValue());
        Assert.Equal("1", result["spec"]!["template"]!["spec"]!["containers"]![0]!["resources"]!["limits"]!["cpu"]!.GetScalarValue());

        // Check deeply nested affinity
        var affinity = result["spec"]!["template"]!["spec"]!["affinity"]!;
        var podAntiAffinity = affinity["podAntiAffinity"]!["preferredDuringSchedulingIgnoredDuringExecution"]!.AsSequence();
        Assert.Single(podAntiAffinity);
        Assert.Equal(100, podAntiAffinity[0]!["weight"]!.GetScalarInt());

        // Block scalar annotation
        var lastApplied = result["metadata"]!["annotations"]!["kubectl.kubernetes.io/last-applied-configuration"]!.GetScalarValue();
        Assert.Contains("apiVersion", lastApplied);
    }

    [Fact]
    public void Kubernetes_Service_WithMixedFlowAndBlock()
    {
        const string yaml = """
            apiVersion: v1
            kind: Service
            metadata:
              name: web-frontend
              labels: {app: web-frontend, tier: frontend}
            spec:
              type: LoadBalancer
              loadBalancerSourceRanges: [10.0.0.0/8, 172.16.0.0/12]
              ports:
              - port: 80
                targetPort: 8080
                protocol: TCP
                name: http
              - port: 443
                targetPort: 8443
                protocol: TCP
                name: https
              selector:
                app: web-frontend
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        Assert.Equal("LoadBalancer", result!["spec"]!["type"]!.GetScalarValue());

        // Flow mapping in labels
        Assert.Equal("web-frontend", result["metadata"]!["labels"]!["app"]!.GetScalarValue());
        Assert.Equal("frontend", result["metadata"]!["labels"]!["tier"]!.GetScalarValue());

        // Flow sequence
        var ranges = result["spec"]!["loadBalancerSourceRanges"]!.AsSequence();
        Assert.Equal(2, ranges.Count);
        Assert.Equal("10.0.0.0/8", ranges[0]!.GetScalarValue());

        // Block sequence of mappings
        var ports = result["spec"]!["ports"]!.AsSequence();
        Assert.Equal(2, ports.Count);
        Assert.Equal(80, ports[0]!["port"]!.GetScalarInt());
        Assert.Equal(443, ports[1]!["port"]!.GetScalarInt());
    }

    [Fact]
    public void Kubernetes_ConfigMap_WithMultilineData()
    {
        const string yaml = """
            apiVersion: v1
            kind: ConfigMap
            metadata:
              name: app-config
            data:
              application.properties: |
                server.port=8080
                spring.datasource.url=jdbc:postgresql://db:5432/app
                spring.profiles.active=production
                logging.level.root=WARN
              nginx.conf: |
                server {
                    listen 80;
                    location / {
                        proxy_pass http://localhost:8080;
                    }
                }
              simple-key: just-a-value
              quoted-value: "contains: colon and # hash"
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        var data = result!["data"]!;
        var appProps = data["application.properties"]!.GetScalarValue();
        Assert.Contains("server.port=8080", appProps);
        Assert.Contains("logging.level.root=WARN", appProps);
        Assert.True(appProps.EndsWith('\n'));

        var nginxConf = data["nginx.conf"]!.GetScalarValue();
        Assert.Contains("listen 80;", nginxConf);
        Assert.Contains("proxy_pass", nginxConf);

        Assert.Equal("just-a-value", data["simple-key"]!.GetScalarValue());
        Assert.Equal("contains: colon and # hash", data["quoted-value"]!.GetScalarValue());
    }

    [Fact]
    public void Kubernetes_CRD_ComplexNestedSpec()
    {
        const string yaml = """
            apiVersion: networking.istio.io/v1beta1
            kind: VirtualService
            metadata:
              name: reviews-route
            spec:
              hosts:
              - reviews.prod.svc.cluster.local
              http:
              - match:
                - headers:
                    end-user:
                      exact: jason
                - uri:
                    prefix: /ratings/v2/
                route:
                - destination:
                    host: reviews.prod.svc.cluster.local
                    subset: v2
                  weight: 75
                - destination:
                    host: reviews.prod.svc.cluster.local
                    subset: v3
                  weight: 25
                timeout: 5s
                retries:
                  attempts: 3
                  perTryTimeout: 2s
                  retryOn: gateway-error,connect-failure,refused-stream
              - route:
                - destination:
                    host: reviews.prod.svc.cluster.local
                    subset: v1
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        var http = result!["spec"]!["http"]!.AsSequence();
        Assert.Equal(2, http.Count);

        // First route has match conditions and weighted destinations
        var firstRoute = http[0]!;
        var match = firstRoute["match"]!.AsSequence();
        Assert.Equal(2, match.Count);
        Assert.Equal("jason", match[0]!["headers"]!["end-user"]!["exact"]!.GetScalarValue());

        var routes = firstRoute["route"]!.AsSequence();
        Assert.Equal(2, routes.Count);
        Assert.Equal(75, routes[0]!["weight"]!.GetScalarInt());
        Assert.Equal(25, routes[1]!["weight"]!.GetScalarInt());
        Assert.Equal("v2", routes[0]!["destination"]!["subset"]!.GetScalarValue());

        Assert.Equal("5s", firstRoute["timeout"]!.GetScalarValue());
        Assert.Equal(3, firstRoute["retries"]!["attempts"]!.GetScalarInt());

        // Default route
        var defaultRoute = http[1]!["route"]!.AsSequence();
        Assert.Single(defaultRoute);
        Assert.Equal("v1", defaultRoute[0]!["destination"]!["subset"]!.GetScalarValue());
    }

    [Fact]
    public void Kubernetes_MultiDocument_ApplyManifest()
    {
        const string yaml = """
            apiVersion: v1
            kind: Namespace
            metadata:
              name: staging
            ---
            apiVersion: v1
            kind: ServiceAccount
            metadata:
              name: app-sa
              namespace: staging
            ---
            apiVersion: v1
            kind: Secret
            metadata:
              name: db-secret
              namespace: staging
            type: Opaque
            stringData:
              password: "s3cret!value"
            """;

        var docs = YamlSerializer.DeserializeAll<YamlNode?>(yaml);
        Assert.Equal(3, docs.Count);

        Assert.Equal("Namespace", docs[0]!["kind"]!.GetScalarValue());
        Assert.Equal("ServiceAccount", docs[1]!["kind"]!.GetScalarValue());
        Assert.Equal("Secret", docs[2]!["kind"]!.GetScalarValue());
        Assert.Equal("s3cret!value", docs[2]!["stringData"]!["password"]!.GetScalarValue());
    }

    // -----------------------------------------------------------------------
    // Docker Compose
    // -----------------------------------------------------------------------

    [Fact]
    public void DockerCompose_FullStack()
    {
        const string yaml = """
            version: "3.8"
            
            x-common-env: &common-env
              TZ: UTC
              LOG_FORMAT: json
            
            services:
              web:
                build:
                  context: .
                  dockerfile: Dockerfile
                  args:
                    NODE_ENV: production
                image: myapp:latest
                ports:
                  - "8080:3000"
                  - "9229:9229"
                environment:
                  <<: *common-env
                  DATABASE_URL: postgres://user:pass@db:5432/myapp
                  REDIS_URL: redis://cache:6379
                  SECRET_KEY: ${SECRET_KEY:-default-dev-key}
                depends_on:
                  db:
                    condition: service_healthy
                  cache:
                    condition: service_started
                volumes:
                  - ./src:/app/src:ro
                  - node_modules:/app/node_modules
                networks:
                  - frontend
                  - backend
                deploy:
                  replicas: 2
                  resources:
                    limits:
                      cpus: "0.5"
                      memory: 512M
                    reservations:
                      cpus: "0.25"
                      memory: 256M
                healthcheck:
                  test: ["CMD", "curl", "-f", "http://localhost:3000/health"]
                  interval: 30s
                  timeout: 10s
                  retries: 3
                  start_period: 40s
                logging:
                  driver: json-file
                  options:
                    max-size: "10m"
                    max-file: "3"
            
              db:
                image: postgres:16-alpine
                environment:
                  <<: *common-env
                  POSTGRES_USER: user
                  POSTGRES_PASSWORD: pass
                  POSTGRES_DB: myapp
                volumes:
                  - pgdata:/var/lib/postgresql/data
                  - ./init.sql:/docker-entrypoint-initdb.d/init.sql:ro
                healthcheck:
                  test: ["CMD-SHELL", "pg_isready -U user"]
                  interval: 5s
                  timeout: 5s
                  retries: 5
                networks:
                  - backend
            
              cache:
                image: redis:7-alpine
                command: redis-server --maxmemory 128mb --maxmemory-policy allkeys-lru
                networks:
                  - backend
            
            volumes:
              pgdata:
              node_modules:
            
            networks:
              frontend:
                driver: bridge
              backend:
                driver: bridge
                internal: true
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        Assert.Equal("3.8", result!["version"]!.GetScalarValue());

        // Anchor/alias merge key
        var webEnv = result["services"]!["web"]!["environment"]!;
        Assert.Equal("UTC", webEnv["TZ"]!.GetScalarValue());
        Assert.Equal("json", webEnv["LOG_FORMAT"]!.GetScalarValue());
        Assert.Contains("postgres://", webEnv["DATABASE_URL"]!.GetScalarValue());

        // Depends_on with conditions
        Assert.Equal("service_healthy", result["services"]!["web"]!["depends_on"]!["db"]!["condition"]!.GetScalarValue());

        // Flow sequence in healthcheck
        var test = result["services"]!["web"]!["healthcheck"]!["test"]!.AsSequence();
        Assert.Equal(4, test.Count);
        Assert.Equal("CMD", test[0]!.GetScalarValue());

        // Deploy resources
        Assert.Equal("0.5", result["services"]!["web"]!["deploy"]!["resources"]!["limits"]!["cpus"]!.GetScalarValue());

        // Volumes and networks top-level
        Assert.True(result["volumes"]!.AsMapping().ContainsKey("pgdata"));
        Assert.True(result["networks"]!["backend"]!["internal"]!.GetScalarBool());
    }

    // -----------------------------------------------------------------------
    // GitHub Actions
    // -----------------------------------------------------------------------

    [Fact]
    public void GitHubActions_ComplexWorkflow()
    {
        const string yaml = """
            name: CI/CD Pipeline
            
            on:
              push:
                branches: [main, "release/*"]
                paths-ignore:
                  - "**.md"
                  - docs/**
              pull_request:
                branches: [main]
              workflow_dispatch:
                inputs:
                  environment:
                    description: "Target environment"
                    required: true
                    default: staging
                    type: choice
                    options:
                      - staging
                      - production
            
            concurrency:
              group: ${{ github.workflow }}-${{ github.ref }}
              cancel-in-progress: true
            
            env:
              DOTNET_VERSION: "8.0.x"
              REGISTRY: ghcr.io
              IMAGE_NAME: ${{ github.repository }}
            
            jobs:
              build:
                runs-on: ubuntu-latest
                permissions:
                  contents: read
                  packages: write
                outputs:
                  version: ${{ steps.version.outputs.version }}
                steps:
                  - uses: actions/checkout@v4
                    with:
                      fetch-depth: 0
            
                  - name: Setup .NET
                    uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: ${{ env.DOTNET_VERSION }}
            
                  - name: Restore
                    run: dotnet restore --locked-mode
            
                  - name: Build
                    run: dotnet build --no-restore -c Release
            
                  - name: Test
                    run: |
                      dotnet test --no-build -c Release \
                        --logger "trx;LogFileName=results.trx" \
                        --collect:"XPlat Code Coverage"
            
                  - name: Determine version
                    id: version
                    run: echo "version=$(date +%Y.%m.%d)-${GITHUB_SHA::7}" >> $GITHUB_OUTPUT
            
              deploy:
                needs: [build]
                if: github.ref == 'refs/heads/main' && github.event_name == 'push'
                runs-on: ubuntu-latest
                environment:
                  name: production
                  url: https://app.example.com
                steps:
                  - name: Deploy
                    uses: azure/webapps-deploy@v3
                    with:
                      app-name: my-web-app
                      images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ needs.build.outputs.version }}
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        Assert.Equal("CI/CD Pipeline", result!["name"]!.GetScalarValue());

        // on.push.branches - flow sequence with quoted glob
        var branches = result["on"]!["push"]!["branches"]!.AsSequence();
        Assert.Equal(2, branches.Count);
        Assert.Equal("main", branches[0]!.GetScalarValue());
        Assert.Equal("release/*", branches[1]!.GetScalarValue());

        // workflow_dispatch inputs
        var inputs = result["on"]!["workflow_dispatch"]!["inputs"]!["environment"]!;
        Assert.Equal("Target environment", inputs["description"]!.GetScalarValue());
        Assert.True(inputs["required"]!.GetScalarBool());

        // Concurrency with expression syntax (contains ${{ }})
        Assert.Contains("github.workflow", result["concurrency"]!["group"]!.GetScalarValue());
        Assert.True(result["concurrency"]!["cancel-in-progress"]!.GetScalarBool());

        // Job outputs with expression
        Assert.Contains("steps.version", result["jobs"]!["build"]!["outputs"]!["version"]!.GetScalarValue());

        // Multi-line run with block scalar
        var testStep = result["jobs"]!["build"]!["steps"]!.AsSequence()
            .First(s => s!["name"]?.GetScalarValue() == "Test");
        Assert.Contains("dotnet test", testStep!["run"]!.GetScalarValue());

        // Deploy job with condition
        var deployJob = result["jobs"]!["deploy"]!;
        var needs = deployJob["needs"]!.AsSequence();
        Assert.Single(needs);
        Assert.Equal("build", needs[0]!.GetScalarValue());
        Assert.Contains("refs/heads/main", deployJob["if"]!.GetScalarValue());
    }

    // -----------------------------------------------------------------------
    // Helm values
    // -----------------------------------------------------------------------

    [Fact]
    public void Helm_ValuesYaml_WithAnchorsAndComplexNesting()
    {
        const string yaml = """
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

            api:
              <<: *defaults
              replicaCount: 3
              image:
                repository: myapp/api
                tag: "1.2.3"
                pullPolicy: IfNotPresent
              service:
                type: ClusterIP
                port: 8080
              ingress:
                enabled: true
                className: nginx
                annotations:
                  nginx.ingress.kubernetes.io/ssl-redirect: "true"
                  nginx.ingress.kubernetes.io/rate-limit: "100"
                hosts:
                  - host: api.example.com
                    paths:
                      - path: /
                        pathType: Prefix
                tls:
                  - secretName: api-tls
                    hosts:
                      - api.example.com
              autoscaling:
                enabled: true
                minReplicas: 3
                maxReplicas: 10
                targetCPUUtilizationPercentage: 70
                targetMemoryUtilizationPercentage: 80

            worker:
              <<: *defaults
              image:
                repository: myapp/worker
                tag: "1.2.3"
              resources:
                requests:
                  cpu: 500m
                  memory: 1Gi
                limits:
                  cpu: "2"
                  memory: 4Gi
              extraEnv:
                - name: WORKER_CONCURRENCY
                  value: "8"
                - name: QUEUE_NAME
                  value: default,priority
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        // Global values
        Assert.Equal("registry.example.com", result!["global"]!["imageRegistry"]!.GetScalarValue());
        Assert.Equal("gp3", result["global"]!["storageClass"]!.GetScalarValue());

        // Merge key inheritance — api gets defaults then overrides replicaCount
        var api = result["api"]!;
        Assert.Equal(3, api["replicaCount"]!.GetScalarInt());
        Assert.True(api["podSecurityContext"]!["runAsNonRoot"]!.GetScalarBool());
        Assert.Equal(1000, api["podSecurityContext"]!["runAsUser"]!.GetScalarInt());

        // Security context with flow sequence
        var drop = api["securityContext"]!["capabilities"]!["drop"]!.AsSequence();
        Assert.Single(drop);
        Assert.Equal("ALL", drop[0]!.GetScalarValue());

        // Ingress annotations with quoted values
        Assert.Equal("true", api["ingress"]!["annotations"]!["nginx.ingress.kubernetes.io/ssl-redirect"]!.GetScalarValue());

        // Worker also inherits defaults
        var worker = result["worker"]!;
        Assert.True(worker["podSecurityContext"]!["runAsNonRoot"]!.GetScalarBool());
        Assert.Equal("1Gi", worker["resources"]!["requests"]!["memory"]!.GetScalarValue());
    }

    // -----------------------------------------------------------------------
    // Complex scalar edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void ComplexScalars_AllBlockStyles()
    {
        const string yaml = """
            literal-keeps-newlines: |
              Line 1
              Line 2
              
              Line 4
            folded-joins-lines: >
              This is a long
              paragraph that gets
              folded into one line.
              
              This is a second
              paragraph.
            literal-strip: |-
              no trailing newline
            literal-keep: |+
              keeps trailing
              
            folded-strip: >-
              no trailing newline here either
            plain: just a plain scalar
            single-quoted: 'contains ''escaped'' quotes'
            double-quoted: "contains\nnewline\tand\ttabs"
            empty-literal: |
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        // Literal keeps internal newlines
        Assert.Equal("Line 1\nLine 2\n\nLine 4\n", result!["literal-keeps-newlines"]!.GetScalarValue());

        // Folded joins lines, preserves blank-line paragraphs
        var folded = result["folded-joins-lines"]!.GetScalarValue();
        Assert.Contains("folded into one line.", folded);
        Assert.Contains("\nThis is a second paragraph.\n", folded);

        // Strip removes trailing newlines
        Assert.Equal("no trailing newline", result["literal-strip"]!.GetScalarValue());

        // Keep preserves trailing newlines
        Assert.Equal("keeps trailing\n\n", result["literal-keep"]!.GetScalarValue());

        // Folded strip
        Assert.Equal("no trailing newline here either", result["folded-strip"]!.GetScalarValue());

        // Single-quoted escape
        Assert.Equal("contains 'escaped' quotes", result["single-quoted"]!.GetScalarValue());

        // Double-quoted escape sequences
        Assert.Equal("contains\nnewline\tand\ttabs", result["double-quoted"]!.GetScalarValue());
    }

    [Fact]
    public void FlowCollections_NestedAndComplex()
    {
        const string yaml = """
            matrix: [[1, 0, 0], [0, 1, 0], [0, 0, 1]]
            config: {debug: true, log: {level: info, format: json}, tags: [a, b, c]}
            empty-map: {}
            empty-seq: []
            nested-empty: {a: {}, b: [], c: {d: []}}
            mixed: [{name: one, values: [1, 2]}, {name: two, values: [3, 4]}]
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        // Nested arrays (matrix)
        var matrix = result!["matrix"]!.AsSequence();
        Assert.Equal(3, matrix.Count);
        Assert.Equal(1, matrix[0]![0]!.GetScalarInt());
        Assert.Equal(0, matrix[0]![1]!.GetScalarInt());
        Assert.Equal(1, matrix[2]![2]!.GetScalarInt());

        // Nested flow mapping
        Assert.True(result["config"]!["debug"]!.GetScalarBool());
        Assert.Equal("info", result["config"]!["log"]!["level"]!.GetScalarValue());
        Assert.Equal(3, result["config"]!["tags"]!.AsSequence().Count);

        // Empty collections
        Assert.Equal(0, result["empty-map"]!.AsMapping().Count);
        Assert.Equal(0, result["empty-seq"]!.AsSequence().Count);

        // Nested empties
        Assert.Equal(0, result["nested-empty"]!["a"]!.AsMapping().Count);
        Assert.Equal(0, result["nested-empty"]!["b"]!.AsSequence().Count);
        Assert.Equal(0, result["nested-empty"]!["c"]!["d"]!.AsSequence().Count);

        // Mixed arrays of objects
        var mixed = result["mixed"]!.AsSequence();
        Assert.Equal("one", mixed[0]!["name"]!.GetScalarValue());
        Assert.Equal(2, mixed[0]!["values"]![1]!.GetScalarInt());
    }

    // -----------------------------------------------------------------------
    // Strongly-typed deserialization of real-world models
    // -----------------------------------------------------------------------

    [Fact]
    public void StronglyTyped_KubernetesDeployment()
    {
        const string yaml = """
            apiVersion: apps/v1
            kind: Deployment
            metadata:
              name: my-app
              labels:
                app: my-app
            spec:
              replicas: 2
              selector:
                matchLabels:
                  app: my-app
              template:
                metadata:
                  labels:
                    app: my-app
                spec:
                  containers:
                  - name: app
                    image: myapp:v1
                    ports:
                    - containerPort: 80
            """;

        var result = YamlSerializer.Deserialize<K8sDeployment>(yaml);
        Assert.NotNull(result);
        Assert.Equal("apps/v1", result.ApiVersion);
        Assert.Equal("Deployment", result.Kind);
        Assert.Equal("my-app", result.Metadata.Name);
        Assert.Equal(2, result.Spec.Replicas);
        Assert.Single(result.Spec.Template.Spec.Containers);
        Assert.Equal("app", result.Spec.Template.Spec.Containers[0].Name);
        Assert.Equal("myapp:v1", result.Spec.Template.Spec.Containers[0].Image);
        Assert.Equal(80, result.Spec.Template.Spec.Containers[0].Ports[0].ContainerPort);
    }

    [Fact]
    public void RoundTrip_ComplexConfig_PreservesStructure()
    {
        var config = new AppConfig
        {
            Database = new DatabaseConfig
            {
                Host = "db.example.com",
                Port = 5432,
                Name = "myapp",
                Ssl = true,
                PoolSize = 20,
                Options = new Dictionary<string, string>
                {
                    ["statement_timeout"] = "30s",
                    ["lock_timeout"] = "10s"
                }
            },
            Cache = new CacheConfig
            {
                Provider = "redis",
                Nodes = ["cache-1:6379", "cache-2:6379", "cache-3:6379"],
                Ttl = 3600
            },
            Features = new Dictionary<string, bool>
            {
                ["dark-mode"] = true,
                ["beta-api"] = false,
                ["new-dashboard"] = true
            }
        };

        var yaml = YamlSerializer.Serialize(config);
        var restored = YamlSerializer.Deserialize<AppConfig>(yaml);

        Assert.NotNull(restored);
        Assert.Equal("db.example.com", restored.Database.Host);
        Assert.Equal(5432, restored.Database.Port);
        Assert.True(restored.Database.Ssl);
        Assert.Equal(20, restored.Database.PoolSize);
        Assert.Equal("30s", restored.Database.Options["statement_timeout"]);
        Assert.Equal("redis", restored.Cache.Provider);
        Assert.Equal(3, restored.Cache.Nodes.Length);
        Assert.Equal(3600, restored.Cache.Ttl);
        Assert.True(restored.Features["dark-mode"]);
        Assert.False(restored.Features["beta-api"]);
    }

    // -----------------------------------------------------------------------
    // Edge cases found in real configs
    // -----------------------------------------------------------------------

    [Fact]
    public void EdgeCase_ColonsInValues()
    {
        const string yaml = """
            url: https://example.com:8443/api/v1
            timestamp: "2024-01-15T10:30:00Z"
            message: "Error: connection refused"
            mapping: "key: value"
            bare-url: http://localhost
            time: 10:30
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);
        Assert.Equal("https://example.com:8443/api/v1", result!["url"]!.GetScalarValue());
        Assert.Equal("2024-01-15T10:30:00Z", result["timestamp"]!.GetScalarValue());
        Assert.Equal("Error: connection refused", result["message"]!.GetScalarValue());
        Assert.Equal("http://localhost", result["bare-url"]!.GetScalarValue());
    }

    [Fact]
    public void EdgeCase_SpecialKeyNames()
    {
        const string yaml = """
            "true": not-a-boolean
            "null": not-null
            "123": not-a-number
            "on": not-yaml11-bool
            key with spaces: value
            "key: with: colons": value2
            "": empty-key
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);
        Assert.Equal("not-a-boolean", result!["true"]!.GetScalarValue());
        Assert.Equal("not-null", result["null"]!.GetScalarValue());
        Assert.Equal("not-a-number", result["123"]!.GetScalarValue());
        Assert.Equal("value", result["key with spaces"]!.GetScalarValue());
        Assert.Equal("value2", result["key: with: colons"]!.GetScalarValue());
        Assert.Equal("empty-key", result[""]!.GetScalarValue());
    }

    [Fact]
    public void EdgeCase_NumericEdgeCases()
    {
        const string yaml = """
            integer: 42
            negative: -17
            hex: 0xFF
            octal: 0o77
            float: 3.14159
            scientific: 6.022e23
            infinity: .inf
            neg-infinity: -.inf
            not-a-number: .nan
            version-string: "1.2.3"
            port-number: 8080
            zero: 0
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);
        Assert.Equal(42, result!["integer"]!.GetScalarLong());
        Assert.Equal(-17, result["negative"]!.GetScalarLong());
        Assert.Equal(255, result["hex"]!.GetScalarLong());
        Assert.Equal(63, result["octal"]!.GetScalarLong());
        Assert.Equal(0, result["zero"]!.GetScalarLong());
        Assert.Equal(8080, result["port-number"]!.GetScalarInt());
        Assert.Equal("1.2.3", result["version-string"]!.GetScalarValue());
    }

    [Fact]
    public void EdgeCase_CommentsEverywhere()
    {
        const string yaml = """
            # Top-level comment
            name: value # inline comment
            list: # comment after key
              - item1 # inline
              # between items
              - item2
            nested:
              # comment in nested
              key: val
            # trailing comment
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);
        Assert.Equal("value", result!["name"]!.GetScalarValue());
        Assert.Equal(2, result["list"]!.AsSequence().Count);
        Assert.Equal("item1", result["list"]![0]!.GetScalarValue());
        Assert.Equal("item2", result["list"]![1]!.GetScalarValue());
        Assert.Equal("val", result["nested"]!["key"]!.GetScalarValue());
    }

    [Fact]
    public void EdgeCase_DeeplyNestedStructure()
    {
        const string yaml = """
            level1:
              level2:
                level3:
                  level4:
                    level5:
                      level6:
                        level7:
                          level8:
                            value: deep
                            list:
                              - a
                              - b: c
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);
        var deep = result!["level1"]!["level2"]!["level3"]!["level4"]!
            ["level5"]!["level6"]!["level7"]!["level8"]!;
        Assert.Equal("deep", deep["value"]!.GetScalarValue());
        Assert.Equal("a", deep["list"]![0]!.GetScalarValue());
        Assert.Equal("c", deep["list"]![1]!["b"]!.GetScalarValue());
    }

    [Fact]
    public void EdgeCase_AnchorAlias_ComplexReuse()
    {
        const string yaml = """
            templates:
              small: &small
                cpu: 250m
                memory: 256Mi
              medium: &medium
                cpu: 500m
                memory: 512Mi
              large: &large
                cpu: "1"
                memory: 1Gi

            services:
              api:
                resources:
                  requests: *small
                  limits: *medium
              worker:
                resources:
                  requests: *medium
                  limits: *large
              gateway:
                resources:
                  requests: *small
                  limits: *large
            """;

        var result = YamlSerializer.Deserialize<YamlNode>(yaml);

        // Verify alias expansion
        Assert.Equal("250m", result!["services"]!["api"]!["resources"]!["requests"]!["cpu"]!.GetScalarValue());
        Assert.Equal("512Mi", result["services"]!["api"]!["resources"]!["limits"]!["memory"]!.GetScalarValue());
        Assert.Equal("1", result["services"]!["worker"]!["resources"]!["limits"]!["cpu"]!.GetScalarValue());
        Assert.Equal("250m", result["services"]!["gateway"]!["resources"]!["requests"]!["cpu"]!.GetScalarValue());
        Assert.Equal("1Gi", result["services"]!["gateway"]!["resources"]!["limits"]!["memory"]!.GetScalarValue());
    }

    [Fact]
    public void Serialization_ProducesValidRereadableYaml()
    {
        var original = new K8sDeployment
        {
            ApiVersion = "apps/v1",
            Kind = "Deployment",
            Metadata = new K8sMetadata
            {
                Name = "test-app",
                Labels = new Dictionary<string, string>
                {
                    ["app"] = "test",
                    ["version"] = "v1"
                }
            },
            Spec = new K8sDeploymentSpec
            {
                Replicas = 3,
                Selector = new K8sSelector
                {
                    MatchLabels = new Dictionary<string, string> { ["app"] = "test" }
                },
                Template = new K8sPodTemplate
                {
                    Metadata = new K8sMetadata
                    {
                        Labels = new Dictionary<string, string> { ["app"] = "test" }
                    },
                    Spec = new K8sPodSpec
                    {
                        Containers =
                        [
                            new K8sContainer
                            {
                                Name = "main",
                                Image = "myapp:latest",
                                Ports = [new K8sPort { ContainerPort = 8080 }]
                            }
                        ]
                    }
                }
            }
        };

        // Serialize → parse → re-serialize → should produce same output
        var yaml1 = YamlSerializer.Serialize(original);
        var restored = YamlSerializer.Deserialize<K8sDeployment>(yaml1);
        var yaml2 = YamlSerializer.Serialize(restored);

        Assert.Equal(yaml1, yaml2);
        Assert.Equal(original.Spec.Replicas, restored!.Spec.Replicas);
        Assert.Equal(original.Spec.Template.Spec.Containers[0].Image, restored.Spec.Template.Spec.Containers[0].Image);
    }
}

// -----------------------------------------------------------------------
// Supporting model types
// -----------------------------------------------------------------------

public sealed class K8sDeployment
{
    [JsonPropertyName("apiVersion")]
    public string ApiVersion { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("metadata")]
    public K8sMetadata Metadata { get; set; } = new();

    [JsonPropertyName("spec")]
    public K8sDeploymentSpec Spec { get; set; } = new();
}

public sealed class K8sMetadata
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("labels")]
    public Dictionary<string, string>? Labels { get; set; }

    [JsonPropertyName("annotations")]
    public Dictionary<string, string>? Annotations { get; set; }
}

public sealed class K8sDeploymentSpec
{
    [JsonPropertyName("replicas")]
    public int Replicas { get; set; }

    [JsonPropertyName("selector")]
    public K8sSelector Selector { get; set; } = new();

    [JsonPropertyName("template")]
    public K8sPodTemplate Template { get; set; } = new();
}

public sealed class K8sSelector
{
    [JsonPropertyName("matchLabels")]
    public Dictionary<string, string> MatchLabels { get; set; } = new();
}

public sealed class K8sPodTemplate
{
    [JsonPropertyName("metadata")]
    public K8sMetadata Metadata { get; set; } = new();

    [JsonPropertyName("spec")]
    public K8sPodSpec Spec { get; set; } = new();
}

public sealed class K8sPodSpec
{
    [JsonPropertyName("containers")]
    public K8sContainer[] Containers { get; set; } = [];
}

public sealed class K8sContainer
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("ports")]
    public K8sPort[] Ports { get; set; } = [];
}

public sealed class K8sPort
{
    [JsonPropertyName("containerPort")]
    public int ContainerPort { get; set; }
}

public sealed class AppConfig
{
    public DatabaseConfig Database { get; set; } = new();
    public CacheConfig Cache { get; set; } = new();
    public Dictionary<string, bool> Features { get; set; } = new();
}

public sealed class DatabaseConfig
{
    public string Host { get; set; } = "";
    public int Port { get; set; }
    public string Name { get; set; } = "";
    public bool Ssl { get; set; }
    public int PoolSize { get; set; }
    public Dictionary<string, string> Options { get; set; } = new();
}

public sealed class CacheConfig
{
    public string Provider { get; set; } = "";
    public string[] Nodes { get; set; } = [];
    public int Ttl { get; set; }
}
