#!/bin/bash
# rvs.AlgoTrader — cross-platform test runner (Linux/macOS/WSL)
# Usage: ./run-tests.sh [unit|arch|integration|e2e|all]
# Requires: .NET 9 SDK, Docker, Node.js 22+

set -e
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SUITE="${1:-all}"
FAILED=()

header() { echo -e "\n\033[0;36m$(printf '=%.0s' {1..60})\n  $1\n$(printf '=%.0s' {1..60})\033[0m\n"; }
pass()   { echo -e "\033[0;32m✓ PASSED: $1\033[0m"; }
fail()   { echo -e "\033[0;31m✗ FAILED: $1\033[0m"; FAILED+=("$1"); }

run_test() {
    local name="$1"; shift
    header "$name"
    if "$@"; then pass "$name"; else fail "$name"; fi
}

# ─── Build ────────────────────────────────────────────────────────────────────
if [[ "$SUITE" != "e2e" ]]; then
    header "Building solution"
    dotnet build "$ROOT/rvs.AlgoTrader.sln" --configuration Release --no-incremental || { echo "Build FAILED"; exit 1; }
fi

# ─── Unit Tests ───────────────────────────────────────────────────────────────
if [[ "$SUITE" == "unit" || "$SUITE" == "all" ]]; then
    run_test "Unit Tests (xUnit)" \
        dotnet test "$ROOT/tests/rvs.AlgoTrader.Tests.Unit/rvs.AlgoTrader.Tests.Unit.csproj" \
            --configuration Release --no-build \
            --logger "console;verbosity=normal" \
            --results-directory "$ROOT/test-results/unit"
fi

# ─── Architecture Tests ───────────────────────────────────────────────────────
if [[ "$SUITE" == "arch" || "$SUITE" == "all" ]]; then
    run_test "Architecture Tests (NetArchTest)" \
        dotnet test "$ROOT/tests/rvs.AlgoTrader.Tests.Architecture/rvs.AlgoTrader.Tests.Architecture.csproj" \
            --configuration Release --no-build \
            --logger "console;verbosity=normal"
fi

# ─── Integration Tests ────────────────────────────────────────────────────────
if [[ "$SUITE" == "integration" || "$SUITE" == "all" ]]; then
    header "Integration Tests (Testcontainers — needs Docker)"
    if docker info &>/dev/null; then
        run_test "Integration Tests" \
            dotnet test "$ROOT/tests/rvs.AlgoTrader.IntegrationTests/rvs.AlgoTrader.IntegrationTests.csproj" \
                --configuration Release --no-build \
                --logger "console;verbosity=normal" \
                --results-directory "$ROOT/test-results/integration"
    else
        echo "⚠ Docker not running — skipping integration tests"
    fi
fi

# ─── Frontend Vitest ──────────────────────────────────────────────────────────
if [[ "$SUITE" == "unit" || "$SUITE" == "all" ]]; then
    header "Frontend Unit Tests (Vitest)"
    pushd "$ROOT/frontend" > /dev/null
    npm ci --silent && run_test "Vitest Frontend Tests" npm test
    popd > /dev/null
fi

# ─── Playwright E2E ───────────────────────────────────────────────────────────
if [[ "$SUITE" == "e2e" || "$SUITE" == "all" ]]; then
    header "Playwright E2E Tests"

    # Start frontend dev server in background
    pushd "$ROOT/frontend" > /dev/null
    npm run dev -- --port 5173 &
    FRONTEND_PID=$!
    popd > /dev/null
    sleep 5

    trap "kill $FRONTEND_PID 2>/dev/null" EXIT

    # Install Playwright Chromium if needed
    pushd "$ROOT/tests/rvs.AlgoTrader.Tests.UI" > /dev/null
    dotnet build --configuration Release
    PLAYWRIGHT_BROWSERS_PATH="$ROOT/.playwright" \
        dotnet run --project . -- install chromium 2>/dev/null || true

    run_test "E2E Tests (Playwright)" \
        dotnet test "$ROOT/tests/rvs.AlgoTrader.Tests.UI/rvs.AlgoTrader.Tests.UI.csproj" \
            --configuration Release --no-build \
            --logger "console;verbosity=normal" \
            --results-directory "$ROOT/test-results/e2e"
    popd > /dev/null
fi

# ─── Summary ──────────────────────────────────────────────────────────────────
header "TEST SUMMARY"
if [[ ${#FAILED[@]} -eq 0 ]]; then
    echo -e "\033[0;32mAll test suites PASSED!\033[0m"
    exit 0
else
    echo -e "\033[0;31mFailed suites (${#FAILED[@]}):\033[0m"
    printf '  - %s\n' "${FAILED[@]}"
    exit 1
fi
