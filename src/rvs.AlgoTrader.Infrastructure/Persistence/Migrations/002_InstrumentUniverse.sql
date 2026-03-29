-- Migration 002: instrument_universe table + default seed
-- Defines the universe of instruments the system will store.
-- Only NSE equities in this table + all NSE indexes + NFO options/futures
-- on listed underlyings (within expiry window) are persisted during refresh.
-- All other instruments (BSE equities, MCX, CDS, BFO, NFO stock options, etc.) are discarded.
--
-- Run after InitialMigration.sql.
-- Add symbols via INSERT or via the UI (InstrumentUniversePage).

CREATE TABLE IF NOT EXISTS instrument_universe (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    symbol     VARCHAR(50)  NOT NULL,
    exchange   VARCHAR(10)  NOT NULL,  -- "NSE" for equities/indexes, "NFO" for option underlyings
    category   VARCHAR(30)  NOT NULL,  -- "NSE_EQUITY" | "OPTIONS_UNDERLYING"
    is_active  BOOL         NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    UNIQUE (symbol, exchange, category)
);

-- EF's EnsureCreatedAsync creates columns without DB-level defaults (it sets
-- values in C# instead). Ensure defaults exist so seed INSERTs that omit these
-- columns get correct values rather than NULL constraint violations.
ALTER TABLE instrument_universe ALTER COLUMN id        SET DEFAULT gen_random_uuid();
ALTER TABLE instrument_universe ALTER COLUMN is_active SET DEFAULT TRUE;
ALTER TABLE instrument_universe ALTER COLUMN created_at SET DEFAULT NOW();

CREATE INDEX IF NOT EXISTS idx_instrument_universe_category ON instrument_universe(category, is_active);

