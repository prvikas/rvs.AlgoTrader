\# SELF\_LEARNING.md

SL-NNN | bad: ... | fix: ... | rule: ...



SL-001 | bad: proposed full rewrite without inspection | fix: inspect code first | rule: always start from IMPLEMENTATION\_STATUS.md

SL-002 | bad: large startup memory | fix: keep CLAUDE.md < 200 lines | rule: detail goes in docs/ or skills

SL-003 | bad: repeated unchanged requirements | fix: deltas only | rule: use REQUIREMENTS\_DELTA.md

SL-004 | bad: strategy coded from prose | fix: spec first | rule: update STRATEGY\_SPECS.md before coding

SL-005 | bad: parity drift backtest vs forward | fix: same signal logic only | rule: parity review on any strategy change

SL-006 | bad: assumed mStock returns IV without verifying | fix: verify live response schema first | rule: unconfirmed API fields must be marked VERIFY\_LIVE

SL-007 | bad: secret or token echoed in output | fix: never log secrets | rule: AP-018 always applies

SL-008 | bad: placed INseEventCalendarImporter interface in Infrastructure; controller imported Infrastructure.Services to use it | fix: always define service interfaces in Application layer; Infrastructure only holds implementations | rule: API → Application ← Infrastructure; AP-003

SL-009 | bad: DateTime.UtcNow in Application handler; .ToDateTimeUtc() in Application mapper | fix: inject IClock (Domain.Interfaces.IClock); use InstantPattern.ExtendedIso.Format() for serialization | rule: AP-001 no DateTime.Now anywhere; NodaTime conversions that return System.DateTime also violate this rule

SL-010 | bad: controller directly enqueued Hangfire job using Infrastructure.Jobs type; controller directly queried DbContext from Infrastructure.Persistence | fix: create IBreadthJobDispatcher + IEnumValuesService interfaces in Application; implement in Infrastructure; inject via DI | rule: controllers must dispatch via abstractions not concrete Infrastructure types



\## Session tracker

| # | Date | Area | Lessons |

|---|---|---|---|

| 1 | 2026-03-25 | full docs baseline | SL-001..SL-007 |

| 2 | 2026-04-06 | PROMPT-003 arch fixes | SL-008..SL-010 |



