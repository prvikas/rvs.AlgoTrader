# Skill: Development Workflow — AlgoTrader

## Purpose
Day-to-day development standards: branching strategy, DI registration, naming conventions,
PR standards, code review rules, and commit discipline.
Load this skill at the START of every Claude Code session before writing any code.

---

## Namespace Standard

```csharp
// Every C# file root namespace follows this exact pattern:
namespace rvs.AlgoTrader.<Layer>[.<SubFolder>]

// Examples:
namespace rvs.AlgoTrader.Domain.Entities
namespace rvs.AlgoTrader.Domain.Events
namespace rvs.AlgoTrader.Application.Commands
namespace rvs.AlgoTrader.Application.Interfaces
namespace rvs.AlgoTrader.Infrastructure.Persistence.Repositories
namespace rvs.AlgoTrader.Infrastructure.Redis
namespace rvs.AlgoTrader.Brokers.Zerodha
namespace rvs.AlgoTrader.Strategies
namespace rvs.AlgoTrader.Backtesting
namespace rvs.AlgoTrader.API.Controllers
namespace rvs.AlgoTrader.API.Hubs
namespace rvs.AlgoTrader.API.Middleware
```

**Never use**: bare `AlgoTrader.*`, `RVS.*`, or any other root.

---

## Coding Standards

### Async Method Rules
```csharp
// RULE 1: Every async method must have CancellationToken as last parameter
// ✅ CORRECT
public async Task<Order> GetByIdAsync(Guid id, CancellationToken ct)
public async Task<bool> TryReserveAsync(Guid instanceId, string broker, decimal amount, CancellationToken ct)

// ❌ WRONG — missing CancellationToken
public async Task<Order> GetByIdAsync(Guid id)

// RULE 2: Always suffix async methods with Async
// ✅ CORRECT: PlaceOrderAsync, EvaluateAsync, GetAvailableCapitalAsync
// ❌ WRONG: PlaceOrder, Evaluate, GetAvailableCapital  (on async methods)

// RULE 3: Never use .Result or .Wait() — always await
// ❌ FORBIDDEN
var order = GetOrderAsync(id, ct).Result;
GetOrderAsync(id, ct).Wait();

// ✅ REQUIRED
var order = await GetOrderAsync(id, ct);

// RULE 4: ConfigureAwait(false) in Infrastructure/Application library code
// (not in API or test code where SynchronizationContext is needed)
var result = await _redis.GetAsync(key).ConfigureAwait(false);

// RULE 5: Pass CancellationToken through — never use CancellationToken.None in business logic
// ❌ WRONG
await _orderRepo.AddAsync(order, CancellationToken.None);
// ✅ CORRECT
await _orderRepo.AddAsync(order, ct);
```

### Logging Format Rules
```csharp
// RULE 1: Use structured logging — never string interpolation in log messages
// ❌ FORBIDDEN
_logger.LogInformation($"Order {orderId} placed for {symbol} at {price}");
_logger.LogError($"Failed: {ex.Message}");

// ✅ REQUIRED
_logger.LogInformation("Order {OrderId} placed for {Symbol} at {Price:F2}",
    orderId, symbol, price);
_logger.LogError(ex, "Order placement failed for {Symbol} at {Price}", symbol, price);

// RULE 2: Always enrich with CorrelationId
// Correlation ID is set by CorrelationIdMiddleware and flows via Activity.Current
// Serilog picks it up automatically via WithCorrelationId enricher
// In background jobs / consumers — manually add:
using (_logger.BeginScope(new Dictionary<string, object>
    { ["CorrelationId"] = correlationId, ["StrategyInstanceId"] = instanceId }))
{
    _logger.LogInformation("Strategy evaluation started for {Symbol}", symbol);
}

// RULE 3: Log levels — use them correctly
// Trace:   hot path internals (only in development, not production)
// Debug:   diagnostic info useful during development
// Information: normal operational events (order placed, session started, startup steps)
// Warning: recoverable issues (Redis degraded, late restart, missed session)
// Error:   exceptions that should be investigated (broker call failed, DB error)
// Critical: system cannot continue (PostgreSQL unreachable, unhandled fatal exception)

// RULE 4: Always include the exception object in Error/Critical
// ❌ WRONG
_logger.LogError("Broker call failed: " + ex.Message);
// ✅ CORRECT
_logger.LogError(ex, "Broker call failed for {BrokerName} operation {Operation}",
    brokerName, operation);

// RULE 5: Never log sensitive data (API keys, tokens, passwords, broker credentials)
// ❌ FORBIDDEN
_logger.LogDebug("Using API key {ApiKey}", apiKey);
```

