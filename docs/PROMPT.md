# How to Use This Kit with Claude Code

## Step 1: Place Files in Repo Root

Copy ALL of the following to the ROOT of your AlgoTrader repo:
```
CLAUDE.md             ← CRITICAL — auto-loaded by Claude Code on every session
SELF_LEARNING.md      ← Do-not-repeat system + session tracking
README.md
docs/ARCHITECTURE.md
docs/PLAN.md
docs/STRATEGY.md
skills/
  trading-domain.md
  broker-integration.md
  testing-patterns.md
  performance-patterns.md
  sebi-compliance.md
  development-workflow.md   ← branching, DI, naming, PR standards, Coding Standards
  qa-checklist.md           ← per-component QA gates
  observability.md          ← OTel, Prometheus, Grafana, CI pipeline, 12 alert thresholds
hooks/
  pre-generate.sh
  post-generate.sh
  learn-from-mistake.sh     ← one-command mistake capture to CLAUDE.md
scaffolding/
  csproj-templates.md       ← all .csproj files, Directory.Build.props, solution file
  contracts.md              ← ApiResponse<T> envelope, all DTOs, PriceActionBreakoutConfig,
                              schedule_json, failure_behavior_json schemas
  domain-events.md          ← all 14 domain event C# records (rvs.AlgoTrader.Domain.Events)
  broker-models.md          ← BrokerTick, OrderRequest, OrderResult, all 4 broker interfaces,
                              IFullBrokerClient, decorator pattern
  docker-compose.yml        ← canonical docker-compose (copy directly to repo root)
  ci-cd.yml                 ← GitHub Actions pipeline (copy to .github/workflows/ci.yml)
```

Claude Code auto-loads `CLAUDE.md` at the start of every session. This is how it:
- Remembers the architectural contracts
- Knows which anti-patterns NOT to repeat
- Understands the correct patterns for IClock, bounded contexts, idempotency, etc.

---

## Step 2: Start a Claude Code Session

Open your terminal in the repo root and run:
```bash
claude
```

Claude Code will automatically read `CLAUDE.md` and load all project context.

---

## Step 3: Use This Master Prompt

Paste this prompt to kick off generation from scratch:

```
You are building a production-grade multi-broker algo-trading platform for Indian markets.
All architectural contracts, anti-patterns, and coding rules are in CLAUDE.md — read it first
and follow it strictly throughout this session.

The complete spec is in: docs/PLAN.md (generation order), docs/ARCHITECTURE.md (ADRs),
docs/STRATEGY.md (acceptance criteria).

Start with Step 1 of PLAN.md: generate the full solution structure.
Use scaffolding/csproj-templates.md for all .csproj files and Directory.Build.props — copy
exact project definitions from there.
Use scaffolding/docker-compose.yml as the canonical docker-compose — copy it to repo root.
Use scaffolding/ci-cd.yml as the GitHub Actions pipeline — copy to .github/workflows/ci.yml.
Generate .env.example and .gitignore.

After each step, run: ./hooks/post-generate.sh
Before moving to the next step, confirm the current step's validation output is clean.

Rules to follow for every file generated:
1. Never use DateTime.Now — always inject IClock
2. Namespace is rvs.AlgoTrader.* for all C# projects — no exceptions
3. No cross-context service calls (Backtesting ↛ Brokers, Trading ↛ DataIngestion directly)
4. MVC for business endpoints, Minimal APIs for /health and /metrics only
5. Every Command/Query has a FluentValidation validator
6. All broker HTTP clients have Polly retry + circuit breaker + timeout policies
7. All order placements check Idempotency-Key in Redis before processing
8. audit_log is INSERT-only — never UPDATE or DELETE
9. All responses use ApiResponse<T> envelope with correlationId (see scaffolding/contracts.md)
10. All time-related code uses NodaTime Instant/ZonedDateTime — no DateTime/DateTimeOffset
11. Pin NuGet versions as specified in CLAUDE.md
12. All domain events match exact C# record signatures in scaffolding/domain-events.md
13. All broker interfaces match exact signatures in scaffolding/broker-models.md
14. PriceActionBreakoutConfig fields match exact schema in scaffolding/contracts.md
```

