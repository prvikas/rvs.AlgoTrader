#!/usr/bin/env bash
# =============================================================================
# rvs.AlgoTrader — GitHub Milestone + Issue Assignment Setup
# Run once: bash scripts/setup-milestones.sh
# Requires: gh CLI authenticated  (gh auth login)
# =============================================================================
set -euo pipefail

REPO="prvikas/rvs.AlgoTrader"

echo "🏗️  Creating milestones..."

# Helper: create milestone only if it does not already exist
create_if_missing() {
  local title="$1"
  local desc="$2"
  if gh api repos/$REPO/milestones --jq '.[].title' | grep -qF "$title"; then
    echo "  ⏭️  Already exists: $title"
  else
    gh api repos/$REPO/milestones --method POST \
      --field title="$title" \
      --field description="$desc" \
      --field state="open" --silent
    echo "  ➕  Created: $title"
  fi
}

create_if_missing \
  "v0.1 — Core Architecture Foundation" \
  "Infrastructure, DI registration, IIndicatorLibrary, CandlePatternDetector, shared contracts. Must be complete before any strategy work."

create_if_missing \
  "v0.2 — Data Layer & Market Data" \
  "IHistoricalDataManager, option chain service, IEventCalendarService, IMarketBreadthService, data feed health monitor, WebSocket reconnection."

create_if_missing \
  "v0.3 — Options Engine" \
  "IBlackScholesEngine, Greeks calculation, IV Rank/IVP, IOptionLegSelector, multi-leg spread order support (SpreadOrderManager)."

create_if_missing \
  "v0.4 — Risk & Execution Engine" \
  "IPortfolioRiskManager, IPositionSizingEngine, slippage/commission model, trailing stop, break-even, paper trading mode, scaling in/out."

create_if_missing \
  "v0.5 — Strategy Implementations" \
  "STRAT-001 VCP Swing, STRAT-002 Fib Spread, STRAT-003 Intraday PCR/VWAP, Iron Condor, Short Straddle, Short Strangle, Calendar Spread, Vertical Spreads."

create_if_missing \
  "v0.6 — Multi-Timeframe & Advanced Signals" \
  "Multi-timeframe analysis (5m+15m+Daily), candle aggregation, PCR live feed, breadth dashboard widget, MTF strategy filters."

create_if_missing \
  "v0.7 — Research & Analytics" \
  "Full performance analytics (Sharpe/Sortino/Calmar/VaR), Monte Carlo simulation, strategy correlation heatmap, Markowitz portfolio construction."

create_if_missing \
  "v0.8 — Trade Journal & Production Readiness" \
  "Trade journal, P&L attribution (by strategy/symbol/session), Indian tax export (ITR-3), admin UI polish, alert system, smoke tests, deployment scripts."

echo ""
echo "✅ Milestones ready. Assigning issues..."
echo ""

# =============================================================================
# ISSUE → MILESTONE ASSIGNMENT MAP
# =============================================================================

get_ms() {
  gh api repos/$REPO/milestones --jq ".[] | select(.title | startswith(\"$1\")) | .number"
}

M01=$(get_ms "v0.1")
M02=$(get_ms "v0.2")
M03=$(get_ms "v0.3")
M04=$(get_ms "v0.4")
M05=$(get_ms "v0.5")
M06=$(get_ms "v0.6")
M07=$(get_ms "v0.7")
M08=$(get_ms "v0.8")

echo "Resolved milestone IDs:"
echo "  v0.1=$M01  v0.2=$M02  v0.3=$M03  v0.4=$M04"
echo "  v0.5=$M05  v0.6=$M06  v0.7=$M07  v0.8=$M08"
echo ""

assign_ms() {
  local ms_number=$1; shift
  for issue in "$@"; do
    gh api repos/$REPO/issues/$issue --method PATCH \
      --field milestone=$ms_number --silent && \
      echo "  #$issue → milestone $ms_number" || \
      echo "  ⚠️  #$issue failed"
  done
}

# ---------------------------------------------------------------------------
# v0.1 — Core Architecture Foundation
# Broker abstraction, DI, config system, alert service, VWAP indicator,
# IIndicatorLibrary, CandlePatternDetector + all early architecture issues
# ---------------------------------------------------------------------------
echo "📦 v0.1 — Core Architecture Foundation"
assign_ms $M01 \
  1 2 3 4 5 6 7 8 9 10 \
  11 12 13 14 15 \
  66 67 69 70 71 74 75 76

# ---------------------------------------------------------------------------
# v0.2 — Data Layer & Market Data
# Historical data OHLCV import, gap detection, data quality,
# event calendar, market breadth, WebSocket reconnection
# ---------------------------------------------------------------------------
echo "📦 v0.2 — Data Layer & Market Data"
assign_ms $M02 \
  16 17 18 19 20 \
  90 91 96 99

# ---------------------------------------------------------------------------
# v0.3 — Options Engine
# Black-Scholes, Greeks, IV Rank, option leg selector, spread orders
# ---------------------------------------------------------------------------
echo "📦 v0.3 — Options Engine"
assign_ms $M03 \
  64 65 68 72 73 84

# ---------------------------------------------------------------------------
# v0.4 — Risk & Execution Engine
# Portfolio risk manager, position sizing, slippage, trailing stops,
# paper trading, scaling in/out
# ---------------------------------------------------------------------------
echo "📦 v0.4 — Risk & Execution Engine"
assign_ms $M04 \
  85 86 87 88 92 93 100

# ---------------------------------------------------------------------------
# v0.5 — Strategy Implementations
# All 7 strategies: VCP, Fib Spread, Intraday PCR, Iron Condor,
# Short Straddle, Short Strangle, Calendar Spread, Vertical Spreads
# ---------------------------------------------------------------------------
echo "📦 v0.5 — Strategy Implementations"
assign_ms $M05 \
  77 78 79 80 81 82 83

# ---------------------------------------------------------------------------
# v0.6 — Multi-Timeframe & Advanced Signals
# MTF analysis (5m+15m+Daily), candle aggregation, breadth widget
# strategy correlation warning in deploy UI
# ---------------------------------------------------------------------------
echo "📦 v0.6 — Multi-Timeframe & Advanced Signals"
assign_ms $M06 \
  21 22 23 24 25 26 \
  94 95

# ---------------------------------------------------------------------------
# v0.7 — Research & Analytics
# Performance analytics, Monte Carlo, strategy correlation, Markowitz
# ---------------------------------------------------------------------------
echo "📦 v0.7 — Research & Analytics"
assign_ms $M07 \
  27 28 29 30 \
  89 97

# ---------------------------------------------------------------------------
# v0.8 — Trade Journal & Production Readiness
# Trade journal, P&L attribution, tax export (ITR-3), admin UI,
# alert polish, smoke tests, all remaining issues
# ---------------------------------------------------------------------------
echo "📦 v0.8 — Trade Journal & Production Readiness"
assign_ms $M08 \
  31 32 33 34 35 36 37 38 39 40 \
  41 42 43 44 45 46 47 48 49 50 \
  51 52 53 54 55 56 57 58 59 60 \
  61 62 63 98

echo ""
echo "🎉 Done! All milestones created and issues assigned."
echo "   View: https://github.com/$REPO/milestones"