### Error Handling Rules
```csharp
// RULE 1: Use typed domain exceptions — never throw Exception or ApplicationException
// ❌ FORBIDDEN
throw new Exception("Order not found");
throw new ApplicationException("Capital unavailable");

// ✅ REQUIRED (defined in rvs.AlgoTrader.Domain/Exceptions/)
throw new NotFoundException(nameof(Order), orderId);
throw new InsufficientCapitalException(requested, available);
throw new KillSwitchActiveException();
throw new MarketClosedException();

// RULE 2: GlobalExceptionFilter maps domain exceptions → structured API responses
// NotFoundException           → HTTP 404 + error envelope
// KillSwitchActiveException   → HTTP 503 + error envelope
// InsufficientCapitalException → HTTP 422 + error envelope
// MarketClosedException       → HTTP 422 + error envelope
// Unhandled exceptions        → HTTP 500 + generic error message (NEVER raw stack trace)

// RULE 3: Never swallow exceptions silently
// ❌ FORBIDDEN
try { await DoSomethingAsync(ct); } catch { }
try { await DoSomethingAsync(ct); } catch (Exception ex) { /* ignore */ }

// ✅ REQUIRED — at minimum, log before rethrowing or returning gracefully
try
{
    await DoSomethingAsync(ct);
}
catch (OperationCanceledException) { throw; }  // Always propagate cancellation
catch (Exception ex) when (ex is not DomainException)
{
    _logger.LogError(ex, "Unexpected error in {Operation}", nameof(DoSomethingAsync));
    throw;
}

// RULE 4: Always re-throw OperationCanceledException — never catch it silently
// It means the CancellationToken was triggered (graceful shutdown / timeout)

// RULE 5: Use Result<T> pattern for expected failures (not exceptions)
// Exceptions are for unexpected/exceptional conditions
// Expected failures (capital unavailable, throttled) → return SkippedResult(reason)
// Unexpected failures (DB down, network error) → throw and let Polly/global handler deal with it
```

### File Naming Rules
```
C# Files:
  Classes/Interfaces   → match the type name exactly
    Order.cs, IOrderRepository.cs, PlaceOrderCommand.cs
  
  EF Configurations    → {Entity}Configuration.cs
    OrderConfiguration.cs, StrategyInstanceConfiguration.cs
  
  Migration files      → auto-generated by dotnet ef — NEVER rename manually
  
  Extension methods    → {Subject}Extensions.cs
    InfrastructureServiceExtensions.cs, ClockExtensions.cs
  
  Test files           → {ClassUnderTest}Tests.cs
    PriceActionBreakoutStrategyTests.cs, CapitalAllocatorTests.cs

TypeScript/React Files:
  Components           → PascalCase.tsx
    OrderPanel.tsx, StrategyInstanceManager.tsx
  
  Custom hooks         → use-{name}.ts (kebab-case)
    use-order-submit.ts, use-live-quote.ts
  
  Zustand stores       → {domain}-store.ts
    strategy-store.ts, alert-store.ts
  
  API functions        → {domain}-api.ts
    orders-api.ts, strategy-instances-api.ts
  
  Types                → {domain}.types.ts or types/index.ts
  
  Test files           → {component}.test.tsx or {hook}.test.ts
    order-panel.test.tsx, use-order-submit.test.ts

Configuration/Infrastructure Files:
  docker-compose.yml   (root of repo)
  .env.example         (root of repo, git-tracked)
  .env                 (root of repo, git-IGNORED)
  appsettings.json     (AlgoTrader.API project)
  appsettings.Development.json  (git-IGNORED)
```

### Folder Naming Rules
```
C# Solution folder structure:
  src/          → all source projects
  tests/        → all test projects
  client/       → frontend (algotrader-ui)
  scaffolding/  → .csproj templates, docker-compose, CI YAML, contract schemas
  .github/      → GitHub Actions workflows
  hooks/        → Claude Code generation scripts
  docs/         → architecture, plan, strategy docs
  skills/       → Claude Code skill files

C# Project internal folders → PascalCase always:
  Entities/, ValueObjects/, Events/, Enums/, Interfaces/, Exceptions/
  Commands/, Queries/, Handlers/, Validators/, DTOs/, Mappers/, Services/
  Persistence/, Repositories/, Configurations/, Migrations/
  Redis/, Messaging/, Brokers/, Identity/, Hangfire/, Secrets/
  DependencyInjection/, Controllers/, Hubs/, Middleware/, Filters/

Frontend src/ folders → kebab-case always:
  components/, stores/, api/, hooks/, types/, utils/, pages/
  components/ui/, components/charts/, components/panels/
  Per-panel folder: order-panel/, chart-view/, strategy-manager/

Never mix conventions within a layer.
```

