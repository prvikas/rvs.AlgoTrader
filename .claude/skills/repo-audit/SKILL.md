---
name: repo-audit
description: Inspect current implementation before proposing changes
model: haiku 
---

# Repo audit

Use before:
- architecture suggestions
- roadmap updates
- major doc rewrites
- feature planning

## Steps
1. inspect task-relevant code
2. classify state: DONE | PARTIAL | STUB | MISSING | NOT_REVIEWED
3. compare prompt vs implementation
4. update `docs/IMPLEMENTATION_STATUS.md`
5. record only new requirements in `docs/REQUIREMENTS_DELTA.md`
6. propose the smallest safe next step

## Rules
- trust code over docs
- no greenfield assumptions
- no broad rewrites before gap analysis
