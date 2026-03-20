// Global using alias to resolve ambiguity between rvs.AlgoTrader.Domain.Interfaces.IClock
// and NodaTime.IClock — both are in scope across this project.
// All Backtesting files that write `IClock` will now resolve to the domain interface.
global using IClock = rvs.AlgoTrader.Domain.Interfaces.IClock;
