// FILE INTENTIONALLY REMOVED
//
// MarketTimezoneService has been deleted because it read the timezone from
// appsettings.json (MarketTimezoneOptions), which meant:
//   - Only one timezone could be active at a time
//   - Changing timezone required an app restart
//   - Multiple overlapping markets (India + US + UK) could not coexist
//
// The correct approach is to store the IANA timezone on the broker_credentials
// DB row (market_timezone_id column) and resolve it at runtime via:
//
//   IBrokerTimezoneResolver.ResolveAsync(brokerName, ct)
//
// See:
//   - src/rvs.AlgoTrader.Domain/Interfaces/IBrokerTimezoneResolver.cs
//   - src/rvs.AlgoTrader.Infrastructure/Clock/BrokerTimezoneResolver.cs
//   - src/rvs.AlgoTrader.Infrastructure/Clock/BrokerMarketTimezone.cs
//
// This file is kept as a one-line comment tombstone so git history explains
// the removal. It does not contain any compilable code.