---

## Branch Strategy

```
main            ← production-ready, protected. No direct commits.
develop         ← integration branch. All features merge here first.
  │
  ├── feature/AT-{ticket}-{short-description}   e.g. feature/AT-42-capital-allocator-lua
  ├── fix/AT-{ticket}-{short-description}        e.g. fix/AT-101-clock-violation-aggregator
  ├── chore/AT-{ticket}-{short-description}      e.g. chore/AT-55-pin-nuget-versions
  └── test/AT-{ticket}-{short-description}       e.g. test/AT-66-strategy-scheduler-edge-cases
```

**Rules:**
- One branch per PLAN.md step (or per ticket)
- Branch from `develop`, merge back to `develop` via PR
- Never commit directly to `main` or `develop`
- Branch names use lowercase kebab-case only

---

## Commit Message Format

```
type(scope): short description (max 72 chars)

[optional body — what and why, not how]

[optional footer]
Closes: AT-42
```

**Types:** `feat`, `fix`, `test`, `refactor`, `chore`, `docs`, `perf`  
**Scopes:** `domain`, `application`, `infrastructure`, `api`, `broker`, `strategy`, `backtesting`, `frontend`, `ci`

**Examples:**
```
feat(infrastructure): add RedisCapitalAllocator with Lua atomic reserve

fix(strategy): inject IClock instead of DateTime.Now in CandleAggregatorService
Closes: AT-88

test(application): add IStrategyScheduler edge case for holiday + manual-pause guard

chore(infrastructure): pin MassTransit to 8.3.0 per CLAUDE.md NuGet versions
```

---

## DI Registration — Standard Pattern

Every new service must be registered before the PR is merged. Use extension methods, not inline `Program.cs` registration.

```csharp
// src/rvs.AlgoTrader.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddDatabase(configuration)
            .AddRedis(configuration)
            .AddRabbitMq(configuration)
            .AddRepositories()
            .AddCaching()
            .AddBrokers(configuration)
            .AddHangfire(configuration)
            .AddHealthChecks(configuration);
        
        return services;
    }
}

// Each sub-extension in its own file:
// InfrastructureServiceExtensions.Database.cs
// InfrastructureServiceExtensions.Redis.cs
// InfrastructureServiceExtensions.Brokers.cs
// etc.
```

**Registration lifetime rules:**
| Lifetime | Use For |
|---|---|
| `Singleton` | `IClock` (SystemClock), `IBrokerClientFactory`, `ICandleCache`, `IFieldEncryptionService`, `ISecretsProvider` |
| `Scoped` | Repositories, `IStrategyInstanceManager`, `ICapitalAllocator` (request scope), `IAuditService` |
| `Transient` | DTOs, validators, `ITransactionCostCalculator`, strategy instances |

```csharp
// Application services registered in rvs.AlgoTrader.Application:
public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PlaceOrderCommand>());
        services.AddValidatorsFromAssemblyContaining<PlaceOrderCommandValidator>();
        // Mapperly mappers (source-gen, register as singletons)
        services.AddSingleton<OrderMapper>();
        services.AddSingleton<StrategyInstanceMapper>();
        return services;
    }
}
```

---

## Naming Conventions

### C# — Backend

| Element | Convention | Example |
|---|---|---|
| Classes | PascalCase | `PriceActionBreakoutStrategy` |
| Interfaces | `I` + PascalCase | `ICapitalAllocator` |
| Private fields | `_camelCase` | `_clock`, `_repository` |
| Constants | `UPPER_SNAKE` or `PascalCase` const | `MaxBarsPerKey`, `AuditAction.OrderPlaced` |
| Methods | PascalCase | `TryReserveAsync` |
| Async methods | suffix `Async` | `PlaceOrderAsync`, `EvaluateAsync` |
| Test methods | `MethodName_Scenario_ExpectedResult` | `EvaluateAsync_WhenVolumeLow_ReturnsHold` |
| MediatR Commands | suffix `Command` | `PlaceOrderCommand` |
| MediatR Queries | suffix `Query` | `GetOrdersQuery` |
| MediatR Handlers | suffix `CommandHandler`/`QueryHandler` | `PlaceOrderCommandHandler` |
| DTOs | suffix `Dto` | `OrderDto`, `CreateOrderDto` |
| Validators | suffix `Validator` | `PlaceOrderCommandValidator` |
| Domain Events | past tense | `OrderPlaced`, `PositionClosed` |
| EF Configurations | suffix `Configuration` | `OrderConfiguration` |

