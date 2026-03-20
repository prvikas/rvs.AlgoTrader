#!/usr/bin/env bash
# post-generate.sh — Run AFTER Claude Code generates a component
# Validates the generated code meets all architectural contracts

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

PASS=0
WARN=0
FAIL=0

log_pass() { echo -e "${GREEN}✅ PASS${NC}: $1"; ((PASS++)); }
log_warn() { echo -e "${YELLOW}⚠️  WARN${NC}: $1"; ((WARN++)); }
log_fail() { echo -e "${RED}❌ FAIL${NC}: $1"; ((FAIL++)); }

echo -e "${BLUE}═══════════════════════════════════════════════${NC}"
echo -e "${BLUE}  AlgoTrader — Post-Generation Validation${NC}"
echo -e "${BLUE}═══════════════════════════════════════════════${NC}"
echo ""

# ─── 1. Build ─────────────────────────────────────────────────────────────────
echo "--- Build Validation ---"

if [ -f "rvs.AlgoTrader.sln" ]; then
    echo "Running dotnet restore..."
    dotnet restore rvs.AlgoTrader.sln -v q
    
    echo "Running dotnet build (TreatWarningsAsErrors=true)..."
    if dotnet build rvs.AlgoTrader.sln --no-restore -v q -p:TreatWarningsAsErrors=true 2>&1; then
        log_pass "Solution builds with zero errors and zero warnings"
    else
        log_fail "Build failed — generated code has errors or warnings. Fix before continuing."
    fi
else
    log_warn "rvs.AlgoTrader.sln not found — run from repo root"
fi

echo ""

# ─── 2. Architecture Contract Violations ─────────────────────────────────────
echo "--- Architecture Contracts ---"

# IClock violations
echo "Checking IClock contract..."
CLOCK_VIOLATIONS=$(grep -rn --include="*.cs" \
    -E "(DateTime\.Now|DateTime\.UtcNow|DateTimeOffset\.UtcNow|DateTimeOffset\.Now|NodaTime\.SystemClock\.Instance\.GetCurrentInstant)" \
    src/ --exclude-path="*/obj/*" --exclude="*SystemClock.cs" \
    2>/dev/null || true)

if [ -z "$CLOCK_VIOLATIONS" ]; then
    log_pass "IClock contract: No DateTime.Now / DateTimeOffset.UtcNow in src/ (except SystemClock.cs)"
else
    log_fail "IClock violation — replace with IClock injection:"
    echo "$CLOCK_VIOLATIONS"
fi

# Backtesting → Broker isolation
echo "Checking Backtesting ↛ Broker isolation..."
BACKTEST_BROKER_DEPS=$(grep -rn --include="*.cs" \
    -E "(ZerodhaClient|UpstoxClient|MStockClient|IFullBrokerClient|IBrokerOrderClient|IBrokerStreamClient)" \
    src/rvs.AlgoTrader.Backtesting/ --exclude-path="*/obj/*" \
    2>/dev/null || true)

if [ -z "$BACKTEST_BROKER_DEPS" ]; then
    log_pass "Backtesting context has no broker dependencies"
else
    log_fail "Backtesting context references broker code:"
    echo "$BACKTEST_BROKER_DEPS"
fi

# Domain → Infrastructure isolation
echo "Checking Domain ↛ Infrastructure..."
DOMAIN_INFRA=$(grep -rn --include="*.csproj" \
    -E "AlgoTrader\.Infrastructure|AlgoTrader\.Brokers|EntityFrameworkCore" \
    src/rvs.AlgoTrader.Domain/ \
    2>/dev/null || true)

if [ -z "$DOMAIN_INFRA" ]; then
    log_pass "Domain .csproj has no Infrastructure/EF dependencies"
else
    log_fail "Domain project references Infrastructure:"
    echo "$DOMAIN_INFRA"
fi

# Application → EF Core isolation
echo "Checking Application ↛ EF Core..."
APP_EF=$(grep -rn --include="*.csproj" \
    -E "EntityFrameworkCore" \
    src/rvs.AlgoTrader.Application/ \
    2>/dev/null || true)

if [ -z "$APP_EF" ]; then
    log_pass "Application .csproj has no EF Core dependency"
else
    log_fail "Application project references EF Core (repositories should be interfaces only):"
    echo "$APP_EF"
fi

# Hardcoded secrets
echo "Checking for hardcoded secrets..."
HARDCODED=$(grep -rn --include="*.cs" \
    -E '(ApiKey|ApiSecret|Password|ConnectionString)\s*=\s*"[^"{]' \
    src/ --exclude-path="*/obj/*" \
    2>/dev/null || true)

if [ -z "$HARDCODED" ]; then
    log_pass "No hardcoded secrets found"
else
    log_fail "Potential hardcoded secrets — use ISecretsProvider:"
    echo "$HARDCODED"
fi

# audit_log INSERT-only check
echo "Checking audit_log is INSERT-only..."
AUDIT_UPDATE=$(grep -rn --include="*.cs" \
    -E "(UPDATE|Delete|ExecuteDelete|ExecuteUpdate).*audit_log|audit_log.*(UPDATE|Delete)" \
    src/ --exclude-path="*/obj/*" \
    -i 2>/dev/null || true)

if [ -z "$AUDIT_UPDATE" ]; then
    log_pass "No UPDATE/DELETE on audit_log found"
