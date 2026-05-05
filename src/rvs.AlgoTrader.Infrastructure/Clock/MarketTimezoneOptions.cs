// FILE INTENTIONALLY REMOVED
//
// MarketTimezoneOptions (and the appsettings.json MarketTimezone:TimeZoneId approach)
// has been removed. The market timezone is now a per-broker DB column:
//
//   broker_credentials.market_timezone_id  (e.g. 'Asia/Kolkata', 'America/New_York')
//
// This allows multiple brokers across different markets to run simultaneously
// without any config change or application restart.
//
// To add a broker with its timezone:
//   INSERT INTO broker_credentials (broker_name, market_timezone_id)
//   VALUES ('IBKR', 'America/New_York');
//
// Resolve timezone at runtime:
//   var tz = await _brokerTimezoneResolver.ResolveAsync(brokerName, ct);
//
// This file is a comment-only tombstone for git history clarity.
