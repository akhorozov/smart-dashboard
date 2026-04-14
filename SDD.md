# Software Design Document  
## Redis Smart Dashboard

**Version:** 1.0  
**Date:** April 14, 2026  
**Stack:** .NET 10 · .NET Aspire · Blazor Server · ASP.NET Core Minimal API · Redis Stack

---

## Table of Contents

1. [Overview](#1-overview)
2. [Goals & Scope](#2-goals--scope)
3. [System Architecture Diagram](#3-system-architecture-diagram)
4. [Component Descriptions](#4-component-descriptions)
5. [Redis Data Architecture](#5-redis-data-architecture)
6. [API Design](#6-api-design)
7. [Sequence Diagrams](#7-sequence-diagrams)
8. [Infrastructure Diagram](#8-infrastructure-diagram)
9. [Observability](#9-observability)
10. [Security Considerations](#10-security-considerations)
11. [Non-Functional Requirements](#11-non-functional-requirements)

---

## 1. Overview

Redis Smart Dashboard is a reference application that demonstrates seven distinct Redis Stack capabilities within a single, cohesive .NET Aspire solution. The system is composed of a Blazor Server frontend, a RESTful Minimal API backend, and a Redis Stack container managed by the Aspire orchestrator.

---

## 2. Goals & Scope

| Goal | Description |
|---|---|
| Demonstrate Redis breadth | Show JSON, Search, TimeSeries, Pub/Sub, Vector, Bloom, and caching in one app |
| Developer experience | Single `dotnet run` to bring up the full stack via Aspire |
| Observable by default | OpenTelemetry traces, metrics, and logs wired at the framework level |
| Minimal external dependencies | Only Docker is required beyond .NET |

**Out of scope:** authentication/authorization, multi-node Redis cluster, persistent volumes, production hardening.

---

## 3. System Architecture Diagram

```mermaid
graph TB
    subgraph Browser
        UI[User Browser]
    end

    subgraph AspireHost["RedisSmartDemo.AppHost (.NET Aspire)"]
        direction TB
        Orchestrator[Aspire Orchestrator]
    end

    subgraph WebProject["RedisSmartDemo.Web (Blazor Server)"]
        direction TB
        BlazorPages[Razor Pages\nHome · Users · Products · Metrics\nActivity · Recommendations · Admin]
        ApiClient[ApiClient\nTyped HTTP Client]
        FluentUI[FluentUI Components]
        ApexCharts[ApexCharts]
    end

    subgraph ApiProject["RedisSmartDemo.ApiService (Minimal API)"]
        direction TB
        UserEP[UserEndpoints\nRedisJSON]
        ProductEP[ProductEndpoints\nRediSearch]
        MetricEP[MetricEndpoints\nTimeSeries]
        ActivityEP[ActivityEndpoints\nPub/Sub + List]
        RecoEP[RecommendationEndpoints\nVector Search]
        AdminEP[AdminEndpoints\nBloom + Rate Limit]
        CacheEP[CacheEndpoints\nString Cache]
        SearchInit[SearchIndexInitializer\nHostedService]
        MetricsBG[MetricsBackgroundService\nHostedService]
    end

    subgraph ServiceDefaults["RedisSmartDemo.ServiceDefaults"]
        OTel[OpenTelemetry\nTraces · Metrics · Logs]
        HealthChecks[Health Checks\n/health · /alive]
        ServiceDiscovery[Service Discovery]
        Resilience[HTTP Resilience]
    end

    subgraph Redis["Redis Stack (Docker)"]
        direction TB
        JSON[RedisJSON]
        FT[RediSearch]
        TS[RedisTimeSeries]
        BF[RedisBloom]
        Pub[Pub/Sub]
        Str[String / List / Hash]
    end

    UI -->|HTTPS| BlazorPages
    BlazorPages --> ApiClient
    ApiClient -->|HTTP service discovery| UserEP
    ApiClient -->|HTTP service discovery| ProductEP
    ApiClient -->|HTTP service discovery| MetricEP
    ApiClient -->|HTTP service discovery| ActivityEP
    ApiClient -->|HTTP service discovery| RecoEP
    ApiClient -->|HTTP service discovery| AdminEP
    ApiClient -->|HTTP service discovery| CacheEP

    UserEP -->|JSON.Set / JSON.Get| JSON
    ProductEP -->|FT.Search| FT
    MetricEP -->|TS.Add / TS.Range| TS
    ActivityEP -->|Publish / Subscribe| Pub
    ActivityEP -->|ListLeftPush| Str
    RecoEP -->|FT KNN| FT
    AdminEP -->|BF.Add / BF.Exists| BF
    CacheEP -->|StringGet / StringSet| Str
    UserEP -->|StringSet cache| Str
    ProductEP -->|StringSet cache| Str

    SearchInit -->|FT.Create + TS.Create| Redis
    MetricsBG -->|TS.Add every 5 s| TS

    Orchestrator -.->|starts & references| WebProject
    Orchestrator -.->|starts & references| ApiProject
    Orchestrator -.->|provisions container| Redis

    WebProject -.->|inherits| ServiceDefaults
    ApiProject -.->|inherits| ServiceDefaults
```

---

## 4. Component Descriptions

### 4.1 RedisSmartDemo.AppHost

The Aspire orchestration host. Responsibilities:

- Pulls `redis/redis-stack:latest` and starts the container with all Redis modules loaded (`rejson`, `redisearch`, `redistimeseries`, `redisbloom`).
- Registers `apiservice` and `webfrontend` projects as Aspire resources.
- Injects the Redis connection string into `apiservice` via `WithReference(redis)`.
- Enforces startup ordering: Redis → ApiService → WebFrontend.
- Configures external HTTP endpoints and health check probes.

### 4.2 RedisSmartDemo.ApiService

Minimal API backend with seven feature groups:

| Endpoint Class | Redis Feature | Key Operations |
|---|---|---|
| `UserEndpoints` | RedisJSON | `JSON.SET`, `JSON.GET`, cache-aside read |
| `ProductEndpoints` | RediSearch | `FT.SEARCH` with full-text, tag, and numeric filters |
| `MetricEndpoints` | RedisTimeSeries | `TS.ADD`, `TS.RANGE` |
| `ActivityEndpoints` | Pub/Sub + List | `PUBLISH`, `SUBSCRIBE`, `LPUSH`, `LRANGE` |
| `RecommendationEndpoints` | Vector Search | `FT.SEARCH` KNN with HNSW index |
| `AdminEndpoints` | Bloom Filter | `BF.ADD`, `BF.EXISTS`, sliding-window rate limiter |
| `CacheEndpoints` | String | `GET`/`SET` with TTL, bulk eviction, hit-rate stats |

Two hosted services run on startup:

- **`SearchIndexInitializer`** — creates `idx:products` (FT JSON index), `idx:products-vec` (HNSW vector index), and four TimeSeries keys, idempotently.
- **`MetricsBackgroundService`** — writes simulated `cpu`, `temperature`, `latency`, and `events` data points every 5 seconds.

### 4.3 RedisSmartDemo.Web

Blazor Server application providing an interactive dashboard. Nine pages map to the same feature areas as the API. Uses `ApiClient` (typed `HttpClient`) for all API calls. UI is built with Microsoft Fluent UI components; charts use `Blazor-ApexCharts`.

### 4.4 RedisSmartDemo.ServiceDefaults

Shared extension methods applied to both `ApiService` and `Web`:

- OpenTelemetry (traces, metrics, logs) with optional OTLP export.
- HTTP resilience via `AddStandardResilienceHandler`.
- Service discovery via `AddServiceDiscovery`.
- `/health` and `/alive` endpoints (development only).

---

## 5. Redis Data Architecture

```mermaid
erDiagram
    USER {
        string id PK
        string name
        string email
        object preferences
    }
    PRODUCT {
        string id PK
        string name
        string description
        string category
        decimal price
        array tags
    }
    ACTIVITY_EVENT {
        string type
        string action
        string entityId
        string message
        datetime timestamp
    }
    METRIC_POINT {
        long timestamp PK
        double value
    }
    VECTOR_EMBEDDING {
        string productId PK
        bytes embedding
    }
    BLOOM_EMAIL {
        string email PK
    }
    BLOOM_VIEW {
        string userId PK
        string productId PK
    }

    USER ||--o{ ACTIVITY_EVENT : "generates"
    PRODUCT ||--o{ ACTIVITY_EVENT : "generates"
    PRODUCT ||--|| VECTOR_EMBEDDING : "has"
    PRODUCT ||--o{ BLOOM_VIEW : "tracked by"
    USER ||--o{ BLOOM_VIEW : "owns"
    METRIC_POINT }o--|| PRODUCT : "may reference"
```

**Redis key space summary:**

```
user:{id}                  → JSON  (User document)
users                      → Set   (all user IDs)
product:{id}               → JSON  (Product document)
products                   → Set   (all product IDs)
cache:user:{id}            → String, TTL 60 s
cache:product:{id}         → String, TTL 60 s
stats:cache:hits           → String (counter)
stats:cache:misses         → String (counter)
activity:feed              → List  (capped at 100)
ts:cpu                     → TimeSeries, 24 h retention
ts:temperature             → TimeSeries, 24 h retention
ts:latency                 → TimeSeries, 24 h retention
ts:events                  → TimeSeries, 24 h retention
vec:product:{id}           → Hash  (64-dim FLOAT32 embedding)
bf:emails                  → Bloom Filter
bf:seen:{userId}           → Bloom Filter (per-user)
ratelimit:{userId}:{min}   → String (counter, TTL 1 min)
idx:products               → FT Index (JSON, prefix product:)
idx:products-vec           → FT Index (Hash, prefix vec:product:)
```

---

## 6. API Design

All endpoints follow REST conventions and return `application/json`. Error responses use `ProblemDetails` (RFC 7807). Interactive documentation is available at `/scalar` in development.

### Cache-Aside Pattern (Users & Products)

```
GET /users/{id}
  → check cache:user:{id}
    HIT  → inc stats:cache:hits  → return cached JSON
    MISS → inc stats:cache:misses
         → JSON.GET user:{id}
         → SET cache:user:{id} EX 60
         → return JSON
```

### Vector Embedding Strategy

Embeddings are SHA-256 derived, normalised to unit length, and stored as 64-dimensional `FLOAT32` vectors. In production this would be replaced with a real embedding model (e.g., Azure OpenAI `text-embedding-ada-002`).

### Rate Limiting

Sliding-window rate limiter using a per-minute counter key:

```
key = ratelimit:{userId}:{yyyyMMddHHmm}
INCR key  →  if count == 1 → EXPIRE key 60
return { used, remaining, isLimited }
```

---

## 7. Sequence Diagrams

### 7.1 User Creation (RedisJSON + Pub/Sub)

```mermaid
sequenceDiagram
    actor Browser
    participant Web as Blazor Web
    participant API as ApiService
    participant Redis

    Browser->>Web: Submit "Create User" form
    Web->>API: POST /users {id, name, email, preferences}
    API->>Redis: JSON.SET user:{id} $ {…}
    API->>Redis: SADD users {id}
    API->>Redis: PUBLISH activity "user.created"
    API->>Redis: LPUSH activity:feed {event}
    API->>Redis: LTRIM activity:feed 0 99
    API-->>Web: 201 Created {user}
    Web-->>Browser: Show success notification

    note over Redis: activity:feed capped at 100 items
```

### 7.2 Product Search (RediSearch)

```mermaid
sequenceDiagram
    actor Browser
    participant Web as Blazor Web
    participant API as ApiService
    participant Redis

    Browser->>Web: Enter search query + filters
    Web->>API: GET /products?q=laptop&category=Electronics&maxPrice=1500
    API->>Redis: FT.SEARCH idx:products "@Name:laptop @Category:{Electronics} @Price:[-inf 1500]" LIMIT 0 50
    Redis-->>API: [{id, score}, …]
    loop for each result
        API->>Redis: JSON.GET product:{id}
        Redis-->>API: {product JSON}
    end
    API-->>Web: [{product}, …]
    Web-->>Browser: Render product cards
```

### 7.3 Metrics Recording & Retrieval (RedisTimeSeries)

```mermaid
sequenceDiagram
    participant MetricsBG as MetricsBackgroundService
    participant Redis
    participant Web as Blazor Web
    participant API as ApiService

    loop every 5 seconds
        MetricsBG->>Redis: TS.ADD ts:cpu {timestamp} {value}
        MetricsBG->>Redis: TS.ADD ts:temperature {timestamp} {value}
        MetricsBG->>Redis: TS.ADD ts:latency {timestamp} {value}
        MetricsBG->>Redis: TS.ADD ts:events {timestamp} {value}
    end

    Web->>API: GET /metrics
    API->>Redis: TS.RANGE ts:cpu (now-5min) +
    API->>Redis: TS.RANGE ts:temperature (now-5min) +
    API->>Redis: TS.RANGE ts:latency (now-5min) +
    API->>Redis: TS.RANGE ts:events (now-5min) +
    Redis-->>API: [{timestamp, value}, …] × 4
    API-->>Web: {cpu:[…], temperature:[…], latency:[…], events:[…]}
    Web-->>Web: Render ApexCharts line charts
```

### 7.4 Activity Stream (Pub/Sub + SSE)

```mermaid
sequenceDiagram
    actor Browser
    participant Web as Blazor Web
    participant API as ApiService
    participant Redis

    Browser->>Web: Open Activity page
    Web->>API: GET /activity/stream (SSE)
    API->>Redis: SUBSCRIBE activity
    note over API: SSE connection held open

    actor OtherUser as Another User
    OtherUser->>API: POST /users (creates user)
    API->>Redis: PUBLISH activity {event JSON}
    Redis-->>API: message delivered
    API-->>Web: data: {event JSON}\n\n
    Web-->>Browser: Render new activity card in real time

    Browser->>Web: Close page / navigate away
    Web->>API: Abort SSE request
    API->>Redis: UNSUBSCRIBE activity
```

### 7.5 Product Recommendation (Vector KNN)

```mermaid
sequenceDiagram
    actor Browser
    participant Web as Blazor Web
    participant API as ApiService
    participant Redis

    Browser->>Web: Open Recommendations page (userId)
    Web->>API: GET /recommendations/{userId}?count=5

    note over API: Derive 64-dim FLOAT32 vector from SHA-256(userId),\nnormalize to unit length

    API->>Redis: FT.SEARCH idx:products-vec "*=>[KNN 5 @embedding $vec AS score]"\n  PARAMS 2 vec {queryVectorBytes} DIALECT 2
    Redis-->>API: [{productId, score}, …] sorted by cosine distance
    API-->>Web: [{productId, similarityScore}, …]
    Web-->>Browser: Render recommendation cards
```

### 7.6 Cache-Aside Read (User/Product)

```mermaid
sequenceDiagram
    actor Browser
    participant Web as Blazor Web
    participant API as ApiService
    participant Redis

    Browser->>Web: View product detail page
    Web->>API: GET /products/{id}
    API->>Redis: GET cache:product:{id}

    alt Cache HIT
        Redis-->>API: {cached JSON}
        API->>Redis: INCR stats:cache:hits
        API-->>Web: 200 OK {product} (from cache)
    else Cache MISS
        Redis-->>API: (nil)
        API->>Redis: INCR stats:cache:misses
        API->>Redis: JSON.GET product:{id}
        Redis-->>API: {product JSON}
        API->>Redis: SET cache:product:{id} {JSON} EX 60
        API->>Redis: PUBLISH activity "product.viewed"
        API-->>Web: 200 OK {product} (from store)
    end

    Web-->>Browser: Render product detail
```

### 7.7 Bloom Filter — Email Registration

```mermaid
sequenceDiagram
    actor Browser
    participant Web as Blazor Web
    participant API as ApiService
    participant Redis

    Browser->>Web: Enter email in Admin panel
    Web->>API: POST /bloom/check-email {email}
    API->>Redis: BF.EXISTS bf:emails {email}
    Redis-->>API: 0 (not seen) | 1 (likely seen)
    API-->>Web: {email, exists: false}
    Web-->>Browser: "Email is available"

    Browser->>Web: Click "Register Email"
    Web->>API: POST /bloom/register-email {email}
    API->>Redis: BF.ADD bf:emails {email}
    Redis-->>API: 1 (was new)
    API-->>Web: {email, wasNew: true, registered: true}
    Web-->>Browser: "Email registered"
```

---

## 8. Infrastructure Diagram

```mermaid
graph TB
    subgraph Developer["Developer Machine"]
        DotnetCLI["dotnet run\n(AppHost)"]
    end

    subgraph AspireRuntime["Aspire Runtime Process"]
        Dashboard["Aspire Dashboard\n:15888"]
        ServiceDiscovery["Service Discovery\n(in-process)"]
    end

    subgraph Containers["Docker Engine"]
        subgraph RedisContainer["redis/redis-stack:latest"]
            RedisPort["TCP :6379"]
            Modules["rejson.so\nredisearch.so\nredistimeseries.so\nredisbloom.so"]
        end
    end

    subgraph WebProcess["webfrontend Process"]
        BlazorApp["Blazor Server\nHTTPS :7xxx"]
        OTEL_Web["OTel SDK\n(traces · metrics · logs)"]
    end

    subgraph ApiProcess["apiservice Process"]
        MinimalAPI["Minimal API\nHTTPS :7yyy"]
        Hosted["Hosted Services\n(SearchIndexInitializer\nMetricsBackgroundService)"]
        OTEL_Api["OTel SDK\n(traces · metrics · logs)"]
    end

    subgraph Observability["Observability (optional)"]
        OTLP["OTLP Collector\n(e.g. Azure Monitor / Jaeger)"]
    end

    DotnetCLI -->|launches| AspireRuntime
    AspireRuntime -->|starts| WebProcess
    AspireRuntime -->|starts| ApiProcess
    AspireRuntime -->|starts container| Containers

    WebProcess -->|HTTP + service discovery| ApiProcess
    ApiProcess -->|TCP 6379| RedisContainer

    OTEL_Web -->|OTLP gRPC| OTLP
    OTEL_Api -->|OTLP gRPC| OTLP

    Dashboard -->|scrapes health + metrics| WebProcess
    Dashboard -->|scrapes health + metrics| ApiProcess
    Dashboard -->|monitor| RedisContainer

    ServiceDiscovery -.->|injects base URL| WebProcess
    ServiceDiscovery -.->|injects connection string| ApiProcess
```

### Infrastructure Notes

| Concern | Decision |
|---|---|
| Container image | `redis/redis-stack:latest` — all modules pre-built |
| Module loading | Passed as `--loadmodule` args by Aspire `WithArgs()` |
| Networking | Aspire manages loopback ports; no manual port mapping required |
| Health probes | `/health` on both services; Aspire polls before allowing dependent starts |
| Startup ordering | Redis → ApiService (WaitFor) → WebFrontend (WaitFor) |
| OTLP export | Enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` env var is set |
| Persistence | None (demo); all data lost on container restart |

---

## 9. Observability

| Signal | Source | Filters |
|---|---|---|
| **Traces** | ASP.NET Core + HttpClient | `/health` and `/alive` paths excluded |
| **Metrics** | ASP.NET Core + HttpClient + .NET Runtime | — |
| **Logs** | All services | Structured with scopes and formatted messages |

All telemetry is exported via OpenTelemetry OTLP if `OTEL_EXPORTER_OTLP_ENDPOINT` is set. The Aspire dashboard provides built-in trace and log viewers without any external tooling.

---

## 10. Security Considerations

| Risk | Mitigation |
|---|---|
| Redis exposed on loopback only | Aspire binds Redis to `127.0.0.1`; not accessible externally in dev |
| No authentication on API | Acceptable for demo; production must add bearer token or API key auth |
| Rate limiter bypass | Per-minute counter is advisory only; production would use middleware |
| Bloom filter false positives | Bloom filters may produce false positives; critical checks should fall back to authoritative store |
| Embedding determinism | SHA-256 embeddings are deterministic but not semantically meaningful; replace with real model in production |

---

## 11. Non-Functional Requirements

| Requirement | Target |
|---|---|
| Startup time | Full stack ready in < 30 seconds on developer hardware |
| Metrics flush interval | 5 seconds (configurable via background service delay) |
| Cache TTL | 60 seconds for user and product caches |
| TimeSeries retention | 24 hours (86 400 000 ms) per metric key |
| Activity feed depth | Last 100 events (LTRIM enforced on every push) |
| Rate limit window | 10 requests per user per minute |
| Vector dimension | 64 FLOAT32 values per product embedding |
| Search result page size | Up to 50 products per query |
