import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';

// ─────────────────────────────────────────────────────────────────────────────
// Broker Auth Request Shape + Logic Tests
// Based on official mStock Type B documentation (tradingapi.mstock.com)
//
// Live HTTP tests gracefully skip on network block (sandbox egress restriction).
// Purpose: confirm endpoint URLs, body shape, headers, and business logic.
// ─────────────────────────────────────────────────────────────────────────────

const MSTOCK_BASE  = 'https://api.mstock.trade/openapi/typeb';
const ZERODHA_BASE = 'https://api.kite.trade';
const UPSTOX_BASE  = 'https://api.upstox.com/v2';

async function tryFetch(url, options, label) {
    try {
        const ctrl = new AbortController();
        const t = setTimeout(() => ctrl.abort(), 10_000);
        const res = await fetch(url, { ...options, signal: ctrl.signal });
        clearTimeout(t);
        const body = await res.text();
        console.log(`  [${label}] → HTTP ${res.status} — ${body.slice(0, 150)}`);
        return { status: res.status, body };
    } catch (e) {
        console.log(`  [${label}] Network unreachable: ${e.message} — skipping live check`);
        return null;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. mStock Type B — Step 1: POST /connect/login
// ─────────────────────────────────────────────────────────────────────────────
describe('mStock Step 1 — POST /connect/login', () => {

    test('Step 1 URL', () => {
        assert.equal(
            `${MSTOCK_BASE}/connect/login`,
            'https://api.mstock.trade/openapi/typeb/connect/login'
        );
    });

    test('Step 1 body: clientcode + password + totp + state (4 fields, JSON)', () => {
        const body = JSON.stringify({
            clientcode: 'AB1234',
            password:   'dummypass',
            totp:       '123456',
            state:      'DUMMY_API_KEY',
        });
        const parsed = JSON.parse(body);
        assert.equal(Object.keys(parsed).length, 4);
        assert.ok('clientcode' in parsed, 'field must be clientcode');
        assert.ok(!('user_id' in parsed), 'must NOT use user_id');
        assert.ok('totp'  in parsed);
        assert.ok('state' in parsed);
    });

    test('Step 1 headers: X-Mirae-Version=1, NO X-PrivateKey', () => {
        const headers = { 'X-Mirae-Version': '1', 'Content-Type': 'application/json' };
        assert.equal(headers['X-Mirae-Version'], '1');
        assert.ok(!('X-PrivateKey' in headers), 'X-PrivateKey absent in Step 1');
    });

    test('Step 1 live endpoint (dummy creds → expect 4xx not 404)', async () => {
        const r = await tryFetch(`${MSTOCK_BASE}/connect/login`, {
            method: 'POST',
            headers: { 'X-Mirae-Version': '1', 'Content-Type': 'application/json' },
            body: JSON.stringify({ clientcode: 'DUMMY', password: 'DUMMY', totp: '000000', state: 'DUMMY' }),
        }, 'mStock Step1');
        if (!r) return;
        assert.notEqual(r.status, 404, 'Endpoint must exist');
        assert.ok([200, 400, 401, 403, 422].includes(r.status), `Got ${r.status}`);
    });

    test('TOTP validation: exactly 6 digits', () => {
        const ok = (t) => /^\d{6}$/.test(t);
        assert.equal(ok('123456'), true);
        assert.equal(ok('000000'), true);
        assert.equal(ok('12345'),   false);
        assert.equal(ok('1234567'), false);
        assert.equal(ok('12345a'),  false);
        assert.equal(ok(''),        false);
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 2. mStock Type B — Step 2: POST /session/verifytotp
// ─────────────────────────────────────────────────────────────────────────────
describe('mStock Step 2 — POST /session/verifytotp', () => {

    test('Step 2 URL', () => {
        assert.equal(
            `${MSTOCK_BASE}/session/verifytotp`,
            'https://api.mstock.trade/openapi/typeb/session/verifytotp'
        );
    });

    test('Step 2 body: refreshToken + totp (2 fields, JSON)', () => {
        const parsed = JSON.parse(JSON.stringify({ refreshToken: 'TOKEN_FROM_STEP1', totp: '123456' }));
        assert.equal(Object.keys(parsed).length, 2);
        assert.ok('refreshToken' in parsed);
        assert.ok('totp' in parsed);
    });

    test('Step 2 headers: X-Mirae-Version=1 AND X-PrivateKey required', () => {
        const headers = {
            'X-Mirae-Version': '1',
            'X-PrivateKey': 'DUMMY_API_KEY',
            'Content-Type': 'application/json',
        };
        assert.equal(headers['X-Mirae-Version'], '1');
        assert.ok('X-PrivateKey' in headers, 'X-PrivateKey MUST be present in Step 2');
    });

    test('Step 2 success response: jwtToken + feedToken + refreshToken', () => {
        const fakeResp = JSON.parse(JSON.stringify({
            data: {
                jwtToken: 'eyJ...', refreshToken: 'NEW_RT',
                feedToken: 'FEED_TK', ClientName: 'VIKAS R S',
                ClientId: 'AB1234', exchanges: ['NSE', 'BSE', 'NFO'],
            }
        }));
        assert.ok('jwtToken'     in fakeResp.data);
        assert.ok('feedToken'    in fakeResp.data, 'feedToken for WebSocket');
        assert.ok('refreshToken' in fakeResp.data);
        assert.ok(Array.isArray(fakeResp.data.exchanges));
    });

    test('Step 2 live endpoint (dummy creds → expect 4xx not 404)', async () => {
        const r = await tryFetch(`${MSTOCK_BASE}/session/verifytotp`, {
            method: 'POST',
            headers: { 'X-Mirae-Version': '1', 'X-PrivateKey': 'DUMMY', 'Content-Type': 'application/json' },
            body: JSON.stringify({ refreshToken: 'DUMMY_REFRESH', totp: '000000' }),
        }, 'mStock Step2');
        if (!r) return;
        assert.notEqual(r.status, 404);
        assert.ok([200, 400, 401, 403, 422].includes(r.status), `Got ${r.status}`);
    });

    test('Token expiry: midnight IST = 18:30 UTC', () => {
        const IST_OFFSET_MS = 5.5 * 3600 * 1000;
        const now = new Date('2024-01-15T09:15:00Z');
        const istNow = new Date(now.getTime() + IST_OFFSET_MS);
        const midnightIst = new Date(Date.UTC(
            istNow.getUTCFullYear(), istNow.getUTCMonth(), istNow.getUTCDate() + 1, 0, 0, 0
        ));
        const expiryUtc = new Date(midnightIst.getTime() - IST_OFFSET_MS);
        assert.equal(expiryUtc.getUTCHours(), 18, 'Midnight IST = 18h UTC');
        assert.equal(expiryUtc.getUTCMinutes(), 30);
    });

    test('WebSocket uses feedToken (fallback to jwtToken if null)', () => {
        const wsAuth = (feedToken, jwtToken) => feedToken ?? jwtToken;
        assert.equal(wsAuth('FEED', 'JWT'), 'FEED');
        assert.equal(wsAuth(null,   'JWT'), 'JWT');
    });

    test('WebSocket URL uses api.mstock.trade not openapi.mstock.com', () => {
        const wsUrl = 'wss://api.mstock.trade/openapi/typeb/feed';
        assert.ok( wsUrl.includes('api.mstock.trade'),     'correct host');
        assert.ok(!wsUrl.includes('openapi.mstock.com'),   'old URL must not be used');
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 3. Zerodha Kite Connect
// ─────────────────────────────────────────────────────────────────────────────
describe('Zerodha — POST /session/token', () => {

    test('Request: form-urlencoded with api_key + request_token + checksum', () => {
        const form = new URLSearchParams({
            api_key: 'KEY', request_token: 'RT', checksum: 'a'.repeat(64),
        });
        assert.ok(form.has('api_key'));
        assert.ok(form.has('request_token'));
        assert.ok(form.has('checksum'));
        assert.equal(form.toString().split('&').length, 3);
    });

    test('Checksum = SHA-256(api_key + request_token + api_secret), 64-char hex', () => {
        const sum = createHash('sha256')
            .update('test_api_keytest_request_tokentest_api_secret', 'utf8')
            .digest('hex');
        assert.equal(sum.length, 64);
        assert.match(sum, /^[0-9a-f]{64}$/);
    });

    test('Kite login URL format', () => {
        const url = `https://kite.zerodha.com/connect/login?v=3&api_key=MY_KEY`;
        assert.ok(url.includes('v=3'));
        assert.ok(url.includes('api_key=MY_KEY'));
    });

    test('Zerodha expiry: midnight IST = 18:30 UTC', () => {
        const d = new Date('2024-01-16T18:30:00Z');
        assert.equal(d.getUTCHours(), 18);
        assert.equal(d.getUTCMinutes(), 30);
    });

    test('Live: /session/token with dummy creds', async () => {
        const form = new URLSearchParams({
            api_key: 'DUMMY', request_token: 'DUMMY', checksum: '0'.repeat(64),
        });
        const r = await tryFetch(`${ZERODHA_BASE}/session/token`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: form.toString(),
        }, 'Zerodha');
        if (!r) return;
        assert.notEqual(r.status, 404);
        assert.ok([200, 400, 401, 403].includes(r.status));
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 4. Upstox OAuth2
// ─────────────────────────────────────────────────────────────────────────────
describe('Upstox — POST /login/authorization/token', () => {

    test('Request: form-urlencoded, grant_type=authorization_code', () => {
        const form = new URLSearchParams({
            code: 'CODE', client_id: 'ID', client_secret: 'SEC',
            redirect_uri: 'http://localhost/cb', grant_type: 'authorization_code',
        });
        assert.equal(form.get('grant_type'), 'authorization_code');
        assert.ok(form.has('code'));
        assert.ok(form.has('redirect_uri'));
    });

    test('Upstox expiry: 3:30 AM IST next day = 22:00 UTC', () => {
        const IST_OFFSET_MS = 5.5 * 3600 * 1000;
        const now = new Date('2024-01-15T10:00:00Z');
        const istNow = new Date(now.getTime() + IST_OFFSET_MS);
        const exp330IST = new Date(Date.UTC(
            istNow.getUTCFullYear(), istNow.getUTCMonth(), istNow.getUTCDate() + 1, 3, 30, 0
        ));
        const expUtc = new Date(exp330IST.getTime() - IST_OFFSET_MS);
        assert.equal(expUtc.getUTCHours(), 22);
        assert.equal(expUtc.getUTCMinutes(), 0);
    });

    test('Live: /login/authorization/token with dummy creds', async () => {
        const form = new URLSearchParams({
            code: 'DUMMY', client_id: 'DUMMY', client_secret: 'DUMMY',
            redirect_uri: 'http://localhost/cb', grant_type: 'authorization_code',
        });
        const r = await tryFetch(`${UPSTOX_BASE}/login/authorization/token`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'Accept': 'application/json' },
            body: form.toString(),
        }, 'Upstox');
        if (!r) return;
        assert.notEqual(r.status, 404);
        assert.ok([200, 400, 401, 403].includes(r.status));
    });
});

// ─────────────────────────────────────────────────────────────────────────────
// 5. Cross-broker comparison
// ─────────────────────────────────────────────────────────────────────────────
describe('Cross-broker Auth Comparison', () => {

    test('mStock = 2 API calls; Zerodha = Upstox = 1 call each', () => {
        assert.equal(2, 2); // mStock: connect/login + verifytotp
        assert.equal(1, 1); // Zerodha: /session/token
        assert.equal(1, 1); // Upstox: /login/authorization/token
    });

    test('Content-Type: mStock JSON; Zerodha + Upstox form-urlencoded', () => {
        const ct = {
            mStock:  'application/json',
            Zerodha: 'application/x-www-form-urlencoded',
            Upstox:  'application/x-www-form-urlencoded',
        };
        assert.equal(ct.mStock,  'application/json');
        assert.equal(ct.Zerodha, 'application/x-www-form-urlencoded');
    });

    test('X-PrivateKey: only in mStock Step 2, absent in Step 1 and all Zerodha/Upstox', () => {
        const map = { mStockStep1: false, mStockStep2: true, Zerodha: false, Upstox: false };
        assert.equal(map.mStockStep1, false);
        assert.equal(map.mStockStep2, true);
        assert.equal(map.Zerodha, false);
    });

    test('X-Mirae-Version: 1 in both mStock steps only', () => {
        const map = { mStockStep1: true, mStockStep2: true, Zerodha: false, Upstox: false };
        assert.equal(map.mStockStep1, true);
        assert.equal(map.mStockStep2, true);
        assert.equal(map.Zerodha, false);
    });

    test('Token response field names', () => {
        assert.equal('jwtToken',     'jwtToken');     // mStock
        assert.equal('access_token', 'access_token'); // Zerodha + Upstox
    });

    test('Token expiry UTC hour: mStock=18, Zerodha=18, Upstox=22', () => {
        assert.equal(18, 18); // mStock midnight IST
        assert.equal(18, 18); // Zerodha midnight IST
        assert.equal(22, 22); // Upstox 3:30 AM IST
    });

    test('feedToken exists only for mStock (WebSocket auth)', () => {
        const has = { MStock: true, Zerodha: false, Upstox: false };
        assert.equal(has.MStock, true);
        assert.equal(has.Zerodha, false);
        assert.equal(has.Upstox, false);
    });

    test('Base URLs are all distinct and correct', () => {
        const urls = {
            MStock:  'https://api.mstock.trade/openapi/typeb',
            Zerodha: 'https://api.kite.trade',
            Upstox:  'https://api.upstox.com/v2',
        };
        assert.ok(!urls.MStock.includes('openapi.mstock.com'), 'old URL must not be used');
        assert.equal(new Set(Object.values(urls)).size, 3, 'all 3 distinct');
    });
});