else
    log_fail "Audit log mutation detected — audit_log is APPEND-ONLY:"
    echo "$AUDIT_UPDATE"
fi

# Idempotency-Key on order controller
echo "Checking idempotency on orders..."
ORDER_CTRL=$(find src/ -name "OrdersController.cs" -not -path "*/obj/*" 2>/dev/null || true)
if [ -n "$ORDER_CTRL" ]; then
    if grep -q "Idempotency-Key\|IdempotencyKey\|IIdempotencyService" "$ORDER_CTRL" 2>/dev/null; then
        log_pass "OrdersController references idempotency"
    else
        log_warn "OrdersController may be missing idempotency check — verify middleware applied"
    fi
else
    log_warn "OrdersController.cs not found — skipping idempotency check"
fi

# Response envelope check
echo "Checking API response envelope..."
CONTROLLERS_WITHOUT_ENVELOPE=$(find src/rvs.AlgoTrader.API/Controllers -name "*.cs" -not -path "*/obj/*" 2>/dev/null | \
    xargs grep -l "return Ok\|return BadRequest\|return NotFound" 2>/dev/null | \
    xargs grep -rL "ApiResponse\|success.*true\|correlationId" 2>/dev/null | \
    grep -v "AuthController" || true)  # Auth may use different structure

if [ -z "$CONTROLLERS_WITHOUT_ENVELOPE" ]; then
    log_pass "All controllers appear to use response envelope"
else
    log_warn "These controllers may be missing ApiResponse envelope — verify:"
    echo "$CONTROLLERS_WITHOUT_ENVELOPE"
fi

echo ""

# ─── 3. Unit Tests ────────────────────────────────────────────────────────────
echo "--- Unit Tests ---"

if [ -d "tests/rvs.AlgoTrader.UnitTests" ]; then
    echo "Running unit tests..."
    if dotnet test tests/rvs.AlgoTrader.UnitTests --no-build -v q 2>&1; then
        log_pass "All unit tests pass"
    else
        log_fail "Unit tests FAILED — fix failing tests before committing"
    fi
    
    echo "Running NetArchTest architecture rules..."
    ARCH_RESULT=$(dotnet test tests/rvs.AlgoTrader.UnitTests --filter "Category=Architecture" --no-build -v q 2>&1)
    if echo "$ARCH_RESULT" | grep -q "Passed"; then
        log_pass "NetArchTest architecture rules pass"
    else
        log_warn "NetArchTest not passing or no architecture tests found yet"
    fi
else
    log_warn "tests/rvs.AlgoTrader.UnitTests not found — skipping unit tests"
fi

echo ""

# ─── 4. Frontend Build ────────────────────────────────────────────────────────
if [ -d "client/algotrader-ui" ]; then
    echo "--- Frontend Build ---"
    echo "Running npm run build..."
    (cd client/algotrader-ui && npm run build 2>&1) && \
        log_pass "Frontend builds successfully" || \
        log_fail "Frontend build failed"
    echo ""
fi

# ─── 5. EF Migration Check ────────────────────────────────────────────────────
echo "--- EF Migrations ---"

if [ -d "src/rvs.AlgoTrader.Infrastructure/Migrations" ]; then
    MIGRATION_COUNT=$(find src/rvs.AlgoTrader.Infrastructure/Migrations -name "*.cs" -not -name "*.Designer.cs" | wc -l)
    log_pass "EF Migrations directory exists with $MIGRATION_COUNT migration files"
    
    # Check for uncommitted schema changes (Model changes without migration)
    PENDING=$(dotnet ef migrations list \
        -p src/rvs.AlgoTrader.Infrastructure \
        -s src/rvs.AlgoTrader.API \
        2>/dev/null | grep "pending" || true)
    
    if [ -z "$PENDING" ]; then
        log_pass "No pending EF migrations"
    else
        log_warn "Pending EF migrations detected — run: dotnet ef database update"
    fi
else
    log_warn "Migrations directory not found yet — will be created in Step 5 of PLAN.md"
fi

echo ""

# ─── 6. Definition of Done Checklist ─────────────────────────────────────────
echo "--- Definition of Done Quick Check ---"
echo -e "${CYAN}Manually verify these for the component just generated:${NC}"
echo "  □ Unit tests written and passing"
echo "  □ Added to DI registration (Program.cs or extension method)"
echo "  □ Swagger XML comments on controller actions (if applicable)"
echo "  □ No compiler warnings in the new files"
echo "  □ Interface signatures match CLAUDE.md contracts"

echo ""

# ─── 7. Summary ───────────────────────────────────────────────────────────────
echo -e "${BLUE}═══════════════════════════════════════════════${NC}"
echo -e "  Results: ${GREEN}$PASS passed${NC} | ${YELLOW}$WARN warnings${NC} | ${RED}$FAIL failed${NC}"
echo -e "${BLUE}═══════════════════════════════════════════════${NC}"

if [ "$FAIL" -gt 0 ]; then
    echo -e "${RED}❌ Post-generation validation FAILED — fix errors before merging${NC}"
    exit 1
elif [ "$WARN" -gt 0 ]; then
    echo -e "${YELLOW}⚠️  Post-generation validation passed with warnings — review above${NC}"
    exit 0
else
    echo -e "${GREEN}✅ All post-generation checks passed — component is DONE${NC}"
    echo ""
    echo -e "${CYAN}Next step: Check PLAN.md and mark the completed step with [x]${NC}"
    exit 0
fi
