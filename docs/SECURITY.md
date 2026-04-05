# Security Policy (#131)

## Supported Versions

| Version | Supported |
|---------|-----------|
| 0.9.x   | ✅ Active development |
| < 0.9   | ❌ No support |

## Reporting a Vulnerability

**Do not file public GitHub issues for security vulnerabilities.**

Email: security@rvs-algotrader.internal *(internal team address)*

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact assessment
- Any suggested fixes (optional)

Response SLA: acknowledgement within 48 hours, fix timeline within 14 days for critical issues.

---

## Security Architecture

### Secrets Management (AP-006, AP-018)
- All secrets (DB passwords, broker API keys, JWT secret) are loaded from **environment variables** or **HashiCorp Vault** (`VaultSecretsProvider`)
- `appsettings.json` contains no secrets — only non-sensitive configuration
- Vault path convention: `secret/algotrader/<key>`

### Token Encryption (AP-018, #129)
- Broker JWT tokens are encrypted at rest using **AES-256-GCM** before writing to Redis (`RedisEncryptedTokenStore`)
- Encryption key: 32-byte key loaded from `TokenStore:EncryptionKey` env var (base64-encoded)
- Per-write random 12-byte nonce; 16-byte GCM authentication tag included in envelope
- Token TTL = token expiry time (auto-evicted from Redis)

### Authentication & Authorization (#128)
- All API endpoints require a valid **JWT Bearer token** (`JWT__SECRET` env var)
- 6-tier RBAC: `Viewer < Analyst < Trader < RiskManager < Admin < SuperAdmin`
- Role is carried in the `role` JWT claim (single value)
- Kill switch activation: RiskManager+; Order placement: Trader+; Settings: Admin+

### Audit Logging (AP-009)
- `audit_log` table is INSERT-only (PostgreSQL RULE blocks UPDATE/DELETE)
- Every order, strategy lifecycle event, and capital change is logged with correlation ID
- Correlation ID propagated via `X-Correlation-Id` header and Serilog enrichment (AP-008)

### Capital Safety (AP-005, AP-015)
- Capital reservation uses atomic Redis Lua script (no TOCTOU race)
- Kill switch dual-writes Redis (fast path) + PostgreSQL (durable fallback)
- `[Authorize(Policy = PolicyNames.RiskManager)]` on kill-switch activate/deactivate

### Input Validation (#130)
- All MediatR commands/queries run through `ValidationBehavior<TRequest, TResponse>`
- FluentValidation validators auto-registered from Application assembly
- `ValidationException` → HTTP 422 Unprocessable Entity with field-level messages

### Transport Security
- All HTTP traffic must use HTTPS in production (Nginx/reverse proxy terminates TLS)
- CORS policy: explicit origin whitelist (`CORS__ORIGINS` env var)
- Rate limiting: 300 requests/minute per IP (configurable via `AddRateLimiter`)

### Database Security
- EF Core + Npgsql — all queries use parameterized statements (no raw string interpolation)
- `candles` COPY path uses typed `NpgsqlDbType` parameters (no string building)
- DB connection string in `DATABASE_URL` environment variable only

### Dependency Scanning
- Run `dotnet list package --vulnerable` before each release
- `npm audit` for the React frontend before each release
- Dependencies pinned in `Directory.Packages.props` (central package management)

### Known Gaps / Roadmap
| Issue | Description | Status |
|-------|-------------|--------|
| #131  | SBOM generation | Planned (cyclonedx-dotnet tool) |
| #131  | Automated vulnerability scanning in CI | Planned (GitHub Actions SARIF upload) |
| #133  | Automated DB backup | Planned (pg_dump cron) |
