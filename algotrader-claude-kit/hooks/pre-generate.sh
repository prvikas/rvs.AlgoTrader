#!/usr/bin/env bash
# pre-generate.sh — Run BEFORE asking Claude Code to generate any component
# Validates environment prerequisites and code hygiene

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

PASS=0
WARN=0
FAIL=0

log_pass() { echo -e "${GREEN}✅ PASS${NC}: $1"; ((PASS++)); }
log_warn() { echo -e "${YELLOW}⚠️  WARN${NC}: $1"; ((WARN++)); }
log_fail() { echo -e "${RED}❌ FAIL${NC}: $1"; ((FAIL++)); }

echo -e "${BLUE}═══════════════════════════════════════════════${NC}"
echo -e "${BLUE}  AlgoTrader — Pre-Generation Validation${NC}"
echo -e "${BLUE}═══════════════════════════════════════════════${NC}"
echo ""

# ─── 1. Tool Prerequisites ───────────────────────────────────────────────────
echo "--- Tool Prerequisites ---"

# .NET SDK
if command -v dotnet &>/dev/null; then
    VERSION=$(dotnet --version)
    if [[ "$VERSION" == 9.* ]]; then
        log_pass ".NET SDK $VERSION"
    else
        log_warn ".NET SDK $VERSION found — project requires 9.0+. Current: $VERSION"
    fi
else
    log_fail ".NET SDK not found. Install from https://dot.net/9"
fi

# Node.js
if command -v node &>/dev/null; then
    NODE_VER=$(node --version)
    MAJOR=$(echo "$NODE_VER" | sed 's/v//' | cut -d. -f1)
    if [ "$MAJOR" -ge 20 ]; then
        log_pass "Node.js $NODE_VER"
    else
        log_warn "Node.js $NODE_VER — project recommends v20+. Current: $NODE_VER"
    fi
else
    log_fail "Node.js not found. Install from https://nodejs.org"
fi

# Docker
if command -v docker &>/dev/null; then
    if docker info &>/dev/null 2>&1; then
        log_pass "Docker running ($(docker --version | cut -d',' -f1))"
    else
        log_warn "Docker installed but daemon not running — start Docker Desktop"
    fi
else
    log_fail "Docker not found. Install Docker Desktop"
fi

# dotnet ef tools
if dotnet tool list -g | grep -q "dotnet-ef"; then
    log_pass "dotnet-ef tool installed"
else
    log_warn "dotnet-ef tool not found. Run: dotnet tool install --global dotnet-ef"
fi

echo ""

# ─── 2. Infrastructure Health ────────────────────────────────────────────────
echo "--- Infrastructure Health ---"

# PostgreSQL
if docker ps --format '{{.Names}}' 2>/dev/null | grep -qiE "postgres|timescale"; then
    log_pass "PostgreSQL container running"
else
    log_warn "PostgreSQL container not detected. Run: docker compose up -d postgres"
fi

# Redis
if docker ps --format '{{.Names}}' 2>/dev/null | grep -qi "redis"; then
    log_pass "Redis container running"
else
    log_warn "Redis container not detected. Run: docker compose up -d redis"
fi

# RabbitMQ
if docker ps --format '{{.Names}}' 2>/dev/null | grep -qi "rabbitmq"; then
    log_pass "RabbitMQ container running"
else
    log_warn "RabbitMQ container not detected. Run: docker compose up -d rabbitmq"
fi

echo ""

# ─── 3. Configuration Files ───────────────────────────────────────────────────
echo "--- Configuration Files ---"

if [ -f ".env" ]; then
    log_pass ".env file exists"
    
    # Check for required keys
    REQUIRED_KEYS=("JWT__SECRET" "DATABASE__PASSWORD")
    for key in "${REQUIRED_KEYS[@]}"; do
        if grep -q "^${key}=" .env && ! grep -q "^${key}=$" .env; then
            log_pass ".env has $key set"
        else
            log_warn ".env missing or empty: $key"
        fi
    done
else
    log_fail ".env file not found. Copy .env.example: cp .env.example .env"
fi

if [ -f "src/rvs.AlgoTrader.API/appsettings.Development.json" ]; then
    log_pass "appsettings.Development.json exists"
else
    log_warn "appsettings.Development.json not found (optional for local dev)"
fi

echo ""