### TypeScript — Frontend

| Element | Convention | Example |
|---|---|---|
| Components | PascalCase | `OrderPanel`, `StrategyInstanceManager` |
| Hooks | `use` prefix | `useOrderSubmit`, `useLiveQuote` |
| Stores (Zustand) | suffix `Store` | `useStrategyStore`, `useAlertStore` |
| API functions | camelCase | `placeOrder`, `getSignalJournal` |
| Types/Interfaces | PascalCase | `OrderDto`, `StrategyStatus` |
| Enums | PascalCase + string values | `SignalType.Buy = "BUY"` |
| Constants | UPPER_SNAKE | `MAX_CANDLES_PER_CHART`, `DEFAULT_TIMEFRAME` |
| data-testid | kebab-case | `kill-switch-btn`, `order-form-qty` |
| Files | kebab-case | `order-panel.tsx`, `use-order-submit.ts` |

---

## File Organization

### Backend — Where Does Each File Go?

```
src/rvs.AlgoTrader.Domain/
├── Entities/           Order.cs, Position.cs, Instrument.cs, ...
├── ValueObjects/       Money.cs, Price.cs, InstrumentSymbol.cs, ...
├── Events/             OrderPlaced.cs, CandleClosedEvent.cs, ...
├── Enums/              OrderType.cs, SignalType.cs, StrategyStatus.cs, ...
└── Interfaces/         IClock.cs, IStrategy.cs, IExecutionEngine.cs, ...

src/rvs.AlgoTrader.Application/
├── Commands/           PlaceOrderCommand.cs, StartStrategyInstanceCommand.cs, ...
├── Queries/            GetOrdersQuery.cs, GetSignalJournalQuery.cs, ...
├── Handlers/           PlaceOrderCommandHandler.cs, ...
├── Validators/         PlaceOrderCommandValidator.cs, ...
├── DTOs/               OrderDto.cs, StrategyInstanceDto.cs, ...
├── Mappers/            OrderMapper.cs (Mapperly), ...
├── Interfaces/         IOrderRepository.cs, ICapitalAllocator.cs, ...
└── Services/           (Application-layer service classes, no EF)

src/rvs.AlgoTrader.Infrastructure/
├── Persistence/
│   ├── AlgoTraderDbContext.cs
│   ├── Configurations/   OrderConfiguration.cs, ...
│   ├── Repositories/     OrderRepository.cs, ...
│   └── Migrations/       (EF migrations — never edit manually)
├── Redis/              CandleCache.cs, RedisCapitalAllocator.cs, ...
├── Messaging/          MassTransit setup, consumers, publishers
├── Brokers/            BrokerClientFactory.cs, decorators
├── Identity/           ASP.NET Core Identity setup
├── Hangfire/           Job registrations, filters
├── Secrets/            VaultSecretsProvider.cs, EnvironmentSecretsProvider.cs
└── DependencyInjection/ extension method files

src/rvs.AlgoTrader.API/
├── Controllers/        OrdersController.cs, StrategyInstancesController.cs, ...
├── Hubs/               QuoteHub.cs, AlertHub.cs, ...
├── Middleware/          CorrelationIdMiddleware.cs, IdempotencyMiddleware.cs, ...
├── Filters/            GlobalExceptionFilter.cs
└── Program.cs
```

### Frontend — Where Does Each File Go?

```
client/algotrader-ui/src/
├── components/
│   ├── ui/             shadcn/ui re-exports + custom base components
│   ├── charts/         TradingView Lightweight Charts wrappers
│   └── panels/         Dashboard panels (one folder per panel)
│       ├── order-panel/
│       │   ├── OrderPanel.tsx
│       │   ├── useOrderSubmit.ts
│       │   └── order-panel.test.tsx
├── stores/             Zustand stores (one per domain)
├── api/                TanStack Query hooks + axios client
├── hooks/              Shared custom hooks (useSignalR, useTheme, etc.)
├── types/              TypeScript types + enums (mirror backend DTOs)
├── utils/              Pure utility functions
└── pages/              Route-level components (thin — delegate to panels)
```

---

## PR / Code Review Standards

