# Redis Smart Dashboard

A .NET Aspire demo application showcasing **seven Redis Stack capabilities** through a Blazor Server frontend and a minimal API backend.

---

## Features

| Feature | Redis Module | Endpoint Group |
|---|---|---|
| User management (store & retrieve structured JSON) | RedisJSON | `/users` |
| Full-text & faceted product search | RediSearch | `/products` |
| Real-time time-series metrics | RedisTimeSeries | `/metrics` |
| Live activity feed with streaming | Pub/Sub + List | `/activity` |
| Product recommendations (KNN) | Vector Search | `/recommendations` |
| Email & product-view deduplication | Bloom Filter | `/bloom` |
| Response caching with hit/miss stats | Redis String | `/cache` |

---

## Architecture

```
Browser
  └─ Blazor Server (RedisSmartDemo.Web)
       └─ ApiClient (HTTP / service discovery)
            └─ ASP.NET Core Minimal API (RedisSmartDemo.ApiService)
                 └─ StackExchange.Redis / NRedisStack
                      └─ Redis Stack (Docker - redis/redis-stack)
```

All services are orchestrated by **.NET Aspire** (`RedisSmartDemo.AppHost`).

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for the Redis Stack container)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)

```bash
dotnet workload install aspire
```

---

## Getting Started

```bash
git clone https://github.com/akhorozov/smart-dashboard.git
cd smart-dashboard
dotnet run --project RedisSmartDemo.AppHost
```

The Aspire dashboard opens automatically and shows all service URLs and health status.

| Service | Default URL |
|---|---|
| Aspire Dashboard | http://localhost:15888 |
| Blazor Web UI | https://localhost:7xxx |
| API (Scalar docs) | https://localhost:7yyy/scalar |

---

## Project Structure

```
RedisSmartDemo.sln
├── RedisSmartDemo.AppHost/          # Aspire orchestration & Redis container setup
├── RedisSmartDemo.ApiService/       # Minimal API backend
│   ├── Endpoints/                   # One file per feature area
│   ├── Models/                      # Domain models
│   └── Services/                    # Hosted services (index init, metrics pump)
├── RedisSmartDemo.Web/              # Blazor Server frontend
│   ├── Components/Pages/            # One page per feature area
│   ├── Models/Dtos.cs               # View models
│   └── Services/ApiClient.cs        # Typed HTTP client
└── RedisSmartDemo.ServiceDefaults/  # Shared OpenTelemetry, health checks, resilience
```

---

## Key NuGet Packages

| Package | Version | Purpose |
|---|---|---|
| `Aspire.StackExchange.Redis` | 13.2.2 | Aspire Redis integration |
| `NRedisStack` | 1.3.0 | RedisJSON, RediSearch, TimeSeries, Bloom, Vector |
| `StackExchange.Redis` | 2.12.14 | Core Redis client |
| `Microsoft.FluentUI.AspNetCore.Components` | 4.14.0 | Fluent UI for Blazor |
| `Blazor-ApexCharts` | 6.1.0 | Charting on the metrics page |

---

## Redis Data Model

| Key Pattern | Type | Used For |
|---|---|---|
| `user:{id}` | JSON | User documents |
| `users` | Set | Index of all user IDs |
| `product:{id}` | JSON | Product documents |
| `products` | Set | Index of all product IDs |
| `cache:user:{id}` / `cache:product:{id}` | String | 60-second response cache |
| `stats:cache:hits` / `stats:cache:misses` | String (counter) | Cache statistics |
| `activity:feed` | List | Last 100 activity events |
| `ts:cpu` / `ts:temperature` / `ts:latency` / `ts:events` | TimeSeries | Metric data points (1-day retention) |
| `vec:product:{id}` | Hash | 64-dim FLOAT32 embedding |
| `bf:emails` | Bloom Filter | Email deduplication |
| `bf:seen:{userId}` | Bloom Filter | Per-user product view tracking |
| `ratelimit:{userId}:{minute}` | String (counter) | Sliding-window rate limiter |
| `idx:products` | FT Index | Full-text + numeric + tag index on JSON |
| `idx:products-vec` | FT Index | HNSW cosine vector index on Hash |

---

## API Reference

Full interactive docs are available at `/scalar` when running in development mode.

### Users
| Method | Path | Description |
|---|---|---|
| `POST` | `/users` | Create user (stored as JSON) |
| `GET` | `/users` | List all users |
| `GET` | `/users/{id}` | Get user by ID (cache-aside) |
| `PUT` | `/users/{id}` | Update user |
| `DELETE` | `/users/{id}` | Delete user |

### Products
| Method | Path | Description |
|---|---|---|
| `POST` | `/products` | Create product |
| `GET` | `/products?q=&category=&minPrice=&maxPrice=` | Full-text + faceted search |
| `GET` | `/products/{id}` | Get product by ID (cache-aside) |
| `PUT` | `/products/{id}` | Update product |
| `DELETE` | `/products/{id}` | Delete product |

### Metrics
| Method | Path | Description |
|---|---|---|
| `POST` | `/metrics/record` | Record a data point |
| `GET` | `/metrics/{name}?from=&to=` | Query time range |
| `GET` | `/metrics` | All metrics, last 5 minutes |

### Activity
| Method | Path | Description |
|---|---|---|
| `POST` | `/activity/publish` | Publish event to Pub/Sub + feed |
| `GET` | `/activity/recent?count=` | Last N events from feed |
| `GET` | `/activity/stream` | SSE real-time stream |

### Recommendations
| Method | Path | Description |
|---|---|---|
| `POST` | `/recommendations/embed/{productId}` | Store vector embedding |
| `GET` | `/recommendations/{userId}?count=` | KNN similarity query |

### Admin (Bloom + Rate Limiting)
| Method | Path | Description |
|---|---|---|
| `POST` | `/bloom/check-email` | Bloom membership test |
| `POST` | `/bloom/register-email` | Add to Bloom filter |
| `POST` | `/bloom/check-product-view` | Per-user seen check |
| `POST` | `/bloom/record-product-view` | Record product view |
| `GET` | `/ratelimit/check/{userId}` | Sliding-window rate limit check |

### Cache
| Method | Path | Description |
|---|---|---|
| `GET` | `/cache/stats` | Hit/miss statistics |
| `DELETE` | `/cache/{key}` | Evict a specific key |
| `DELETE` | `/cache` | Flush all cache keys + reset stats |

---

## Observability

The app ships with OpenTelemetry configured out of the box via `ServiceDefaults`:

- **Traces** — ASP.NET Core + HttpClient (health-check paths filtered out)
- **Metrics** — ASP.NET Core, HttpClient, .NET Runtime
- **Logs** — structured with scopes and formatted messages

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to export to any OTLP-compatible backend.

---

## License

MIT
