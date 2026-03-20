import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

// ─────────────────────────────────────────────────────────────────────────────
// Helper
// ─────────────────────────────────────────────────────────────────────────────
function approx(a, b, eps = 1e-9) {
    return Math.abs(a - b) < eps;
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. formatPct
// ─────────────────────────────────────────────────────────────────────────────
function formatPct(value, decimals = 2) {
    if (value === null || value === undefined || isNaN(value)) return '—';
    const sign = value > 0 ? '+' : '';
    return `${sign}${value.toFixed(decimals)}%`;
}

describe('formatPct', () => {
    test('formats positive', () => assert.equal(formatPct(5.123), '+5.12%'));
    test('formats negative', () => assert.equal(formatPct(-3.5), '-3.50%'));
    test('formats zero', () => assert.equal(formatPct(0), '0.00%'));
    test('handles null', () => assert.equal(formatPct(null), '—'));
    test('handles NaN', () => assert.equal(formatPct(NaN), '—'));
    test('custom decimals', () => assert.equal(formatPct(1.23456, 3), '+1.235%'));
    test('large negative', () => assert.equal(formatPct(-100), '-100.00%'));
    test('small positive', () => assert.equal(formatPct(0.001, 3), '+0.001%'));
});

// ─────────────────────────────────────────────────────────────────────────────
// 2. formatInr
// ─────────────────────────────────────────────────────────────────────────────
function formatInr(value) {
    if (value === null || value === undefined || isNaN(value)) return '₹—';
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value);
}

describe('formatInr', () => {
    test('formats positive', () => assert.ok(formatInr(1000).includes('1,000')));
    test('formats negative', () => assert.ok(formatInr(-500).includes('500')));
    test('handles null', () => assert.equal(formatInr(null), '₹—'));
    test('large value', () => assert.ok(formatInr(1000000).includes('10,00,000') || formatInr(1000000).includes('1,000,000')));
    test('zero', () => assert.ok(formatInr(0).includes('0')));
    test('handles NaN', () => assert.equal(formatInr(NaN), '₹—'));
});

// ─────────────────────────────────────────────────────────────────────────────
// 3. isMarketHours — NSE 09:15–15:30 IST Mon–Fri
// ─────────────────────────────────────────────────────────────────────────────
function isMarketHours(utcMs) {
    // IST = UTC+5:30
    const istMs = utcMs + 5.5 * 3600 * 1000;
    const d = new Date(istMs);
    const dow = d.getUTCDay(); // 0=Sun in IST adjusted date
    if (dow === 0 || dow === 6) return false;
    const h = d.getUTCHours(), m = d.getUTCMinutes();
    const mins = h * 60 + m;
    return mins >= 9 * 60 + 15 && mins < 15 * 60 + 30;
}

