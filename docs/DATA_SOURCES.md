\# DATA\_SOURCES.md



\## Primary broker: mStock Type B

sdk: https://pypi.org/project/mStock-TradingApi-B/

postman: https://www.postman.com/miraeasset-mstock/mstock-tradingapi/

openalgo ref: https://docs.openalgo.in/connect-brokers/brokers/mstock



\## mStock API capability matrix



| Data | Method | Status |

|---|---|---|

| Historical OHLCV | get\_historical\_chart | ✅ Confirmed |

| Intraday candles 1-min | get\_intraday\_chart | ✅ Confirmed |

| Live quote OHLC | get\_market\_quote | ✅ Confirmed |

| Order placement | place\_order | ✅ Confirmed |

| Option chain OI | get\_option\_chain\_data | ✅ Confirmed |

| Change in OI | Computed from snapshots | ✅ Computable |

| PCR change in OI | Computed from above | ✅ Computable |

| IV per strike | get\_option\_chain\_data | ⚠️ VERIFY\_LIVE — response schema not public |

| Delta/Gamma/Theta | get\_option\_chain\_data | ⚠️ VERIFY\_LIVE — response schema not public |

| IVP | Computed rolling percentile | ⚠️ Needs IV confirmed + 60-day history |

| WebSocket tick stream | MTicker | ✅ Confirmed |

| Market breadth % 20DMA | ❌ Not in mStock | NSE Bhavcopy |

| Event calendar | ❌ Not in mStock | NSE corporate calendar |



\## VERIFY\_LIVE protocol

before building STRAT-002 or STRAT-003 on IV/Greeks:

1\. call get\_option\_chain\_data with a live NIFTY expiry

2\. log raw response JSON

3\. confirm IV, Delta, Gamma fields exist and are populated

4\. update this file status from VERIFY\_LIVE to Confirmed or ❌ Not available

5\. if not available: fallback to Black-Scholes compute via IOptionGreeksCalculator



\## STRAT-001 VCP data



| Data | Source | Status |

|---|---|---|

| Daily OHLCV | mStock get\_historical\_chart | ✅ |

| EMA/SMA indicators | Computed | ✅ |

| Market breadth % above 20DMA | NSE Bhavcopy + BreadthService | ⚠️ Build |



\#### BreadthService

source: https://nseindia.com/products/content/equities/equities/archieve\_eq.htm

frequency: daily after 15:30 IST

method: download CSV -> compute SMA20 per symbol -> store in market\_breadth table



\## STRAT-002 Fibonacci option spread data



| Data | Source | Status |

|---|---|---|

| Underlying OHLCV | mStock | ✅ |

| Option chain OI | mStock get\_option\_chain\_data | ✅ |

| IV per strike | mStock (VERIFY\_LIVE) | ⚠️ |

| IVP rolling | Computed from IV history | ⚠️ 60-day warmup |

| Event calendar | NSE corporate calendar | ⚠️ Build EventCalendarService |

| Fibonacci levels | Computed from swing points | ✅ |



\## STRAT-003 Intraday PCR/OI/VWAP data



| Data | Source | Status |

|---|---|---|

| Option chain OI snapshots | mStock get\_option\_chain\_data | ✅ |

| Change in OI | Computed between snapshots | ✅ |

| PCR change in OI | Computed | ✅ |

| Gamma/Delta per strike | mStock (VERIFY\_LIVE) | ⚠️ |

| Option VWAP | Computed from 1-min candles | ✅ |

| Gap detection | mStock get\_market\_quote | ✅ |



## mStock option chain field mappings (get_option_chain_data)

The following fields are expected from mStock `get_option_chain_data`. Verify each against a live NIFTY response (see VERIFY_LIVE protocol above).

| System Field | mStock JSON Field | Type | Notes |
|---|---|---|---|
| `LastTradedPrice` | `ltp` | decimal | Option premium last price |
| `ImpliedVolatility` | `iv` | decimal (annualised %) | **VERIFY_LIVE** — may be absent |
| `Delta` | `delta` | decimal | **VERIFY_LIVE** — may need Black-Scholes fallback |
| `Gamma` | `gamma` | decimal | **VERIFY_LIVE** |
| `Theta` | `theta` | decimal | **VERIFY_LIVE** |
| `Vega` | `vega` | decimal | **VERIFY_LIVE** |
| `OpenInterest` | `oi` | long | Confirmed |
| `OiChange` | computed | long | delta between consecutive snapshots |
| `BidPrice` | `bid` | decimal | **VERIFY_LIVE** |
| `AskPrice` | `ask` | decimal | **VERIFY_LIVE** |
| `Strike` | `strikePrice` | decimal | Confirmed |
| `OptionType` | `optionType` | string (`CE`/`PE`) | Confirmed |

**Action required:** Call `get_option_chain_data` for a live NIFTY weekly expiry and log the raw response JSON. Update each `VERIFY_LIVE` row above to `Confirmed` or `Not available`. If `iv`/Greeks are absent, `IBlackScholesEngine` is already wired as a fallback.

Code reference: `src/rvs.AlgoTrader.Brokers.MStock/MStockClient.cs` → `GetOptionChainSnapshotAsync`.

## NSE Bhavcopy (new — D1)

URL pattern: `https://nsearchives.nseindia.com/products/content/sec_bhavdata_full_{DDMMYYYY}.csv`
Required headers: `Referer: https://www.nseindia.com`, `User-Agent`, `Accept`
Columns used: `SYMBOL`, `SERIES` (filter EQ), `OPEN`, `HIGH`, `LOW`, `CLOSE`, `TOTTRDQTY`
Service: `NseBhavcopyCandleSource` → bulk-inserts into `candles` table as `1D` timeframe
Wired: `BreadthCalculatorJob` downloads before calling `IMarketBreadthService.ComputeAndSaveAsync`

## NSE Corporate Actions CSV import (new — D2)

Endpoint: `POST /api/event-calendar/import` (multipart CSV)
Expected CSV columns: `SYMBOL`, `PURPOSE`, `EX_DATE` (yyyy-MM-dd or dd/MM/yyyy)
Service: `NseEventCalendarImporter` → upserts into `market_events` table
PURPOSE → EventType mapping: Dividend→DIVIDEND | Bonus→BONUS | Split→SPLIT | Results→EARNINGS | Rights→RIGHTS

\## Fallback if mStock IV not confirmed

use IOptionGreeksCalculator (Black-Scholes):

inputs: underlying\_price, strike, expiry, risk\_free\_rate, option\_price

outputs: IV, Delta, Gamma, Theta, Vega

register as injectable service; swap transparently when mStock confirms or denies



\## External data needed



\### NSE Bhavcopy (breadth)

url: https://nseindia.com/products/content/equities/equities/archieve\_eq.htm

format: daily EOD CSV

service: BreadthService -> market\_breadth table



\### NSE Event Calendar

url: https://www.nseindia.com/companies-listing/corporate-filings-results

service: EventCalendarService -> event\_calendar table



