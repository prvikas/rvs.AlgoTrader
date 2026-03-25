---
name: dotnet-guardrails
description: Apply project .NET and architecture constraints
---

# .NET guardrails

## Hard rules
- use `IClock`
- no partial candle in strategy eval
- no cross-context direct calls
- no EF Core in Application layer
- no hardcoded secrets
- no business config in appsettings
- no MediatR for trivial CRUD
- standard business API envelope

## Architecture
Domain <- Application <- Infrastructure <- API
