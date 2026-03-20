# SELF_LEARNING.md — Do Not Repeat System

> This file defines the complete workflow for capturing mistakes, enforcing learning,
> and preventing the same error from happening twice across Claude Code sessions.
>
> **The core mechanism:** Claude Code reads `CLAUDE.md` at the start of every session.
> Every mistake you capture here gets added to `CLAUDE.md`. Next session = never repeated.

---

## How the Learning Loop Works

```
Claude generates code
        │
        ▼
Does it violate a rule?
        │
   YES ─┤─ NO → continue ✅
        │
        ▼
Run: ./hooks/learn-from-mistake.sh
        │
        ▼
Describe the mistake → auto-appended to CLAUDE.md
        │
        ▼
Next Claude Code session starts
        │
        ▼
Claude reads CLAUDE.md (auto-loaded)
        │
        ▼
Sees new anti-pattern → will NOT repeat it ✅
```

---

## Step-by-Step: How to Capture a Mistake

### Option A — Automated (Recommended)
```bash
./hooks/learn-from-mistake.sh
```
The script prompts you for:
1. Short title (e.g., "Missing CancellationToken on broker call")
2. What was generated wrongly (paste the bad code)
3. What the correct code is (paste the fix)
4. Why this matters (link to rule or architectural principle)

It auto-appends to `CLAUDE.md` with the next AP number.

### Option B — Manual
Open `CLAUDE.md` and append to the `## 🚫 Anti-Patterns Seen in Past Sessions` section:

```markdown
### AP-NNN: [Short Title — max 8 words]
**Mistake:** [One sentence: what Claude generated that was wrong]
```csharp
// bad code here
```
**Fix:** [One sentence: what the correct code is]
```csharp
// correct code here
```
**Why:** [One sentence: why this matters — link to rule, data safety, SEBI, performance, etc.]
```

---

## Anti-Pattern Template

Copy-paste this template when adding a new entry:

```markdown
### AP-NNN: [Short Title]
**Mistake:** [Describe exactly what was generated incorrectly — be specific, include file name if known]
**Fix:** [Describe the correct implementation]
**Why:** [Root cause / rule violated — reference HARD RULES section if applicable]
```

---

## Lessons Learned Template (for non-code insights)

Use this for operational lessons (deployment, config, runtime behavior) — append to `## 📝 Lessons Learned Log`:

```markdown
### LL-NNN: [Short Title]
**Context:** [What situation revealed this lesson]
**Fix/Rule:** [What to do instead]
**Rule:** [One-line rule going forward]
```

---

## Categories of Mistakes to Watch For

Organize your observations into these categories. When running `learn-from-mistake.sh`, tag the category:

| Tag | Category | Examples |
|---|---|---|
| `[CLOCK]` | IClock / time handling | DateTime.Now, missing IClock injection |
| `[CONTEXT]` | Bounded context violation | Backtesting calling broker, cross-context DI |
| `[CANDLE]` | Candle pipeline | Partial candle in strategy, wrong bar |
| `[AUDIT]` | Audit log / SEBI | UPDATE on audit_log, missing audit write |
| `[CAPITAL]` | Capital / risk | Non-atomic reserve, race condition |
| `[IDEMPOTENCY]` | Idempotency | Missing key check, key reuse |
| `[SECURITY]` | Secrets / auth | Hardcoded secret, missing [Authorize] |
| `[INFRA]` | Infrastructure | Missing Polly, no TTL on Redis key |
| `[TEST]` | Testing | Using SystemClock in test, missing edge case |
| `[PERF]` | Performance | O(N) in hot path, missing index |
| `[FRONTEND]` | React / TypeScript | Missing data-testid, no loading state |
| `[PATTERN]` | Code patterns | MediatR misuse, missing validator |

---

## Session Start Checklist (Say This to Claude Code)

At the start of every new Claude Code session, paste this:

```
Before writing any code:
1. Read CLAUDE.md carefully — especially ## Anti-Patterns Seen in Past Sessions
2. Confirm you understand the current AP count (AP-001 through AP-NNN)
3. State which anti-patterns are most relevant to the task I'm about to give you
4. Load the relevant skill files for this task
5. Tell me which PLAN.md step we are continuing from

I will tell you if anything you generate violates a rule, and we will add it 
to CLAUDE.md immediately so it never happens again.
```

---

## Session End Checklist (Do This After Every Session)

```
1. Run: ./hooks/post-generate.sh
   → Fix any FAIL items before closing the session

2. Review generated code for subtle violations:
   → Any DateTime.Now that grep might miss?
   → Any audit log missing?
   → Any missing CancellationToken?

3. For each issue found:
   → Run: ./hooks/learn-from-mistake.sh
   → Add to CLAUDE.md

4. Update PLAN.md:
   → Mark completed steps with [x]
   → Note any steps that were partially done

5. Git commit with correct format:
   → git add CLAUDE.md (if new anti-patterns added)
   → git commit -m "docs(claude): add AP-NNN [short description]"
```

