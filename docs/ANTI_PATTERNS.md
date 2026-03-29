# Anti-patterns

| Code | Pattern | Fix |
|------|---------|-----|
| AP-001 | DateTime.Now / DateTimeOffset.UtcNow | use IClock |
| AP-002 | backtesting injecting broker | use historical data only |
| AP-003 | MediatR on trivial CRUD | direct service call |
| AP-004 | missing Idempotency-Key on order | enforce before order processing |
| AP-005 | non-atomic capital reserve | single Redis Lua script |
| AP-006 | hardcoded secret | ISecretsProvider |
| AP-007 | partial candle in strategy | closed candle events only |
| AP-008 | missing correlation ID in logs | Serilog enrichment |
| AP-009 | UPDATE/DELETE on audit_log | INSERT only |
| AP-010 | broker HTTP without Polly | retry + circuit breaker + timeout |
| AP-011 | order without market calendar check | validate session first |
| AP-012 | frontend submit without idempotency key | crypto.randomUUID() per submit |
| AP-013 | schema change without migration | not allowed unless requested |
| AP-014 | timeseries query without time range | always bound timestamps |
| AP-015 | kill switch ignored on restart | always blocks auto-resume |
| AP-016 | candle aggregation using static clock | use IClock |
| AP-017 | silent cold restart | surface event in UI |
| AP-018 | secret or API key echoed in output | never log or print secrets |
| AP-019 | creating feature/topic branches | always commit directly to master |
| AP-020 | raw hex color in frontend | import from src/styles/tokens.ts |
| AP-021 | left sidebar layout | use top-nav horizontal layout only |
| AP-022 | inline expanded forms | use right-side drawer pattern |