---

## Step 4: Resume a Session (Learning from Mistakes)

**Option A — Automated (use this):**
```bash
./hooks/learn-from-mistake.sh
```
The script prompts you for title, bad code, fix, and reason — then auto-appends to `CLAUDE.md` with the next AP number and commits.

**Option B — Manual:** open `CLAUDE.md` and append under `## Anti-Patterns Seen in Past Sessions`:
```markdown
### AP-016: [Short description of mistake]
**Mistake:** [What was generated incorrectly]
**Fix:** [What the correct code should be]
**Why:** [Reason — links to architectural principle]
```

Then in the next session, paste the resume prompt from `SELF_LEARNING.md`:
```
Resume AlgoTrader generation.
Last completed: Step [N] — [name].
Next step: Step [N+1] — [name].
CLAUDE.md updated with AP-016: [description]. Read it and acknowledge before writing code.
```

---

## Step 5: Component-Specific Prompts

Use these patterns for generating specific components:

### Generating solution scaffold (Step 1):
```
Generate the full solution structure for rvs.AlgoTrader.
Copy .csproj files EXACTLY from scaffolding/csproj-templates.md — no modifications.
Copy docker-compose.yml EXACTLY from scaffolding/docker-compose.yml to repo root.
Copy .github/workflows/ci.yml EXACTLY from scaffolding/ci-cd.yml.
Generate .env.example and .gitignore.
```

### Generating domain layer (Step 2):
```
Generate all domain events in rvs.AlgoTrader.Domain.Events.
Use EXACT C# record signatures from scaffolding/domain-events.md — do not invent new fields.
Namespace: rvs.AlgoTrader.Domain.Events
All events are immutable records. Include NodaTime ZonedDateTime OccurredAt and CorrelationId.
```

### Generating application contracts (Step 3):
```
Generate all DTOs, commands, and queries in rvs.AlgoTrader.Application.
Use EXACT DTO shapes from scaffolding/contracts.md.
ApiResponse<T> envelope is the canonical response for all MVC controllers.
PriceActionBreakoutConfig, ScheduleConfig, FailureBehaviorConfig — copy from scaffolding/contracts.md.
```

### Generating broker interfaces (Step 4):
```
Generate rvs.AlgoTrader.Brokers.Abstractions.
Use EXACT interface signatures and model definitions from scaffolding/broker-models.md.
Do NOT add methods not listed there — this is the stable contract.
```

### Generating a strategy:
```
Generate PriceActionBreakoutStrategy (Step 28 in PLAN.md).
Load skills/trading-domain.md for signal pipeline rules.
Load skills/sebi-compliance.md for audit requirements.
All params from config_json — use PriceActionBreakoutConfig from scaffolding/contracts.md.
Use IClock for all time operations.
Write unit tests in rvs.AlgoTrader.UnitTests/Strategies/ using SimulatedClock.
```

### Generating broker adapters:
```
Generate ZerodhaClient implementing IFullBrokerClient (Step 19 in PLAN.md).
Load skills/broker-integration.md for Polly policies, latency measurement, and decorators.
Interfaces MUST match scaffolding/broker-models.md exactly (IBrokerOrderClient, etc.).
Register SessionAwareBrokerClient and ReconnectingBrokerStreamClient as decorators.
All secrets via ISecretsProvider — no hardcoded values.
```

### Generating tests:
```
Generate unit tests for ICapitalAllocator (Step 22/36 in PLAN.md).
Load skills/testing-patterns.md for conventions.
Test: concurrent reservation safety — use multiple threads, assert no over-allocation.
Use SimulatedClock from TestClocks fixtures. Use Moq for Redis.
```