### PR Template
```markdown
## What this PR does
[1-3 sentences]

## PLAN.md steps completed
- [ ] Step N: [description]

## CLAUDE.md compliance checklist
- [ ] No DateTime.Now / DateTimeOffset.UtcNow (IClock used everywhere)
- [ ] No cross-context service calls
- [ ] All new Commands/Queries have FluentValidation validators
- [ ] Broker HTTP clients have Polly policies
- [ ] audit_log only receives INSERT (no UPDATE/DELETE)
- [ ] Response envelope used on all new endpoints
- [ ] NuGet versions match CLAUDE.md pinned versions

## Tests
- [ ] Unit tests written and passing
- [ ] Integration tests written (if infra component)
- [ ] NetArchTest still passing

## Definition of Done
- [ ] DI registration added
- [ ] No compiler warnings
- [ ] Swagger XML comments on new controller actions
- [ ] post-generate.sh passes with zero failures
```

### Code Review Rules — What to Reject

Reviewers MUST reject a PR that contains:
1. `DateTime.Now` or `DateTimeOffset.UtcNow` outside of `SystemClock.cs`
2. Any broker interface injected into `rvs.AlgoTrader.Backtesting` project
3. `UPDATE` or `DELETE` against `audit_log`
4. Missing `Idempotency-Key` check on order endpoints
5. Hardcoded secrets (API keys, passwords, connection strings)
6. `throw new Exception(...)` — must use typed domain exceptions
7. Missing `CancellationToken` parameter on any `async Task` method that calls DB/Redis/broker
8. EF Core `DbContext` referenced in Application or Domain projects
9. `AutoMapper` — project uses Mapperly only
10. Synchronous blocking calls (`.Result`, `.Wait()`) anywhere

---

## Typed Domain Exceptions

```csharp
// Define in rvs.AlgoTrader.Domain/Exceptions/
public class DomainException : Exception
{
    public string Code { get; }
    public DomainException(string code, string message) : base(message) => Code = code;
}

public class NotFoundException : DomainException
{
    public NotFoundException(string resource, object id) 
        : base("NOT_FOUND", $"{resource} with id '{id}' was not found.") { }
}

public class KillSwitchActiveException : DomainException
{
    public KillSwitchActiveException() 
        : base("KILL_SWITCH_ACTIVE", "System kill switch is active. All trading is halted.") { }
}

public class InsufficientCapitalException : DomainException
{
    public InsufficientCapitalException(decimal requested, decimal available)
        : base("INSUFFICIENT_CAPITAL", 
            $"Cannot reserve ₹{requested:N0}. Available capital: ₹{available:N0}") { }
}

public class MarketClosedException : DomainException
{
    public MarketClosedException() 
        : base("MARKET_CLOSED", "Market is closed. Cannot place orders.") { }
}

// Global exception handler in API maps DomainException → structured 4xx responses:
// NotFoundException → 404
// KillSwitchActiveException → 503
// InsufficientCapitalException → 422
// MarketClosedException → 422
```

---

## Environment-Specific Behavior

```csharp
// Never check environment strings in business logic — use feature flags / config
// CORRECT:
if (_appConfig.GetAsync<bool>("Features:LiveTradingEnabled"))
    await _broker.PlaceOrderAsync(...);

// WRONG:
if (_environment.IsProduction())
    await _broker.PlaceOrderAsync(...);

// appsettings.Development.json — dev-only defaults (git-ignored):
{
  "Features": {
    "LiveTradingEnabled": false,    // NEVER true in dev
    "SwaggerEnabled": true
  },
  "Jwt": { "Secret": "dev-only-not-a-real-secret-32chars!!" }
}
```

---

## Code Generation Speed — Tips for Claude Code

When asking Claude Code to generate a component, always specify:
1. Which PLAN.md step you're on
2. Which skills to load (`Load skills/trading-domain.md`)
3. What already exists (so it doesn't regenerate)
4. Where the file should be saved
5. Whether you want tests in the same response or as a follow-up

**Efficient prompt structure:**
```
[Step N of PLAN.md] Generate [ComponentName].
Location: src/[Project]/[Folder]/[FileName].cs
Implements: [IInterfaceName]
Dependencies to inject: [IClock, ISecretsProvider, ...]
Key behavior: [2-3 sentences]
Tests in: tests/rvs.AlgoTrader.UnitTests/[Folder]/[FileName]Tests.cs
Load: skills/[relevant].md
```

---

## Do Not Re-generate — Check First

Before asking Claude Code to generate a file, verify it doesn't already exist:
```bash
# Check if a file already exists
find src/ tests/ -name "*.cs" | grep -i "CapitalAllocator"

# Check if an interface is already defined
grep -rn "ICapitalAllocator" src/ --include="*.cs"

# Check DI registration
grep -rn "ICapitalAllocator\|RedisCapitalAllocator" src/rvs.AlgoTrader.Infrastructure/ --include="*.cs"
```

If it exists and needs changes, use **edit mode** — don't regenerate from scratch.