-- ────────────────────────────────────────────────────────────────────────────
-- SEED: Option Underlyings — NFO index options/futures tracked by this system
-- ────────────────────────────────────────────────────────────────────────────
INSERT INTO instrument_universe (symbol, exchange, category) VALUES
('NIFTY',       'NFO', 'OPTIONS_UNDERLYING'),   -- NIFTY 50 index options (weekly + monthly)
('BANKNIFTY',   'NFO', 'OPTIONS_UNDERLYING'),   -- NIFTY BANK index options
('FINNIFTY',    'NFO', 'OPTIONS_UNDERLYING'),   -- NIFTY FIN SERVICE index options
('MIDCPNIFTY',  'NFO', 'OPTIONS_UNDERLYING'),   -- NIFTY MIDCAP SELECT options
('NIFTYNXT50',  'NFO', 'OPTIONS_UNDERLYING')    -- NIFTY NEXT 50 options
ON CONFLICT (symbol, exchange, category) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- SEED: NSE_EQUITY — NIFTY 50 (50 stocks)
-- ────────────────────────────────────────────────────────────────────────────
INSERT INTO instrument_universe (symbol, exchange, category) VALUES
('ADANIENT',    'NSE', 'NSE_EQUITY'),
('ADANIPORTS',  'NSE', 'NSE_EQUITY'),
('APOLLOHOSP',  'NSE', 'NSE_EQUITY'),
('ASIANPAINT',  'NSE', 'NSE_EQUITY'),
('AXISBANK',    'NSE', 'NSE_EQUITY'),
('BAJAJ-AUTO',  'NSE', 'NSE_EQUITY'),
('BAJFINANCE',  'NSE', 'NSE_EQUITY'),
('BAJAJFINSV',  'NSE', 'NSE_EQUITY'),
('BPCL',        'NSE', 'NSE_EQUITY'),
('BHARTIARTL',  'NSE', 'NSE_EQUITY'),
('BRITANNIA',   'NSE', 'NSE_EQUITY'),
('CIPLA',       'NSE', 'NSE_EQUITY'),
('COALINDIA',   'NSE', 'NSE_EQUITY'),
('DIVISLAB',    'NSE', 'NSE_EQUITY'),
('DRREDDY',     'NSE', 'NSE_EQUITY'),
('EICHERMOT',   'NSE', 'NSE_EQUITY'),
('GRASIM',      'NSE', 'NSE_EQUITY'),
('HCLTECH',     'NSE', 'NSE_EQUITY'),
('HDFCBANK',    'NSE', 'NSE_EQUITY'),
('HDFCLIFE',    'NSE', 'NSE_EQUITY'),
('HEROMOTOCO',  'NSE', 'NSE_EQUITY'),
('HINDALCO',    'NSE', 'NSE_EQUITY'),
('HINDUNILVR',  'NSE', 'NSE_EQUITY'),
('ICICIBANK',   'NSE', 'NSE_EQUITY'),
('ITC',         'NSE', 'NSE_EQUITY'),
('INDUSINDBK',  'NSE', 'NSE_EQUITY'),
('INFY',        'NSE', 'NSE_EQUITY'),
('JSWSTEEL',    'NSE', 'NSE_EQUITY'),
('KOTAKBANK',   'NSE', 'NSE_EQUITY'),
('LT',          'NSE', 'NSE_EQUITY'),
('LTIM',        'NSE', 'NSE_EQUITY'),
('M&M',         'NSE', 'NSE_EQUITY'),
('MARUTI',      'NSE', 'NSE_EQUITY'),
('NESTLEIND',   'NSE', 'NSE_EQUITY'),
('NTPC',        'NSE', 'NSE_EQUITY'),
('ONGC',        'NSE', 'NSE_EQUITY'),
('POWERGRID',   'NSE', 'NSE_EQUITY'),
('RELIANCE',    'NSE', 'NSE_EQUITY'),
('SBILIFE',     'NSE', 'NSE_EQUITY'),
('SBIN',        'NSE', 'NSE_EQUITY'),
('SUNPHARMA',   'NSE', 'NSE_EQUITY'),
('TCS',         'NSE', 'NSE_EQUITY'),
('TATACONSUM',  'NSE', 'NSE_EQUITY'),
('TATAMOTORS',  'NSE', 'NSE_EQUITY'),
('TATASTEEL',   'NSE', 'NSE_EQUITY'),
('TECHM',       'NSE', 'NSE_EQUITY'),
('TITAN',       'NSE', 'NSE_EQUITY'),
('TRENT',       'NSE', 'NSE_EQUITY'),
('ULTRACEMCO',  'NSE', 'NSE_EQUITY'),
('WIPRO',       'NSE', 'NSE_EQUITY')
ON CONFLICT (symbol, exchange, category) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- SEED: NSE_EQUITY — NIFTY NEXT 50 (50 stocks)
-- ────────────────────────────────────────────────────────────────────────────
INSERT INTO instrument_universe (symbol, exchange, category) VALUES
('ABB',         'NSE', 'NSE_EQUITY'),
('ADANIGREEN',  'NSE', 'NSE_EQUITY'),
('ADANIPOWER',  'NSE', 'NSE_EQUITY'),
('AMBUJACEM',   'NSE', 'NSE_EQUITY'),
('ATGL',        'NSE', 'NSE_EQUITY'),
('BAJAJHLDNG',  'NSE', 'NSE_EQUITY'),
('BANKBARODA',  'NSE', 'NSE_EQUITY'),
('BERGEPAINT',  'NSE', 'NSE_EQUITY'),
('BOSCHLTD',    'NSE', 'NSE_EQUITY'),
('CANBK',       'NSE', 'NSE_EQUITY'),
('CHOLAFIN',    'NSE', 'NSE_EQUITY'),
('COLPAL',      'NSE', 'NSE_EQUITY'),
('DLF',         'NSE', 'NSE_EQUITY'),
('DMART',       'NSE', 'NSE_EQUITY'),
('GAIL',        'NSE', 'NSE_EQUITY'),
('GODREJCP',    'NSE', 'NSE_EQUITY'),
('HAVELLS',     'NSE', 'NSE_EQUITY'),
('HINDPETRO',   'NSE', 'NSE_EQUITY'),
('ICICIGI',     'NSE', 'NSE_EQUITY'),
('ICICIPRULI',  'NSE', 'NSE_EQUITY'),
('INDHOTEL',    'NSE', 'NSE_EQUITY'),
('IOC',         'NSE', 'NSE_EQUITY'),
('IRCTC',       'NSE', 'NSE_EQUITY'),
('LODHA',       'NSE', 'NSE_EQUITY'),
('LUPIN',       'NSE', 'NSE_EQUITY'),
('MARICO',      'NSE', 'NSE_EQUITY'),
('MCDOWELL-N',  'NSE', 'NSE_EQUITY'),
('MUTHOOTFIN',  'NSE', 'NSE_EQUITY'),
('NAUKRI',      'NSE', 'NSE_EQUITY'),
('OBEROIRLTY',  'NSE', 'NSE_EQUITY'),
('OFSS',        'NSE', 'NSE_EQUITY'),
('PERSISTENT',  'NSE', 'NSE_EQUITY'),
('PIIND',       'NSE', 'NSE_EQUITY'),
('PIDILITIND',  'NSE', 'NSE_EQUITY'),
('RECLTD',      'NSE', 'NSE_EQUITY'),
('SAIL',        'NSE', 'NSE_EQUITY'),
('SHREECEM',    'NSE', 'NSE_EQUITY'),
('SIEMENS',     'NSE', 'NSE_EQUITY'),
('SRF',         'NSE', 'NSE_EQUITY'),
('TATAPOWER',   'NSE', 'NSE_EQUITY'),
('TORNTPHARM',  'NSE', 'NSE_EQUITY'),
('TVSMOTOR',    'NSE', 'NSE_EQUITY'),
('UBL',         'NSE', 'NSE_EQUITY'),
('UNIONBANK',   'NSE', 'NSE_EQUITY'),
('VBL',         'NSE', 'NSE_EQUITY'),
('VEDL',        'NSE', 'NSE_EQUITY'),
('VOLTAS',      'NSE', 'NSE_EQUITY'),
('ZOMATO',      'NSE', 'NSE_EQUITY'),
('SWIGGY',      'NSE', 'NSE_EQUITY'),
('HYUNDAI',     'NSE', 'NSE_EQUITY')
ON CONFLICT (symbol, exchange, category) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- SEED: NSE_EQUITY — NIFTY MIDCAP 150 (150 stocks)
-- ────────────────────────────────────────────────────────────────────────────
INSERT INTO instrument_universe (symbol, exchange, category) VALUES
('ABCAPITAL',   'NSE', 'NSE_EQUITY'),
('ABFRL',       'NSE', 'NSE_EQUITY'),
('ACC',         'NSE', 'NSE_EQUITY'),
('ALKEM',       'NSE', 'NSE_EQUITY'),
('APOLLOTYRE',  'NSE', 'NSE_EQUITY'),
('ASHOKLEY',    'NSE', 'NSE_EQUITY'),
('ASTRAL',      'NSE', 'NSE_EQUITY'),
('AUROPHARMA',  'NSE', 'NSE_EQUITY'),
('AUBANK',      'NSE', 'NSE_EQUITY'),
('BALKRISIND',  'NSE', 'NSE_EQUITY'),
('BANKINDIA',   'NSE', 'NSE_EQUITY'),
('BEL',         'NSE', 'NSE_EQUITY'),
('BHEL',        'NSE', 'NSE_EQUITY'),
('BIOCON',      'NSE', 'NSE_EQUITY'),
('BSOFT',       'NSE', 'NSE_EQUITY'),
('CAMS',        'NSE', 'NSE_EQUITY'),
('CANFINHOME',  'NSE', 'NSE_EQUITY'),
('CDSL',        'NSE', 'NSE_EQUITY'),
('CGPOWER',     'NSE', 'NSE_EQUITY'),
('COFORGE',     'NSE', 'NSE_EQUITY'),
('CONCOR',      'NSE', 'NSE_EQUITY'),
('CUMMINSIND',  'NSE', 'NSE_EQUITY'),
('DABUR',       'NSE', 'NSE_EQUITY'),
('DELHIVERY',   'NSE', 'NSE_EQUITY'),
('DIXON',       'NSE', 'NSE_EQUITY'),
('ELGIEQUIP',   'NSE', 'NSE_EQUITY'),
('ESCORTS',     'NSE', 'NSE_EQUITY'),
('EXIDEIND',    'NSE', 'NSE_EQUITY'),
('FEDERALBNK',  'NSE', 'NSE_EQUITY'),
('FORTIS',      'NSE', 'NSE_EQUITY'),
('GLENMARK',    'NSE', 'NSE_EQUITY'),
('GNFC',        'NSE', 'NSE_EQUITY'),
('GODREJIND',   'NSE', 'NSE_EQUITY'),
('GODREJPROP',  'NSE', 'NSE_EQUITY'),
('HAL',         'NSE', 'NSE_EQUITY'),
('HFCL',        'NSE', 'NSE_EQUITY'),
('IDFCFIRSTB',  'NSE', 'NSE_EQUITY'),
('IGL',         'NSE', 'NSE_EQUITY'),
('INDIGO',      'NSE', 'NSE_EQUITY'),
('INDUSTOWER',  'NSE', 'NSE_EQUITY'),
('IPCALAB',     'NSE', 'NSE_EQUITY'),
('IRFC',        'NSE', 'NSE_EQUITY'),
('JBCHEPHARM',  'NSE', 'NSE_EQUITY'),
('JKCEMENT',    'NSE', 'NSE_EQUITY'),
('JUBLFOOD',    'NSE', 'NSE_EQUITY'),
('KAJARIACER',  'NSE', 'NSE_EQUITY'),
('KPITTECH',    'NSE', 'NSE_EQUITY'),
('LICHSGFIN',   'NSE', 'NSE_EQUITY'),
('LTTS',        'NSE', 'NSE_EQUITY'),
('MCX',         'NSE', 'NSE_EQUITY'),
('MFSL',        'NSE', 'NSE_EQUITY'),
('MPHASIS',     'NSE', 'NSE_EQUITY'),
('NATIONALUM',  'NSE', 'NSE_EQUITY'),
('NAVINFLUOR',  'NSE', 'NSE_EQUITY'),
('NBCC',        'NSE', 'NSE_EQUITY'),
('NCC',         'NSE', 'NSE_EQUITY'),
('NHPC',        'NSE', 'NSE_EQUITY'),
('NLCINDIA',    'NSE', 'NSE_EQUITY'),
('NYKAA',       'NSE', 'NSE_EQUITY'),
('PAGEIND',     'NSE', 'NSE_EQUITY'),
('PATANJALI',   'NSE', 'NSE_EQUITY'),
('PEL',         'NSE', 'NSE_EQUITY'),
('PHOENIXLTD',  'NSE', 'NSE_EQUITY'),
('PIRAMALENT',  'NSE', 'NSE_EQUITY'),
('POLYCAB',     'NSE', 'NSE_EQUITY'),
('PVRINOX',     'NSE', 'NSE_EQUITY'),
('RBLBANK',     'NSE', 'NSE_EQUITY'),
('RVNL',        'NSE', 'NSE_EQUITY'),
('SBICARD',     'NSE', 'NSE_EQUITY'),
('SJVN',        'NSE', 'NSE_EQUITY'),
('SONACOMS',    'NSE', 'NSE_EQUITY'),
('SUNTV',       'NSE', 'NSE_EQUITY'),
('SUZLON',      'NSE', 'NSE_EQUITY'),
('TATACHEM',    'NSE', 'NSE_EQUITY'),
('TATACOMM',    'NSE', 'NSE_EQUITY'),
('THERMAX',     'NSE', 'NSE_EQUITY'),
('TIINDIA',     'NSE', 'NSE_EQUITY'),
('TORNTPOWER',  'NSE', 'NSE_EQUITY'),
('TRIDENT',     'NSE', 'NSE_EQUITY'),
('TTKPRESTIGE', 'NSE', 'NSE_EQUITY'),
('UJJIVAN',     'NSE', 'NSE_EQUITY'),
('VINATIORGA',  'NSE', 'NSE_EQUITY'),
('YESBANK',     'NSE', 'NSE_EQUITY'),
('ZEEL',        'NSE', 'NSE_EQUITY'),
('ZYDUSLIFE',   'NSE', 'NSE_EQUITY'),
-- Additional liquid midcap/smallcap often traded
('ADANIENSOL',  'NSE', 'NSE_EQUITY'),
('ANGELONE',    'NSE', 'NSE_EQUITY'),
('BSE',         'NSE', 'NSE_EQUITY'),
('CHALET',      'NSE', 'NSE_EQUITY'),
('CRISIL',      'NSE', 'NSE_EQUITY'),
('DEEPAKNTR',   'NSE', 'NSE_EQUITY'),
('EMAMILTD',    'NSE', 'NSE_EQUITY'),
('FLUOROCHEM',  'NSE', 'NSE_EQUITY'),
('GRINDWELL',   'NSE', 'NSE_EQUITY'),
('HINDCOPPER',  'NSE', 'NSE_EQUITY'),
('HSCL',        'NSE', 'NSE_EQUITY'),
('IDEA',        'NSE', 'NSE_EQUITY'),
('INDIANB',     'NSE', 'NSE_EQUITY'),
('INOXGREEN',   'NSE', 'NSE_EQUITY'),
('JSWENERGY',   'NSE', 'NSE_EQUITY'),
('JYOTHYLAB',   'NSE', 'NSE_EQUITY'),
('KALYANKJIL',  'NSE', 'NSE_EQUITY'),
('KEI',         'NSE', 'NSE_EQUITY'),
('LATENTVIEW',  'NSE', 'NSE_EQUITY'),
('LINDEINDIA',  'NSE', 'NSE_EQUITY'),
('MAZDOCK',     'NSE', 'NSE_EQUITY'),
('METROBRAND',  'NSE', 'NSE_EQUITY'),
('MOTILALOFS',  'NSE', 'NSE_EQUITY'),
('MRF',         'NSE', 'NSE_EQUITY'),
('MTAR',        'NSE', 'NSE_EQUITY'),
('NMDC',        'NSE', 'NSE_EQUITY'),
('NUVAMA',      'NSE', 'NSE_EQUITY'),
('OIL',         'NSE', 'NSE_EQUITY'),
('OLECTRA',     'NSE', 'NSE_EQUITY'),
('PAYTM',       'NSE', 'NSE_EQUITY'),
('PCBL',        'NSE', 'NSE_EQUITY'),
('PFC',         'NSE', 'NSE_EQUITY'),
('PGHH',        'NSE', 'NSE_EQUITY'),
('POLYMED',     'NSE', 'NSE_EQUITY'),
('PRESTIGE',    'NSE', 'NSE_EQUITY'),
('PRINCEPIPE',  'NSE', 'NSE_EQUITY'),
('RAYMOND',     'NSE', 'NSE_EQUITY'),
('SAFARI',      'NSE', 'NSE_EQUITY'),
('SOLARINDS',   'NSE', 'NSE_EQUITY'),
('STARHEALTH',  'NSE', 'NSE_EQUITY'),
('SUPREMEIND',  'NSE', 'NSE_EQUITY'),
('SWSOLAR',     'NSE', 'NSE_EQUITY'),
('TANLA',       'NSE', 'NSE_EQUITY'),
('TATAELXSI',   'NSE', 'NSE_EQUITY'),
('TATAINVEST',  'NSE', 'NSE_EQUITY'),
('TCNSBRANDS',  'NSE', 'NSE_EQUITY'),
('TITAGARH',    'NSE', 'NSE_EQUITY'),
('UCOBANK',     'NSE', 'NSE_EQUITY'),
('VAIBHAVGBL',  'NSE', 'NSE_EQUITY'),
('VIJAYABANK',  'NSE', 'NSE_EQUITY'),
('WOCKPHARMA',  'NSE', 'NSE_EQUITY')
ON CONFLICT (symbol, exchange, category) DO NOTHING;

-- ────────────────────────────────────────────────────────────────────────────
-- To add more symbols: INSERT INTO instrument_universe (symbol, exchange, category) VALUES (...)
-- To remove a symbol:  UPDATE instrument_universe SET is_active = false WHERE symbol = '...'
-- To change expiry window: UPDATE app_config SET value = '3' WHERE key = 'InstrumentFilter:NfoExpiryWeeks'
-- ────────────────────────────────────────────────────────────────────────────

-- Default config: NFO option expiry lookahead = 4 weeks
-- app_config schema: key, value, updated_at (no description/updated_by columns)
INSERT INTO app_config (key, value, updated_at)
VALUES ('InstrumentFilter:NfoExpiryWeeks', '4', NOW())
ON CONFLICT (key) DO NOTHING;
