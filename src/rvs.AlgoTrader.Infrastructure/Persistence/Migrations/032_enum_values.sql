-- Migration 032: enum_values lookup table
-- Single source of truth for all domain primitive values shown in the UI.
-- Adding a new broker, indicator type, or timeframe is a DB INSERT — no frontend code change needed.

CREATE TABLE enum_values (
  domain      VARCHAR(64)   NOT NULL,
  value       VARCHAR(128)  NOT NULL,
  label       VARCHAR(128)  NOT NULL,
  sort_order  INT           NOT NULL DEFAULT 0,
  is_active   BOOLEAN       NOT NULL DEFAULT TRUE,
  PRIMARY KEY (domain, value)
);

-- Seed data — mirrors the TypeScript enums exactly at initial deploy.
INSERT INTO enum_values (domain, value, label, sort_order) VALUES
  -- Timeframe
  ('timeframe','1m','1 min',1), ('timeframe','3m','3 min',2), ('timeframe','5m','5 min',3),
  ('timeframe','15m','15 min',4), ('timeframe','30m','30 min',5),
  ('timeframe','1h','1 Hour',6), ('timeframe','4h','4 Hour',7), ('timeframe','1D','Daily',8),
  -- TradingStyle
  ('tradingStyle','Scalping','Scalping',1), ('tradingStyle','Intraday','Intraday',2),
  ('tradingStyle','Swing','Swing',3), ('tradingStyle','Positional','Positional',4),
  ('tradingStyle','Custom','Custom',5),
  -- IndicatorType — Trend
  ('indicatorType','EMA','EMA',1), ('indicatorType','SMA','SMA',2),
  ('indicatorType','HullMA','Hull MA',3), ('indicatorType','DonchianChannel','Donchian Channel',4),
  ('indicatorType','SuperTrend','SuperTrend',5),
  -- IndicatorType — Momentum
  ('indicatorType','RSI','RSI',10), ('indicatorType','CCI','CCI',11),
  ('indicatorType','Stochastics','Stochastics',12), ('indicatorType','MACD','MACD',13),
  -- IndicatorType — Volatility
  ('indicatorType','ATR','ATR',20), ('indicatorType','BollingerBands','Bollinger Bands',21),
  ('indicatorType','RangePercentile','Range Percentile',22),
  -- IndicatorType — Volume
  ('indicatorType','Volume','Volume',30), ('indicatorType','VolumeSpike','Volume Spike',31),
  ('indicatorType','VWAP','VWAP',32),
  -- IndicatorType — Price Action
  ('indicatorType','SwingHighLow','Swing High/Low',40), ('indicatorType','InsideBar','Inside Bar',41),
  ('indicatorType','Engulfing','Engulfing',42), ('indicatorType','PrevHighLowBreak','Prev High/Low Break',43),
  -- IndicatorType — Session/Time
  ('indicatorType','SessionFilter','Session Filter',50), ('indicatorType','DayOfWeekFilter','Day of Week Filter',51),
  -- IndicatorType — Regime
  ('indicatorType','ADX','ADX',60), ('indicatorType','ATRPercentile','ATR Percentile',61),
  -- IndicatorRole
  ('indicatorRole','EntryTrigger','Entry Trigger',1), ('indicatorRole','EntryFilter','Entry Filter',2),
  ('indicatorRole','Exit','Exit',3), ('indicatorRole','StopLoss','Stop Loss',4),
  ('indicatorRole','TakeProfit','Take Profit',5), ('indicatorRole','TrailingStop','Trailing Stop',6),
  ('indicatorRole','RiskModel','Risk Model',7), ('indicatorRole','RegimeFilter','Regime Filter',8),
  ('indicatorRole','InfoOnly','Info Only',9),
  -- ConditionOperator
  ('conditionOp','>','>',1), ('conditionOp','<','<',2), ('conditionOp','>=','>=',3),
  ('conditionOp','<=','<=',4), ('conditionOp','==','=',5), ('conditionOp','!=','!=',6),
  ('conditionOp','crossesAbove','Crosses Above',7), ('conditionOp','crossesBelow','Crosses Below',8),
  -- AggregationType
  ('aggregationType','avg','Average',1), ('aggregationType','max','Max',2),
  ('aggregationType','min','Min',3), ('aggregationType','percentile','Percentile',4),
  ('aggregationType','highest','Highest',5), ('aggregationType','lowest','Lowest',6),
  -- StopType
  ('stopType','FixedPoints','Fixed Points',1), ('stopType','ATRMultiple','ATR Multiple',2),
  ('stopType','StructureLowHigh','Structure Low/High',3),
  -- TrailingType
  ('trailingType','FixedPoints','Fixed Points',1), ('trailingType','ATRMultiple','ATR Multiple',2),
  ('trailingType','MovingAverage','Moving Average',3), ('trailingType','DonchianChannel','Donchian Channel',4),
  -- ScenarioStatus
  ('scenarioStatus','Draft','Draft',1), ('scenarioStatus','Running','Running',2),
  ('scenarioStatus','Backtested','Backtested',3), ('scenarioStatus','Fwd Testing','Fwd Testing',4),
  ('scenarioStatus','Live Candidate','Live Candidate',5), ('scenarioStatus','Live','Live',6),
  ('scenarioStatus','Archived','Archived',7),
  -- StrategyStatus
  ('strategyStatus','Draft','Draft',1), ('strategyStatus','Backtested','Backtested',2),
  ('strategyStatus','Fwd Testing','Fwd Testing',3), ('strategyStatus','Live','Live',4),
  ('strategyStatus','Archived','Archived',5),
  -- DeploymentStatus
  ('deploymentStatus','Draft','Draft',1), ('deploymentStatus','Running','Running',2),
  ('deploymentStatus','Backtested','Backtested',3), ('deploymentStatus','Fwd Testing','Fwd Testing',4),
  ('deploymentStatus','Live Candidate','Live Candidate',5), ('deploymentStatus','Live','Live',6),
  ('deploymentStatus','Archived','Archived',7),
  -- RunMode
  ('runMode','Backtest','Backtest',1), ('runMode','ForwardTest','Forward Test',2),
  -- ExitCombineLogic
  ('exitCombineLogic','AND','AND',1), ('exitCombineLogic','OR','OR',2),
  -- DayOfWeek
  ('dayOfWeek','Mon','Mon',1), ('dayOfWeek','Tue','Tue',2), ('dayOfWeek','Wed','Wed',3),
  ('dayOfWeek','Thu','Thu',4), ('dayOfWeek','Fri','Fri',5),
  ('dayOfWeek','Sat','Sat',6), ('dayOfWeek','Sun','Sun',7),
  -- MoveStopTo
  ('moveStopTo','breakeven','Breakeven',1), ('moveStopTo','previousLevel','Previous Level',2),
  ('moveStopTo','custom','Custom',3),
  -- StartCondition
  ('startCondition','immediately','Immediately',1), ('startCondition','afterKR','After K×R',2),
  ('startCondition','afterAbsoluteProfit','After Absolute Profit',3),
  -- DeploymentMode
  ('deploymentMode','Backtest','Backtest',1), ('deploymentMode','Forward Test','Forward Test',2),
  ('deploymentMode','Live','Live',3),
  -- Broker
  ('broker','MStock','MStock',1), ('broker','Zerodha','Zerodha',2), ('broker','Upstox','Upstox',3),
  -- OverrideSection
  ('overrideSection','indicator','Indicator',1), ('overrideSection','stopLoss','Stop Loss',2),
  ('overrideSection','profitTarget','Profit Target',3), ('overrideSection','trailing','Trailing',4),
  ('overrideSection','scaleOut','Scale Out',5), ('overrideSection','exitBehaviour','Exit Behaviour',6),
  ('overrideSection','rr','R:R',7);