# ─── 4. Code Quality Checks (if solution exists) ─────────────────────────────
if [ -f "rvs.AlgoTrader.sln" ]; then
    echo "--- Code Quality Checks ---"
    
    # Build check
    echo "Running dotnet build..."
    if dotnet build rvs.AlgoTrader.sln --no-restore -v q 2>&1 | tail -5; then
        log_pass "Solution builds successfully"
    else
        log_fail "Solution build failed — fix errors before generating new code"
    fi
    
    # Check for DateTime.Now violations
    echo "Scanning for IClock violations..."
    VIOLATIONS=$(grep -rn --include="*.cs" \
        -E "(DateTime\.Now|DateTimeOffset\.UtcNow|NodaTime\.SystemClock\.Instance)" \
        src/ --exclude-path="*/obj/*" \
        2>/dev/null || true)
    
    if [ -z "$VIOLATIONS" ]; then
        log_pass "No IClock violations (DateTime.Now / UtcNow / SystemClock.Instance not found in src/)"
    else
        log_fail "IClock violations found! Replace with IClock injection:"
        echo "$VIOLATIONS" | head -20
    fi
    
    # Check for hardcoded secrets
    echo "Scanning for hardcoded secrets..."
    SECRET_VIOLATIONS=$(grep -rn --include="*.cs" --include="*.json" \
        -E "(password|api_key|apikey|secret|token)[[:space:]]*[=:][[:space:]]*\"[^{]" \
        src/ --exclude-path="*/obj/*" --exclude="appsettings.Development.json" \
        -i 2>/dev/null || true)
    
    if [ -z "$SECRET_VIOLATIONS" ]; then
        log_pass "No hardcoded secrets detected"
    else
        log_warn "Potential hardcoded secrets found — verify these use ISecretsProvider:"
        echo "$SECRET_VIOLATIONS" | head -10
    fi
    
    # Check for missing audit_log inserts on order controllers
    echo "Checking audit logging in controllers..."
    ORDER_CTRL="src/rvs.AlgoTrader.API/Controllers/OrdersController.cs"
    if [ -f "$ORDER_CTRL" ]; then
        if grep -q "auditLog\|IAuditService" "$ORDER_CTRL"; then
            log_pass "OrdersController references audit service"
        else
            log_warn "OrdersController may be missing audit log calls — verify IAuditService is injected"
        fi
    fi
    
    # Check cross-context violations
    echo "Scanning for cross-context violations..."
    BACKTEST_BROKER=$(grep -rn --include="*.cs" \
        "IZerodhaClient\|IUpstoxClient\|IMStockClient\|IFullBrokerClient\|IBrokerOrderClient" \
        src/rvs.AlgoTrader.Backtesting/ --exclude-path="*/obj/*" \
        2>/dev/null || true)
    
    if [ -z "$BACKTEST_BROKER" ]; then
        log_pass "No broker dependencies in Backtesting context"
    else
        log_fail "Backtesting context contains broker dependencies — violation of bounded context isolation!"
        echo "$BACKTEST_BROKER"
    fi
    
    # NetArchTest
    echo "Running NetArchTest architecture tests..."
    if dotnet test tests/rvs.AlgoTrader.UnitTests --filter "Category=Architecture" --no-build -v q 2>&1 | grep -q "Passed"; then
        log_pass "NetArchTest architecture rules pass"
    else
        log_warn "NetArchTest not run or failed (ensure tests exist)"
    fi
    
else
    log_warn "rvs.AlgoTrader.sln not found — skipping build and code quality checks (run from repo root)"
fi

echo ""

# ─── 5. Summary ───────────────────────────────────────────────────────────────
echo -e "${BLUE}═══════════════════════════════════════════════${NC}"
echo -e "  Results: ${GREEN}$PASS passed${NC} | ${YELLOW}$WARN warnings${NC} | ${RED}$FAIL failed${NC}"
echo -e "${BLUE}═══════════════════════════════════════════════${NC}"

if [ "$FAIL" -gt 0 ]; then
    echo -e "${RED}❌ Pre-generation check FAILED — resolve errors above before proceeding${NC}"
    exit 1
elif [ "$WARN" -gt 0 ]; then
    echo -e "${YELLOW}⚠️  Pre-generation check passed with warnings — review above${NC}"
    exit 0
else
    echo -e "${GREEN}✅ All pre-generation checks passed — safe to generate${NC}"
    exit 0
fi
