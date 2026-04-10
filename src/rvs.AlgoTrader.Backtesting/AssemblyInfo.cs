using System.Runtime.CompilerServices;

// Expose internal helpers (NearestWeeklyExpiry, NearestMonthlyExpiry, ExtractSpreadConfig,
// ResolveStrike, FindDeltaStrike, ComputeOtmStrike) to the unit test project.
[assembly: InternalsVisibleTo("rvs.AlgoTrader.Tests.Unit")]
