# MCP_DESIGN.md

## Purpose

Expose a minimal Model Context Protocol (MCP) server so Claude Code and other LLM agents can query live trading state, retrieve backtest results, and trigger the kill switch without touching the main REST API directly.

Reference implementation: https://github.com/marketcalls/openalgo-mcp

---

## Authentication

All MCP endpoints share the same JWT bearer token as the main API (`Authorization: Bearer <token>`). No separate credentials. Same RBAC policies apply — kill switch requires `RiskManager` role minimum.

---

## API shapes

### GET /mcp/strategy-status

Returns the current runtime state of all active strategy instances.

**Response**
```json
{
  "data": [
    {
      "strategyInstanceId": "uuid",
      "strategyName": "VcpSwingStrategy",
      "symbol": "RELIANCE",
      "executionMode": "Live",
      "sessionState": "Active",
      "openPositions": 1,
      "dailyPnl": 1240.50,
      "killSwitchActive": false,
      "lastSignalAt": "2026-04-06T09:32:00+05:30"
    }
  ]
}
```

**Notes**
- `sessionState`: `Waiting | Active | Closed`
- `executionMode`: `Backtest | ForwardTest | Paper | Live`
- Returns empty array if no instances are running
- Reads from Redis (kill switch) + DB (positions, P&L) — sub-100ms target

---

### GET /mcp/backtest-results/{id}

Returns a summary of a completed backtest job.

**Path param**: `id` — UUID of the backtest job

**Response**
```json
{
  "data": {
    "jobId": "uuid",
    "strategyName": "FibOptionSpreadStrategy",
    "symbol": "NIFTY",
    "status": "Completed",
    "cagr": 18.4,
    "maxDrawdownPct": 12.1,
    "sharpeRatio": 1.42,
    "totalTrades": 87,
    "winRate": 61.0,
    "netPnl": 142300.00,
    "deploymentRating": "Approved",
    "completedAt": "2026-04-06T10:15:00+05:30"
  }
}
```

**Errors**
- `404` if job not found
- `409` if job is still running (use `status: "Running"` body)

---

### POST /mcp/kill-switch

Activates or deactivates the global or per-strategy kill switch.

**Request**
```json
{
  "scope": "global",
  "strategyInstanceId": null,
  "active": true,
  "reason": "Unexpected market gap — MCP-triggered halt"
}
```

- `scope`: `"global"` or `"strategy"`
- `strategyInstanceId`: required when `scope = "strategy"`, null otherwise
- `active`: `true` to halt, `false` to resume
- `reason`: free-text, written to audit_log (INSERT-only, AP-009)

**Response**
```json
{
  "data": {
    "scope": "global",
    "active": true,
    "appliedAt": "2026-04-06T10:20:00+05:30",
    "auditId": "uuid"
  }
}
```

**Notes**
- Dual-writes Redis + DB per AP-015
- Requires `RiskManager` role; returns `403` otherwise
- Idempotent — calling `active: true` when already active returns 200 with current state

---

## Implementation notes

- Mount MCP server under `/mcp` prefix in `Program.cs` alongside existing API routes
- Use existing `IKillSwitchService`, `IBacktestService`, `IStrategyScheduler` — no new domain logic
- Return standard API envelope (`{ "data": ..., "errors": [] }`) matching AP-013
- Log all MCP calls with correlation ID per AP-008
- Phase: P8 (placeholder — implement after P7 data services verified live)
