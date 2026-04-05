# Anti-patterns

| Code | Pattern | Fix |
|---|---|---|
| AP-001 | DateTime.Now / UtcNow | use IClock |
| AP-002 | backtest injecting broker | historical data only |
| AP-003 | MediatR on trivial CRUD | direct service call |
| AP-004 | missing Idempotency-Key on order | enforce before processing |
| AP-005 | non-atomic capital reserve | single Redis Lua script |
| AP-006 | hardcoded secret | ISecretsProvider |
| AP-007 | partial candle in strategy | closed candle events only |
| AP-008 | missing correlation ID in logs | Serilog enrichment |
| AP-009 | UPDATE/DELETE on audit_log | INSERT only |
| AP-010 | broker HTTP without Polly | retry + circuit breaker + timeout |
| AP-011 | order without market calendar check | validate session first |
| AP-012 | frontend submit without idempotency key | crypto.randomUUID() per submit |
| AP-013 | schema change without migration | not allowed unless asked |
| AP-014 | timeseries query without time range | always bound timestamps |
| AP-015 | kill switch ignored on restart | always blocks auto-resume |
| AP-016 | candle aggregation with static clock | use IClock |
| AP-017 | silent cold restart | surface event in UI |
| AP-018 | secret echoed in output | never log secrets |
| AP-019 | feature/topic branch | commit to master only |
| AP-020 | raw hex in frontend | import from tokens.ts |
| AP-021 | left sidebar layout | top-nav horizontal only |
| AP-022 | inline expanded form | right-side drawer pattern |
| AP-023 | adding indicators in Scenario drawer | param list structurally locked to parent strategy |
| AP-024 | symbol/broker/capital in Strategy creation | deployment layer fields only |
| AP-025 | Compare tab omitted from Strategies page | mandatory — not optional |