### Generating API layer:
```
Generate OrdersController (Step 33 in PLAN.md).
MVC controller at /api/v1/orders. Roles: Admin, Trader.
Apply: [EnableRateLimiting("OrderPolicy")], IdempotencyMiddleware, Swagger XML comments.
All responses: ApiResponse<T> envelope — see scaffolding/contracts.md for exact shape.
Handler via MediatR: PlaceOrderCommand with FluentValidation.
```

### Generating observability (metrics, CI, monitoring):
```
Generate OpenTelemetry setup + Prometheus config (Step 38 in PLAN.md).
Load skills/observability.md for exact meter names, Prometheus scrape config, and CI steps.
CI pipeline is already at .github/workflows/ci.yml (from scaffolding/ci-cd.yml) — do not recreate.
Use IMeterFactory for all custom meters — never instantiate Meter directly.
All 12 built-in monitoring alert thresholds must be seeded into monitoring_alert_rules table.
```

---

## Step 6: Verify Each Component

After generating each component, the Definition of Done from CLAUDE.md must be satisfied:
- [ ] Implementation written
- [ ] Unit tests written and passing
- [ ] Added to DI registration
- [ ] Swagger XML comments on controller actions
- [ ] No compiler warnings
- [ ] All interfaces respected (no signature changes)
- [ ] `dotnet build` passes
- [ ] NetArchTest rules still pass

---

## Lessons Learned Workflow

This is the core "learn from mistakes" mechanism:

1. **Mistake happens** → Claude generates code that violates a rule
2. **You correct it** → identify the root cause
3. **You add to CLAUDE.md** → under `## Anti-Patterns Seen in Past Sessions`
4. **Claude Code reads it next session** → it loaded from CLAUDE.md and won't repeat

The CLAUDE.md already contains 15 anti-patterns (AP-001 through AP-015) derived from common mistakes on projects like this. Add your own as they occur.

---

## Quick Reference Card

| I want to... | Load this skill | Key rule to remember |
|---|---|---|
| Write a strategy | skills/trading-domain.md | Only closed candles in EvaluateAsync |
| Write a broker client | skills/broker-integration.md + scaffolding/broker-models.md | Polly on all HTTP + latency measurement |
| Write tests | skills/testing-patterns.md | SimulatedClock always, never SystemClock |
| Write order/position code | skills/sebi-compliance.md | audit_log INSERT-only |
| Write indicator logic | skills/performance-patterns.md | IIncrementalIndicator for O(1) live use |
| Write any time code | CLAUDE.md Rule #1 | IClock injection, NodaTime only |
| Start a new component | skills/development-workflow.md | Branching, DI, naming, Coding Standards |
| Review generated code | skills/qa-checklist.md | Run the matching QA-0N checklist |
| Write metrics/alerts/CI | skills/observability.md | IMeterFactory, Prometheus scrape, CI steps |
| Define .csproj / solution | scaffolding/csproj-templates.md | Copy exactly, no modifications |
| Define DTOs / API contracts | scaffolding/contracts.md | ApiResponse<T>, all DTO shapes |
| Define domain events | scaffolding/domain-events.md | All 14 records + MassTransit registration |
| Define Docker environment | scaffolding/docker-compose.yml | Healthcheck on every service |
| Define CI pipeline | scaffolding/ci-cd.yml | Copy to .github/workflows/ci.yml |
| Capture a mistake | hooks/learn-from-mistake.sh | Auto-appends to CLAUDE.md with AP-NNN |
| Track learning progress | SELF_LEARNING.md | Session table + resume prompt template |

---

## Three Hooks — When to Run Each

| Hook | When | What it does |
|---|---|---|
| `./hooks/pre-generate.sh` | Before starting a session | Checks Docker, .env, scans for violations |
| `./hooks/post-generate.sh` | After generating a component | Build, lint, tests, architecture rules |
| `./hooks/learn-from-mistake.sh` | After spotting any mistake | Captures AP-NNN into CLAUDE.md |