---

## Multi-Session Progress Tracking

Keep this table updated as you progress through PLAN.md:

| Session | Date | Steps Completed | Anti-Patterns Added | Notes |
|---|---|---|---|---|
| 1 | | Step 1 (project structure) | AP-016 | — |
| 2 | | Steps 2–3 (Domain, Application) | AP-017, AP-018 | — |
| 3 | | Steps 4–5 (Handlers, DB schema) | — | — |
| ... | | | | |

---

## Resume Prompt for Next Session

After each session, fill in this template and save it. Paste it at the start of the next session:

```
Resume AlgoTrader generation.

Last completed: Step [N] of PLAN.md — [step name].
Next step: Step [N+1] — [step name].

CLAUDE.md has been updated with [N] new anti-patterns since last session:
- AP-NNN: [short description]
- AP-NNN: [short description]

Please read CLAUDE.md, acknowledge the new anti-patterns, then continue with Step [N+1].
Run ./hooks/pre-generate.sh before starting and ./hooks/post-generate.sh after finishing.
```

---

## What "Self-Learning" Means for Claude Code

Claude Code does not have persistent memory across sessions by itself.
The `CLAUDE.md` file IS the memory. Here is exactly how it works:

```
Session 1                 Session 2                 Session 3
─────────                 ─────────                 ─────────
Claude starts             Claude starts             Claude starts
    ↓                         ↓                         ↓
Reads CLAUDE.md           Reads CLAUDE.md           Reads CLAUDE.md
(AP-001 to AP-015)        (AP-001 to AP-016)        (AP-001 to AP-017)
    ↓                         ↓                         ↓
Generates code            Generates code            Generates code
    ↓                         ↓                         ↓
Makes mistake             Avoids AP-016 ✅          Avoids AP-016, 017 ✅
AP-016 captured           Makes new mistake         No new mistakes ✅
Appended to CLAUDE.md     AP-017 captured
                          Appended to CLAUDE.md
```

**The more mistakes you capture, the smarter the next session is.**
There is no theoretical limit — every edge case you document becomes a permanent guard.

---

## High-Value Anti-Patterns to Watch for in THIS Project

These are statistically common mistakes in .NET + React trading platforms.
Watch for them closely and add to CLAUDE.md immediately if seen:

### Category: Clock
- Using `DateTime.Now` in any class that isn't `SystemClock.cs`
- Passing `ZonedDateTime` across the wire (should be `Instant` → ISO string)
- VWAP not resetting daily because `IncrementalVWAP` doesn't call `_clock.Today()`

### Category: Capital & Orders
- Two strategy instances not using the same `ICapitalAllocator` instance (singleton vs scoped)
- `TryReserveAsync` called twice (once in handler, once in engine) causing double-reservation
- `ReleaseAsync` not called in a `finally` block (capital never returned on exception)

### Category: Candle Pipeline
- `CandleAggregatorService` using `Task.Run()` for bar boundary check (not needed — use channels)
- Two timeframes sharing the same aggregator instance causing bar crosstalk
- `ICandleCache.AppendAsync` called with a future timestamp (SimulatedClock not advancing)

### Category: Audit / SEBI
- Using `_db.SaveChangesAsync()` without logging to audit_log in the same transaction
- Audit log `actor` left as empty string (should be userId or "SYSTEM")
- Strategy lifecycle changes (start/pause/stop) not captured in audit_log

### Category: Frontend
- `crypto.randomUUID()` called on component mount (causes same key on re-renders)
- SignalR hub connection created outside `useEffect` (memory leak on unmount)
- `useEffect` dependency array missing `signalRConnection` (stale closure)

### Category: Testing
- `SimulatedClock` not passed to `IncrementalVWAP` in test (uses real clock instead)
- `Respawn` not resetting DB between tests (test isolation failure)
- Testcontainers not reused across tests in the same class (slow tests)

---

## Escalation: Mistakes That Require Architecture Review

If you encounter a mistake that suggests a deeper design flaw (not just a coding error),
add it here AND open a discussion before continuing:

```markdown
## 🔴 Architecture Issues Requiring Review

### AI-001: [Issue title]
**Observed:** [What happened]
**Root cause:** [Design flaw]
**Options:**
  A. [Option A + tradeoffs]
  B. [Option B + tradeoffs]
**Recommended:** [Your recommendation]
**Status:** OPEN / RESOLVED — [Resolution if resolved]
```

This section appears at the TOP of CLAUDE.md so it's seen immediately next session.
