#!/usr/bin/env bash
# learn-from-mistake.sh
# Captures a mistake and appends it as a new Anti-Pattern (AP-NNN) to CLAUDE.md
# Run this IMMEDIATELY after spotting a mistake in Claude Code's output

set -euo pipefail

BLUE='\033[0;34m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m'

CLAUDE_FILE="CLAUDE.md"

# ─── Verify CLAUDE.md exists ──────────────────────────────────────────────────
if [ ! -f "$CLAUDE_FILE" ]; then
    echo -e "${RED}❌ CLAUDE.md not found. Run from repo root.${NC}"
    exit 1
fi

echo ""
echo -e "${BLUE}═══════════════════════════════════════════════════════${NC}"
echo -e "${BLUE}   AlgoTrader — Capture Mistake → CLAUDE.md${NC}"
echo -e "${BLUE}═══════════════════════════════════════════════════════${NC}"
echo ""

# ─── Find next AP number ──────────────────────────────────────────────────────
LAST_AP=$(grep -oE "AP-[0-9]+" "$CLAUDE_FILE" | grep -oE "[0-9]+" | sort -n | tail -1 || echo "015")
NEXT_AP=$(printf "%03d" $((10#$LAST_AP + 1)))
echo -e "${CYAN}Next anti-pattern ID: AP-${NEXT_AP}${NC}"
echo ""

# ─── Collect category ────────────────────────────────────────────────────────
echo -e "${YELLOW}Category (choose one):${NC}"
echo "  1) [CLOCK]       IClock / time handling"
echo "  2) [CONTEXT]     Bounded context violation"
echo "  3) [CANDLE]      Candle pipeline"
echo "  4) [AUDIT]       Audit log / SEBI compliance"
echo "  5) [CAPITAL]     Capital / risk management"
echo "  6) [IDEMPOTENCY] Idempotency"
echo "  7) [SECURITY]    Secrets / auth / authorization"
echo "  8) [INFRA]       Infrastructure (Polly, Redis, RabbitMQ)"
echo "  9) [TEST]        Testing patterns"
echo " 10) [PERF]        Performance"
echo " 11) [FRONTEND]    React / TypeScript"
echo " 12) [PATTERN]     Code patterns (MediatR, naming, DI)"
echo " 13) [OTHER]       Something else"
echo ""
read -rp "Enter number [1-13]: " CAT_NUM

case $CAT_NUM in
    1)  CATEGORY="[CLOCK]" ;;
    2)  CATEGORY="[CONTEXT]" ;;
    3)  CATEGORY="[CANDLE]" ;;
    4)  CATEGORY="[AUDIT]" ;;
    5)  CATEGORY="[CAPITAL]" ;;
    6)  CATEGORY="[IDEMPOTENCY]" ;;
    7)  CATEGORY="[SECURITY]" ;;
    8)  CATEGORY="[INFRA]" ;;
    9)  CATEGORY="[TEST]" ;;
    10) CATEGORY="[PERF]" ;;
    11) CATEGORY="[FRONTEND]" ;;
    12) CATEGORY="[PATTERN]" ;;
    *)  CATEGORY="[OTHER]" ;;
esac

echo ""
echo -e "${YELLOW}Short title for AP-${NEXT_AP} (max 8 words):${NC}"
read -rp "> " TITLE

echo ""
echo -e "${YELLOW}What did Claude generate WRONG? (one sentence or paste bad code, then press Enter twice):${NC}"
MISTAKE=""
while IFS= read -r line; do
    [[ -z "$line" && -n "$MISTAKE" ]] && break
    MISTAKE+="$line"$'\n'
done

echo ""
echo -e "${YELLOW}What is the CORRECT code / fix? (one sentence or paste correct code, then press Enter twice):${NC}"
FIX=""
while IFS= read -r line; do
    [[ -z "$line" && -n "$FIX" ]] && break
    FIX+="$line"$'\n'
done

echo ""
echo -e "${YELLOW}Why does this matter? (link to rule, data safety, SEBI, performance, etc.):${NC}"
read -rp "> " WHY

echo ""
echo -e "${YELLOW}File where mistake occurred (optional, press Enter to skip):${NC}"
read -rp "> " FILE_REF

# ─── Build the entry ──────────────────────────────────────────────────────────
TIMESTAMP=$(date "+%Y-%m-%d")

ENTRY="\n### AP-${NEXT_AP}: ${CATEGORY} ${TITLE}"
ENTRY+="\n**Date:** ${TIMESTAMP}"
[ -n "$FILE_REF" ] && ENTRY+="\n**File:** \`${FILE_REF}\`"
ENTRY+="\n**Mistake:** $(echo "$MISTAKE" | head -1)"

# If mistake is multi-line (code block), add fenced block
MISTAKE_LINES=$(echo "$MISTAKE" | wc -l)
if [ "$MISTAKE_LINES" -gt 2 ]; then
    ENTRY+="\n\`\`\`csharp"
    ENTRY+="\n$(echo "$MISTAKE" | tail -n +2)"
    ENTRY+="\`\`\`"
fi

ENTRY+="\n**Fix:** $(echo "$FIX" | head -1)"

FIX_LINES=$(echo "$FIX" | wc -l)
if [ "$FIX_LINES" -gt 2 ]; then
    ENTRY+="\n\`\`\`csharp"
    ENTRY+="\n$(echo "$FIX" | tail -n +2)"
    ENTRY+="\`\`\`"
fi

ENTRY+="\n**Why:** ${WHY}"
ENTRY+="\n"

# ─── Insert before the Lessons Learned Log section ───────────────────────────
# Find line number of "## 📝 Lessons Learned Log"
LESSONS_LINE=$(grep -n "## 📝 Lessons Learned Log" "$CLAUDE_FILE" | head -1 | cut -d: -f1)

if [ -n "$LESSONS_LINE" ]; then
    # Insert before the Lessons Learned section
    TEMP_FILE=$(mktemp)
    head -n "$((LESSONS_LINE - 1))" "$CLAUDE_FILE" > "$TEMP_FILE"
    echo -e "$ENTRY" >> "$TEMP_FILE"
    tail -n "+${LESSONS_LINE}" "$CLAUDE_FILE" >> "$TEMP_FILE"
    mv "$TEMP_FILE" "$CLAUDE_FILE"
else
    # Fallback: append at end of Anti-Patterns section or end of file
    echo -e "$ENTRY" >> "$CLAUDE_FILE"
fi

# ─── Update SELF_LEARNING.md session table ────────────────────────────────────
if [ -f "SELF_LEARNING.md" ]; then
    # Just log a note — table is manual
    echo ""
fi

# ─── Confirmation ─────────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
echo -e "${GREEN}✅ AP-${NEXT_AP} added to CLAUDE.md${NC}"
echo -e "${GREEN}═══════════════════════════════════════════════════════${NC}"
echo ""
echo -e "Next session, Claude will read CLAUDE.md and ${GREEN}will NOT repeat AP-${NEXT_AP}${NC}."
echo ""
echo -e "${CYAN}Recommended next steps:${NC}"
echo "  1. git add CLAUDE.md"
echo "  2. git commit -m \"docs(claude): add AP-${NEXT_AP} ${TITLE}\""
echo "  3. Update SELF_LEARNING.md session table"
echo ""

# ─── Show what was added ──────────────────────────────────────────────────────
echo -e "${YELLOW}Preview of what was added to CLAUDE.md:${NC}"
echo "─────────────────────────────────────────────────────"
echo -e "$ENTRY"
echo "─────────────────────────────────────────────────────"
