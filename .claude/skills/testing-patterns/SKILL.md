---

name: testing-patterns
description: Apply consistent test patterns across unit, integration, parity, and E2E tests
model: sonnet

---



# Testing patterns



## Test layer rules



| Layer | Tool | When |

|---|---|---|

| Unit | xUnit + Moq + FluentAssertions | domain logic, strategies, indicators, services |

| Integration | Testcontainers + Respawn | DB, Redis, RabbitMQ, broker adapters |

| Architecture | NetArchTest | enforce layer rules in CI |

| Parity | xUnit custom | verify backtest == forward test signal sequence |

| E2E | Playwright | critical user workflows |



## SimulatedClock in tests

- inject SimulatedClock for all time-dependent tests

- never use SystemClock in unit or integration tests

- advance clock explicitly per candle or per step



## Strategy unit test pattern


[Fact]

public async Task EvaluateAsync\_GivenBullishSetup\_ReturnsBuySignal()

{

   var clock = new SimulatedClock(TestInstants.MarketOpen);

 var strategy = new VcpStrategy(clock, ...);

  var candles = CandleBuilder.BuildVcpSetup();

 var ctx = new StrategyContext(candles, ...);



  var result = await strategy.EvaluateAsync(ctx, CancellationToken.None);



  result.Signal.Should().Be(SignalType.Buy);

   result.StopLoss.Should().BeGreaterThan(0);

}