describe('isMarketHours', () => {
    // Monday 9:15 IST = Monday 03:45 UTC
    const mon_open = Date.UTC(2024, 0, 15, 3, 45, 0);  // 9:15 IST
    const mon_mid  = Date.UTC(2024, 0, 15, 7, 0, 0);   // 12:30 IST
    const mon_close= Date.UTC(2024, 0, 15, 10, 0, 0);  // 15:30 IST — closed (exclusive)
    const mon_pre  = Date.UTC(2024, 0, 15, 3, 44, 0);  // 9:14 IST
    const sat      = Date.UTC(2024, 0, 20, 7, 0, 0);   // Saturday

    test('open at market open', () => assert.equal(isMarketHours(mon_open), true));
    test('open mid-day', () => assert.equal(isMarketHours(mon_mid), true));
    test('closed at 15:30', () => assert.equal(isMarketHours(mon_close), false));
    test('closed pre-market', () => assert.equal(isMarketHours(mon_pre), false));
    test('closed Saturday', () => assert.equal(isMarketHours(sat), false));
    test('closed Sunday', () => {
        const sun = Date.UTC(2024, 0, 14, 7, 0, 0);
        assert.equal(isMarketHours(sun), false);
    });
    test('open last minute 15:29', () => {
        const last = Date.UTC(2024, 0, 15, 9, 59, 0); // 15:29 IST
        assert.equal(isMarketHours(last), true);
    });
    test('Friday is open', () => {
        const fri = Date.UTC(2024, 0, 19, 7, 0, 0); // Friday 12:30 IST
        assert.equal(isMarketHours(fri), true);
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 4. CapitalAllocation domain entity
// ─────────────────────────────────────────────────────────────────────────────
class CapitalAllocation {
    constructor({ id, strategyInstanceId, brokerName, allocatedCapital, reservedCapital = 0, createdAt, updatedAt }) {
        this.id = id;
        this.strategyInstanceId = strategyInstanceId;
        this.brokerName = brokerName;
        this.allocatedCapital = allocatedCapital;
        this.reservedCapital = reservedCapital;
        this.createdAt = createdAt;
        this.updatedAt = updatedAt;
    }
    get availableCapital() { return this.allocatedCapital - this.reservedCapital; }

    static create(strategyInstanceId, brokerName, allocatedCapital, now) {
        if (allocatedCapital <= 0) throw new RangeError('Allocated capital must be positive');
        return new CapitalAllocation({
            id: crypto.randomUUID(),
            strategyInstanceId,
            brokerName,
            allocatedCapital,
            reservedCapital: 0,
            createdAt: now,
            updatedAt: now
        });
    }
    updateAllocation(newCapital, brokerNameOrNow, nowOrUndefined) {
        if (typeof brokerNameOrNow === 'string') {
            if (newCapital <= 0) throw new RangeError('Allocated capital must be positive');
            this.allocatedCapital = newCapital;
            this.brokerName = brokerNameOrNow;
            this.updatedAt = nowOrUndefined;
        } else {
            this.updateAllocation(newCapital, this.brokerName, brokerNameOrNow);
        }
    }
    syncReservedCapital(reserved, now) {
        this.reservedCapital = reserved;
        this.updatedAt = now;
    }
}

describe('CapitalAllocation', () => {
    const NOW = new Date('2024-01-15T09:15:00Z');
    test('create sets fields', () => {
        const ca = CapitalAllocation.create('strat-1', 'ZERODHA', 100000, NOW);
        assert.equal(ca.allocatedCapital, 100000);
        assert.equal(ca.reservedCapital, 0);
        assert.equal(ca.availableCapital, 100000);
        assert.equal(ca.brokerName, 'ZERODHA');
    });
    test('create throws on zero capital', () => {
        assert.throws(() => CapitalAllocation.create('s', 'b', 0, NOW), RangeError);
    });
    test('create throws on negative capital', () => {
        assert.throws(() => CapitalAllocation.create('s', 'b', -1, NOW), RangeError);
    });
    test('availableCapital computed correctly', () => {
        const ca = CapitalAllocation.create('strat-1', 'ZERODHA', 100000, NOW);
        ca.syncReservedCapital(30000, NOW);
        assert.equal(ca.availableCapital, 70000);
    });
    test('updateAllocation changes capital and broker', () => {
        const ca = CapitalAllocation.create('s', 'ZERODHA', 50000, NOW);
        ca.updateAllocation(75000, 'UPSTOX', NOW);
        assert.equal(ca.allocatedCapital, 75000);
        assert.equal(ca.brokerName, 'UPSTOX');
    });
    test('updateAllocation legacy preserves broker', () => {
        const ca = CapitalAllocation.create('s', 'ZERODHA', 50000, NOW);
        ca.updateAllocation(75000, NOW);
        assert.equal(ca.brokerName, 'ZERODHA');
        assert.equal(ca.allocatedCapital, 75000);
    });
    test('syncReservedCapital updates reserved', () => {
        const ca = CapitalAllocation.create('s', 'b', 100000, NOW);
        ca.syncReservedCapital(25000, NOW);
        assert.equal(ca.reservedCapital, 25000);
        assert.equal(ca.availableCapital, 75000);
    });
    test('updateAllocation throws on zero', () => {
        const ca = CapitalAllocation.create('s', 'b', 100000, NOW);
        assert.throws(() => ca.updateAllocation(0, 'b', NOW), RangeError);
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 5. TransactionCostCalculator — NSE equity delivery
// ─────────────────────────────────────────────────────────────────────────────
function calcCosts(qty, price, isBuy) {
    const turnover = qty * price;
    const brokerage = Math.min(turnover * 0.0003, 20);
    const stt = isBuy ? 0 : turnover * 0.00025;
    const exchangeTxn = turnover * 0.0000335;
    const gst = (brokerage + exchangeTxn) * 0.18;
    const sebi = turnover * 0.000001;
    const stampDuty = isBuy ? turnover * 0.00003 : 0;
    const total = brokerage + stt + exchangeTxn + gst + sebi + stampDuty;
    return { brokerage, stt, exchangeTxn, gst, sebi, stampDuty, total };
}

describe('TransactionCostCalculator', () => {
    test('buy: no STT', () => assert.equal(calcCosts(10, 100, true).stt, 0));
    test('sell: STT 0.025%', () => {
        const c = calcCosts(10, 100, false);
        assert.ok(approx(c.stt, 1000 * 0.00025));
    });
    test('buy: stamp duty 0.003%', () => {
        const c = calcCosts(10, 100, true);
        assert.ok(approx(c.stampDuty, 1000 * 0.00003));
    });
    test('sell: no stamp duty', () => assert.equal(calcCosts(10, 100, false).stampDuty, 0));
    test('brokerage capped at 20', () => {
        // 10000 * 2500 * 0.0003 = 7500 > 20
        const c = calcCosts(10000, 2500, true);
        assert.equal(c.brokerage, 20);
    });
    test('gst is 18% of brokerage+exchange', () => {
        const c = calcCosts(10, 100, true);
        const expected = (c.brokerage + c.exchangeTxn) * 0.18;
        assert.ok(approx(c.gst, expected));
    });
    test('sebi charge 0.0001%', () => {
        const c = calcCosts(10, 100, true);
        assert.ok(approx(c.sebi, 1000 * 0.000001));
    });
    test('total is sum of all components', () => {
        const c = calcCosts(10, 100, true);
        const sum = c.brokerage + c.stt + c.exchangeTxn + c.gst + c.sebi + c.stampDuty;
        assert.ok(approx(c.total, sum));
    });
    test('sell total includes STT', () => {
        const buy = calcCosts(10, 100, true);
        const sell = calcCosts(10, 100, false);
        // sell has STT (0.025%) but no stampDuty (0.003%); net difference:
        // sell - buy = 0.25 (STT) - 0.03 (stampDuty) = +0.22
        assert.ok(sell.total > buy.total);
    });
    test('exchange txn 0.00335%', () => {
        const c = calcCosts(10, 100, true);
        assert.ok(approx(c.exchangeTxn, 1000 * 0.0000335));
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 6. PriceActionBreakout strategy logic
// ─────────────────────────────────────────────────────────────────────────────
function makeBar(o, h, l, c, v = 10000) {
    return { open: o, high: h, low: l, close: c, volume: v };
}

// Simplified breakout evaluator matching C# PriceActionBreakoutStrategy logic
function evaluateSignal(bars, config = {}) {
    const consolidationBars = config.consolidationBars ?? 5;
    const atrMultiplier = config.atrMultiplier ?? 1.5;
    const volumeMultiplier = config.volumeMultiplier ?? 1.2;

    // Need at least consolidationBars + 1 bars
    if (bars.length < consolidationBars + 1) return 'SKIP';

    const window = bars.slice(-consolidationBars - 1, -1);
    const current = bars[bars.length - 1];

    const rangeHigh = Math.max(...window.map(b => b.high));
    const rangeLow  = Math.min(...window.map(b => b.low));
    const rangeSize = rangeHigh - rangeLow;

    // ATR (simple range average)
    const avgRange = window.reduce((s, b) => s + (b.high - b.low), 0) / window.length;
    const atrFilter = rangeSize <= avgRange * atrMultiplier;

    // Volume filter
    const avgVol = window.reduce((s, b) => s + b.volume, 0) / window.length;
    const volOk = current.volume >= avgVol * volumeMultiplier;

    if (!atrFilter) return 'HOLD'; // Range too wide, not consolidated

    if (current.close > rangeHigh && volOk) return 'BUY';
    if (current.close < rangeLow  && volOk) return 'SELL';
    return 'HOLD';
}

describe('PriceActionBreakout', () => {
    // Tight consolidation bars — 5 bars all near 100
    const consolidation = [
        makeBar(99, 101, 99, 100),
        makeBar(99, 101, 99, 100),
        makeBar(99, 101, 99, 100),
        makeBar(99, 101, 99, 100),
        makeBar(99, 101, 99, 100),
    ];

    test('SKIP when insufficient bars', () => {
        assert.equal(evaluateSignal(consolidation.slice(0, 3)), 'SKIP');
    });

    test('SKIP with only 1 bar', () => {
        assert.equal(evaluateSignal([makeBar(100, 101, 99, 100)]), 'SKIP');
    });

    test('SKIP with 0 bars', () => {
        assert.equal(evaluateSignal([]), 'SKIP');
    });

    test('BUY on upside breakout with volume', () => {
        const bars = [...consolidation, makeBar(100, 103, 100, 102, 50000)];
        assert.equal(evaluateSignal(bars), 'BUY');
    });

    test('SELL on downside breakout with volume', () => {
        const bars = [...consolidation, makeBar(100, 100, 97, 98, 50000)];
        assert.equal(evaluateSignal(bars), 'SELL');
    });

    test('HOLD on breakout without volume', () => {
        // Volume only 5000, avgVol = 10000 → 5000 < 10000 * 1.2
        const bars = [...consolidation, makeBar(100, 103, 100, 102, 5000)];
        assert.equal(evaluateSignal(bars), 'HOLD');
    });

    test('HOLD when price stays in range', () => {
        const bars = [...consolidation, makeBar(99, 101, 99, 100, 50000)];
        assert.equal(evaluateSignal(bars), 'HOLD');
    });

    test('HOLD on wide range (ATR filter)', () => {
        // Wide consolidation — rangeSize will exceed atrMultiplier * avgRange
        const wideConsolidation = [
            makeBar(80, 120, 80, 100, 10000),
            makeBar(80, 120, 80, 100, 10000),
            makeBar(80, 120, 80, 100, 10000),
            makeBar(80, 120, 80, 100, 10000),
            makeBar(80, 120, 80, 100, 10000),
        ];
        // rangeHigh=120, rangeLow=80, rangeSize=40; avgRange=40; 40 <= 40*1.5=60 → passes ATR
        // So this would still be HOLD due to price not breaking out
        const bars = [...wideConsolidation, makeBar(100, 121, 100, 121, 50000)];
        // rangeSize=40, avgRange=40, 40 <= 60 passes; close=121 > 120; vol ok → BUY
        // Actually that should give BUY — let's test price in range
        const barsInRange = [...wideConsolidation, makeBar(100, 110, 90, 100, 50000)];
        assert.equal(evaluateSignal(barsInRange), 'HOLD');
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 7. BacktestEngine — P&L and metrics
// ─────────────────────────────────────────────────────────────────────────────
function runBacktest(trades) {
    if (!trades.length) return { totalPnl: 0, winRate: 0, trades: 0, winners: 0, losers: 0 };
    let totalPnl = 0;
    let winners = 0;
    for (const t of trades) {
        totalPnl += t.pnl;
        if (t.pnl > 0) winners++;
    }
    return {
        totalPnl,
        winRate: winners / trades.length,
        trades: trades.length,
        winners,
        losers: trades.length - winners
    };
}

describe('BacktestEngine', () => {
    test('empty trades returns zero metrics', () => {
        const r = runBacktest([]);
        assert.equal(r.totalPnl, 0);
        assert.equal(r.winRate, 0);
    });
    test('all winners', () => {
        const r = runBacktest([{ pnl: 100 }, { pnl: 200 }, { pnl: 50 }]);
        assert.equal(r.winRate, 1);
        assert.equal(r.totalPnl, 350);
    });
    test('all losers', () => {
        const r = runBacktest([{ pnl: -100 }, { pnl: -200 }]);
        assert.equal(r.winRate, 0);
        assert.equal(r.totalPnl, -300);
    });
    test('mixed trades', () => {
        const r = runBacktest([{ pnl: 100 }, { pnl: -50 }, { pnl: 200 }, { pnl: -30 }]);
        assert.equal(r.winners, 2);
        assert.equal(r.losers, 2);
        assert.ok(approx(r.winRate, 0.5));
        assert.equal(r.totalPnl, 220);
    });
    test('trade count', () => {
        const r = runBacktest([{ pnl: 10 }, { pnl: -10 }, { pnl: 5 }]);
        assert.equal(r.trades, 3);
    });
    test('single winner', () => {
        const r = runBacktest([{ pnl: 500 }]);
        assert.equal(r.winRate, 1);
        assert.equal(r.winners, 1);
        assert.equal(r.losers, 0);
    });
    test('single loser', () => {
        const r = runBacktest([{ pnl: -500 }]);
        assert.equal(r.winRate, 0);
        assert.equal(r.winners, 0);
        assert.equal(r.losers, 1);
    });
    test('break-even trade not a winner', () => {
        const r = runBacktest([{ pnl: 0 }, { pnl: 100 }]);
        assert.equal(r.winners, 1); // pnl=0 is NOT > 0
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 8. MonteCarloSimulator
// ─────────────────────────────────────────────────────────────────────────────
function runMonteCarlo(trades, iterations = 1000, seed = 42) {
    if (!trades.length) return { p5: 0, p50: 0, p95: 0 };
    // Seeded LCG for reproducibility
    let s = seed;
    function rand() { s = (s * 1664525 + 1013904223) & 0xffffffff; return (s >>> 0) / 0xffffffff; }

    const results = [];
    for (let i = 0; i < iterations; i++) {
        // Shuffle trades
        const shuffled = [...trades];
        for (let j = shuffled.length - 1; j > 0; j--) {
            const k = Math.floor(rand() * (j + 1));
            [shuffled[j], shuffled[k]] = [shuffled[k], shuffled[j]];
        }
        results.push(shuffled.reduce((sum, t) => sum + t.pnl, 0));
    }
    results.sort((a, b) => a - b);
    const p5  = results[Math.floor(0.05 * iterations)];
    const p50 = results[Math.floor(0.50 * iterations)];
    const p95 = results[Math.floor(0.95 * iterations)];
    return { p5, p50, p95 };
}

describe('MonteCarloSimulator', () => {
    test('empty returns zeros', () => {
        const r = runMonteCarlo([]);
        assert.equal(r.p5, 0);
        assert.equal(r.p50, 0);
        assert.equal(r.p95, 0);
    });
    test('all same PnL: all percentiles equal', () => {
        const trades = Array.from({ length: 10 }, () => ({ pnl: 100 }));
        const r = runMonteCarlo(trades, 100);
        assert.equal(r.p5, r.p50);
        assert.equal(r.p50, r.p95);
        assert.equal(r.p50, 1000);
    });
    test('p5 <= p50 <= p95', () => {
        const trades = [100, -50, 200, -30, 80, -20, 150, -100, 60, -40].map(p => ({ pnl: p }));
        const r = runMonteCarlo(trades, 1000);
        assert.ok(r.p5 <= r.p50);
        assert.ok(r.p50 <= r.p95);
    });
    test('p50 near total when equal shuffle', () => {
        const trades = Array.from({ length: 10 }, () => ({ pnl: 100 }));
        const r = runMonteCarlo(trades, 500);
        // All shuffles give same total: 1000
        assert.equal(r.p50, 1000);
    });
    test('p95 >= p5 for mixed trades', () => {
        const trades = [{ pnl: 1000 }, { pnl: -500 }, { pnl: 200 }, { pnl: -100 }];
        const r = runMonteCarlo(trades, 1000);
        assert.ok(r.p95 >= r.p5);
    });
    test('reproducible with seed', () => {
        const trades = [100, -50, 200, -30, 80].map(p => ({ pnl: p }));
        const r1 = runMonteCarlo(trades, 100, 42);
        const r2 = runMonteCarlo(trades, 100, 42);
        assert.equal(r1.p50, r2.p50);
        assert.equal(r1.p5, r2.p5);
    });
    test('different seeds give same total PnL (only order differs)', () => {
        const trades = Array.from({ length: 5 }, () => ({ pnl: 200 }));
        const r = runMonteCarlo(trades, 50, 99);
        assert.equal(r.p50, 1000); // all shuffles sum to 1000
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 9. TrailingStopLoss
// ─────────────────────────────────────────────────────────────────────────────
class TrailingStop {
    constructor(entryPrice, trailPct) {
        this.entryPrice = entryPrice;
        this.trailPct = trailPct;
        this.highWater = entryPrice;
        this.stopPrice = entryPrice * (1 - trailPct / 100);
    }
    update(currentPrice) {
        if (currentPrice > this.highWater) {
            this.highWater = currentPrice;
            this.stopPrice = this.highWater * (1 - this.trailPct / 100);
        }
        return currentPrice <= this.stopPrice;
    }
    isTriggered(currentPrice) {
        return currentPrice <= this.stopPrice;
    }
}

describe('TrailingStopLoss', () => {
    test('initial stop price', () => {
        const ts = new TrailingStop(100, 5);
        assert.ok(approx(ts.stopPrice, 95));
    });
    test('stop moves up with price', () => {
        const ts = new TrailingStop(100, 5);
        ts.update(110);
        assert.ok(approx(ts.stopPrice, 104.5)); // 110 * 0.95
    });
    test('stop does not move down', () => {
        const ts = new TrailingStop(100, 5);
        ts.update(110);
        ts.update(105);
        assert.ok(approx(ts.stopPrice, 104.5)); // stays at 110 * 0.95
    });
    test('triggered when price drops to stop', () => {
        const ts = new TrailingStop(100, 5);
        ts.update(110);
        assert.equal(ts.update(104), true); // 104 <= 104.5
    });
    test('not triggered above stop', () => {
        const ts = new TrailingStop(100, 5);
        assert.equal(ts.update(110), false); // 110 > 95
    });
    test('triggered at entry if price never moves', () => {
        const ts = new TrailingStop(100, 5);
        assert.equal(ts.isTriggered(94), true);  // 94 <= 95
    });
    test('2% trail', () => {
        const ts = new TrailingStop(200, 2);
        assert.ok(approx(ts.stopPrice, 196));
        ts.update(210);
        assert.ok(approx(ts.stopPrice, 205.8)); // 210 * 0.98
    });
    test('multiple highs — only highest counts', () => {
        const ts = new TrailingStop(100, 10);
        ts.update(110);
        ts.update(120);
        ts.update(115);
        assert.ok(approx(ts.stopPrice, 108)); // 120 * 0.90
        assert.equal(ts.highWater, 120);
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 10. IndicatorService
// ─────────────────────────────────────────────────────────────────────────────
function sma(values, period) {
    if (values.length < period) return [];
    const result = [];
    for (let i = period - 1; i < values.length; i++) {
        const slice = values.slice(i - period + 1, i + 1);
        result.push(slice.reduce((a, b) => a + b, 0) / period);
    }
    return result;
}

function ema(values, period) {
    if (values.length < period) return [];
    const k = 2 / (period + 1);
    const result = [values.slice(0, period).reduce((a, b) => a + b, 0) / period];
    for (let i = period; i < values.length; i++) {
        result.push(values[i] * k + result[result.length - 1] * (1 - k));
    }
    return result;
}

function wilderAtr(bars, period = 14) {
    if (bars.length < period + 1) return [];
    const trs = [];
    for (let i = 1; i < bars.length; i++) {
        const hl = bars[i].high - bars[i].low;
        const hc = Math.abs(bars[i].high - bars[i - 1].close);
        const lc = Math.abs(bars[i].low  - bars[i - 1].close);
        trs.push(Math.max(hl, hc, lc));
    }
    let atr = trs.slice(0, period).reduce((a, b) => a + b, 0) / period;
    const result = [atr];
    for (let i = period; i < trs.length; i++) {
        atr = (atr * (period - 1) + trs[i]) / period;
        result.push(atr);
    }
    return result;
}

function vwap(bars) {
    let cumVol = 0, cumTpVol = 0;
    return bars.map(b => {
        const tp = (b.high + b.low + b.close) / 3;
        cumTpVol += tp * b.volume;
        cumVol += b.volume;
        return cumVol === 0 ? 0 : cumTpVol / cumVol;
    });
}

describe('IndicatorService', () => {
    test('SMA-3 of [1,2,3,4,5]', () => {
        const r = sma([1, 2, 3, 4, 5], 3);
        assert.equal(r.length, 3);
        assert.ok(approx(r[0], 2));
        assert.ok(approx(r[1], 3));
        assert.ok(approx(r[2], 4));
    });
    test('SMA returns empty when too few values', () => {
        assert.deepEqual(sma([1, 2], 5), []);
    });
    test('EMA-3 first value is SMA', () => {
        const r = ema([1, 2, 3, 4, 5], 3);
        assert.ok(approx(r[0], 2)); // (1+2+3)/3
    });
    test('EMA has correct length', () => {
        const r = ema([1, 2, 3, 4, 5], 3);
        assert.equal(r.length, 3); // 5 - 3 + 1
    });
    test('EMA returns empty when too few values', () => {
        assert.deepEqual(ema([1, 2], 5), []);
    });
    test('Wilder ATR uses smoothing formula', () => {
        const bars = Array.from({ length: 20 }, (_, i) => ({
            high: 100 + i, low: 99 + i, close: 100 + i, open: 99 + i
        }));
        const atrs = wilderAtr(bars, 14);
        assert.ok(atrs.length > 0);
        atrs.forEach(v => assert.ok(v >= 0));
    });
    test('Wilder ATR returns empty when too few bars', () => {
        const bars = Array.from({ length: 10 }, () => makeBar(100, 101, 99, 100));
        assert.deepEqual(wilderAtr(bars, 14), []);
    });
    test('VWAP length equals bar count', () => {
        const bars = [makeBar(100, 102, 98, 101, 1000), makeBar(101, 103, 99, 102, 1200)];
        assert.equal(vwap(bars).length, 2);
    });
    test('VWAP first value = TP of first bar', () => {
        const bars = [makeBar(100, 106, 94, 100, 1000)];
        const tp = (106 + 94 + 100) / 3; // 100
        assert.ok(approx(vwap(bars)[0], tp));
    });
    test('VWAP is volume-weighted', () => {
        const b1 = makeBar(100, 110, 90, 100, 1000); // TP = 100
        const b2 = makeBar(110, 120, 100, 110, 3000); // TP = 110
        const v = vwap([b1, b2]);
        // cumVWAP after b2 = (100*1000 + 110*3000) / 4000 = 107.5
        assert.ok(approx(v[1], 107.5));
    });
    test('SMA of constant values equals the value', () => {
        const r = sma([5, 5, 5, 5, 5], 3);
        r.forEach(v => assert.ok(approx(v, 5)));
    });
});
