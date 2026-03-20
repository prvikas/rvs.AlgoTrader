# AlgoTrader

> Production-grade, multi-broker algorithmic trading platform for Indian markets (NSE/BSE).  
> Built on .NET 9, React 19, PostgreSQL + TimescaleDB, Redis, RabbitMQ. 100% OSS stack.

---

## Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Quick Start (Docker)](#quick-start-docker)
- [Project Structure](#project-structure)
- [Configuration](#configuration)
- [Running the API](#running-the-api)
- [Running the Frontend](#running-the-frontend)
- [Running Tests](#running-tests)
- [Broker Setup](#broker-setup)
- [CI/CD](#cicd)
- [Architecture Overview](#architecture-overview)
- [Contributing](#contributing)

---

## Overview

AlgoTrader is a SEBI-compliant, modular monolith with three bounded contexts:

| Context | Responsibility |
|---|---|
| **Trading Execution** | Order management, position tracking, risk management, strategy execution, kill switch |
| **Data Ingestion** | Broker WebSocket streaming, candle aggregation, historical data download, master data |
| **Backtesting Engine** | Fully isolated backtest/forward-test runner, Monte Carlo, walk-forward testing |

**Key capabilities:**
- Multi-broker support: Zerodha, Upstox, mStock (pluggable via `IFullBrokerClient`)
- Real-time market data streaming via broker WebSockets → SignalR → React dashboard
- Strategy scheduling with IST session windows, missed-session handling, auto-resume
- Idempotent order placement with Redis-backed deduplication
- Atomic capital locking (Redis Lua scripts) to prevent over-leveraging
- SEBI-compliant append-only audit log
- Full backtest reproducibility via SHA-256 data snapshots
- 18-panel React dashboard with role-based visibility

---

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | 9.0+ | Backend |
| Node.js | 20 LTS+ | Frontend |
| Docker Desktop | Latest | All infrastructure |
| Git | Any | Source control |

Optional (for production):
- HashiCorp Vault (OSS) — secrets management
- Prometheus + Grafana — monitoring (included in Docker Compose)

---

## Quick Start (Docker)

```bash
# 1. Clone
git clone https://github.com/your-org/algotrader.git
cd algotrader

# 2. Copy environment template
cp .env.example .env
# Edit .env — add broker API keys (see Broker Setup section)

# 3. Start all infrastructure
docker compose up -d

# 4. Run database migrations
dotnet ef database update -p src/rvs.AlgoTrader.Infrastructure -s src/rvs.AlgoTrader.API

# 5. Start the API
cd src/rvs.AlgoTrader.API
dotnet run

# 6. Start the frontend (separate terminal)
cd client/algotrader-ui
npm install
npm run dev
```

**Default ports:**
| Service | URL |
|---|---|
| API | http://localhost:5000 |
| Swagger (dev only) | http://localhost:5000/swagger |
| React UI | http://localhost:5173 |
| RabbitMQ Management | http://localhost:15672 (guest/guest) |
| Grafana | http://localhost:3000 (admin/admin) |
| Prometheus | http://localhost:9090 |
| Hangfire Dashboard | http://localhost:5000/hangfire (Admin login required) |

---

## Project Structure

```
rvs.AlgoTrader.sln
├── src/
│   ├── rvs.AlgoTrader.Domain/              # Entities, Value Objects, Interfaces, Domain Events
│   ├── rvs.AlgoTrader.Application/         # MediatR CQRS, DTOs, Validators, Service Interfaces
│   ├── rvs.AlgoTrader.Infrastructure/      # EF Core, Redis, RabbitMQ, Hangfire, Identity
│   ├── rvs.AlgoTrader.Brokers.Abstractions/# IBrokerOrderClient, IBrokerStreamClient, etc.
│   ├── rvs.AlgoTrader.Brokers.Zerodha/     # Zerodha Kite Connect implementation
│   ├── rvs.AlgoTrader.Brokers.Upstox/      # Upstox API v2 implementation
│   ├── rvs.AlgoTrader.Brokers.MStock/      # mStock API implementation
│   ├── rvs.AlgoTrader.Strategies/          # IStrategy implementations
│   ├── rvs.AlgoTrader.Backtesting/         # Fully isolated backtest + forward-test engine
│   └── rvs.AlgoTrader.API/                 # ASP.NET Core host, MVC controllers, SignalR, Minimal APIs
├── client/
│   └── algotrader-ui/                  # React 19 + Vite 6 + shadcn/ui + Tailwind v4
├── tests/
│   ├── rvs.AlgoTrader.UnitTests/           # xUnit + Moq + FluentAssertions + NetArchTest
│   ├── rvs.AlgoTrader.IntegrationTests/    # Testcontainers + Respawn
│   └── rvs.AlgoTrader.Tests.UI/            # Playwright end-to-end
├── docs/
│   ├── ARCHITECTURE.md
│   ├── PLAN.md
│   └── STRATEGY.md
├── hooks/
│   ├── pre-generate.sh                 # Validation before code generation
│   └── post-generate.sh                # Validation after code generation
├── skills/
│   ├── trading-domain.md
│   ├── broker-integration.md
│   ├── testing-patterns.md
│   ├── performance-patterns.md
│   └── sebi-compliance.md
├── CLAUDE.md                           # Claude Code project memory (AUTO-LOADED)
└── docker-compose.yml
```

---

## Configuration

### appsettings.json (non-secret infrastructure pointers only)

```json
{
  "Secrets": { "Provider": "Environment" },
  "ActiveBroker": "Zerodha",
  "Database": { "Host": "localhost", "Port": 5432, "Database": "algotrader" },
  "Redis": { "Host": "localhost", "Port": 6379 },
  "RabbitMQ": { "Host": "localhost", "VirtualHost": "/" },
  "Jwt": {
    "Issuer": "AlgoTrader",
    "Audience": "AlgoTraderUI",
    "AccessTokenExpiryMinutes": 60,
    "RefreshTokenExpiryDays": 30
  }
}
```

### .env (secrets — git-ignored)

```env
# Broker credentials
BROKERS__ZERODHA__APIKEY=your_key
BROKERS__ZERODHA__APISECRET=your_secret
BROKERS__UPSTOX__CLIENTID=your_client_id
BROKERS__UPSTOX__CLIENTSECRET=your_client_secret
BROKERS__MSTOCK__APIKEY=your_key

# JWT
JWT__SECRET=at_least_32_character_random_secret

# Database
DATABASE__PASSWORD=postgres_password

# Redis (optional password)
REDIS__PASSWORD=

# Field encryption key (AES-256 — must be 32 bytes base64)
FIELDENCRYPTION__KEY=base64_encoded_32_byte_key

# Telegram (optional)
TELEGRAM__BOTTOKEN=
TELEGRAM__CHATID=
```

### Production: HashiCorp Vault

Set `"Secrets": { "Provider": "Vault" }` and configure Vault address via environment:
```env
VAULT__ADDRESS=http://vault:8200
VAULT__TOKEN=your_vault_token
```

---

## Running the API

```bash
# Development
cd src/rvs.AlgoTrader.API
dotnet run --environment Development

# With hot reload
dotnet watch run --environment Development

# Production build
dotnet publish -c Release -o ./publish
./publish/rvs.AlgoTrader.API
```

**API Documentation:** http://localhost:5000/swagger (development only)

---

## Running the Frontend

```bash
cd client/algotrader-ui
npm install
npm run dev      # dev server at http://localhost:5173
npm run build    # production build
npm run preview  # preview production build
```

---

## Running Tests

```bash
# All tests
dotnet test

# Unit tests only
dotnet test tests/rvs.AlgoTrader.UnitTests

# Integration tests (requires Docker for Testcontainers)
dotnet test tests/rvs.AlgoTrader.IntegrationTests

# Architecture tests
dotnet test tests/rvs.AlgoTrader.UnitTests --filter Category=Architecture

# UI tests (requires browser binaries)
dotnet tool install --global Microsoft.Playwright.CLI
playwright install chromium
dotnet test tests/rvs.AlgoTrader.Tests.UI

# Coverage report
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=html
```

---

## Broker Setup

### Zerodha (Kite Connect)
1. Create app at https://developers.kite.trade
2. Set `BROKERS__ZERODHA__APIKEY` and `BROKERS__ZERODHA__APISECRET`
3. Zerodha requires daily manual login (TOTP). Use the Login URL alert in the dashboard.
4. After login, paste the `request_token` from the redirect URL into the Settings panel.

### Upstox
1. Create app at https://developer.upstox.com
2. Set `BROKERS__UPSTOX__CLIENTID` and `BROKERS__UPSTOX__CLIENTSECRET`
3. OAuth2 refresh token is auto-refreshed — no daily login required.

### mStock
1. Obtain API credentials from mStock support (Type B API).
2. Set `BROKERS__MSTOCK__APIKEY` and `BROKERS__MSTOCK__PRIVATKEY`
3. Auth: `Authorization: Bearer {jwtToken}` + `X-PrivateKey: {api_key}` on all calls.
4. No refresh token — session is re-exchanged on 401/403 response.

---

## CI/CD

GitHub Actions workflow at `.github/workflows/ci.yml`:

```
On: push to main, pull_request
Steps:
  1. dotnet build (zero warnings as errors)
  2. dotnet test rvs.AlgoTrader.UnitTests (includes NetArchTest)
  3. dotnet test rvs.AlgoTrader.IntegrationTests (Testcontainers)
  4. npm run build (frontend)
  5. npm run test (frontend unit tests)
  6. docker compose build (validate compose file)
```

---

## Architecture Overview

See `docs/ARCHITECTURE.md` for full details.

**Bounded context isolation rules:**
- Trading Execution ← reads candle cache from Redis; never calls Data Ingestion directly
- Backtesting Engine ← reads from TimescaleDB only; zero broker calls
- Data Ingestion ← writes candles; never places orders

**Key design decisions:**
- `IClock` abstraction for all time access (enables SimulatedClock in tests/backtest)
- MediatR only where CQRS adds real value (order placement, strategy exec, backtest runs)
- Redis AOF persistence for kill-switch, capital reservation, session tokens
- Atomic Lua scripts for capital locking (no read-then-write race conditions)
- NetArchTest enforces architecture rules in CI

---

## Contributing

1. Read `CLAUDE.md` — all anti-patterns and hard rules
2. Check `docs/PLAN.md` for current generation state
3. Follow the Definition of Done checklist in `CLAUDE.md`
4. Run `hooks/pre-generate.sh` before submitting a PR
5. Run `hooks/post-generate.sh` after generating new code to validate
