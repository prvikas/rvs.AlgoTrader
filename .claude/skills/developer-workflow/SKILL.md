
***

## .claude/skills/developer-workflow/SKILL.md

```md
---
name: developer-workflow
description: Standard development workflow for implementing features in this repo
model: haiku 
---

# Developer workflow

## For every task
1. run repo-audit skill first
2. propose smallest safe change
3. implement with tests
4. update docs
5. run post-generate hook validation

## Implementation checklist
- [ ] existing code inspected
- [ ] tests written or updated
- [ ] IMPLEMENTATION_STATUS.md updated
- [ ] REQUIREMENTS_DELTA.md updated if needed
- [ ] no new anti-patterns introduced
- [ ] no secrets in code or output

## Naming
- classes: PascalCase
- interfaces: IPascalCase
- methods: PascalCase
- async methods: suffix Async
- tests: MethodName_GivenCondition_ExpectedResult

## Service registration
- all new services registered in DI
- interfaces registered, not concrete types
- scoped/transient/singleton chosen deliberately

## When adding a new strategy
1. update STRATEGY_SPECS.md first
2. implement IStrategy
3. write unit tests with SimulatedClock
4. write parity test
5. register in DI
6. add to strategy_type enum/registry

## When touching broker code
1. verify behavior against mStock Postman collection
2. update DATA_SOURCES.md if capability confirmed or denied
3. test with mock IBrokerClient in unit tests
4. integration test against Testcontainers mock if available
