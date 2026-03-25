\# REFERENCES.md



\## Primary broker

mStock Type B SDK: https://pypi.org/project/mStock-TradingApi-B/

mStock Postman: https://www.postman.com/miraeasset-mstock/mstock-tradingapi/

mStock via OpenAlgo: https://docs.openalgo.in/connect-brokers/brokers/mstock

note: primary broker for all execution and market data



\## Broker SDKs

Zerodha .NET: https://github.com/zerodha/dotnetkiteconnect

Upstox .NET: https://github.com/upstox/upstox-dotnet



\## Architecture reference

OpenAlgo: https://github.com/marketcalls/openalgo

\- unified broker abstraction, 30+ Indian brokers

\- study for IFullBrokerClient design patterns

\- option greeks API: https://docs.openalgo.in/api-documentation/v1/data-api/optiongreeks



OpenAlgo MCP: https://github.com/marketcalls/openalgo-mcp

\- reference for MCP server exposing trading operations to AI



\## Backtest reference

backtesting.py: https://github.com/kernc/backtesting.py

\- event loop design, metrics, trade journal schema



\## Strategy reference

VCP screener: https://github.com/marco-hui-95/vcp\_screener.github.io

Indian broker quirks: https://github.com/TheHardeep/fenix



\## NSE data

Bhavcopy: https://nseindia.com/products/content/equities/equities/archieve\_eq.htm

Option chain API: https://www.nseindia.com/api/option-chain-indices?symbol=NIFTY

Event calendar: https://www.nseindia.com/companies-listing/corporate-filings-results



\## Usage rules

1\. study patterns from references, do not copy Python code into .NET

2\. when broker behavior is unclear, check Fenix or OpenAlgo implementation

3\. when backtest metrics are ambiguous, use backtesting.py definitions

4\. mStock is primary — verify unconfirmed fields live before coding against them



