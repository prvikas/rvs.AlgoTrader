-- Migration 033: PROMPT-002 enum values
-- Adds lookup rows for all new domain enums introduced by the Advanced Quant Research feature set.

INSERT INTO enum_values (domain, value, label, sort_order) VALUES
  -- SignalLayer (ordered evaluation chain)
  ('signalLayer','HTFBias',           'HTF Bias',             1),
  ('signalLayer','MTFContext',        'MTF Context',          2),
  ('signalLayer','PrimarySignal',     'Primary Signal',       3),
  ('signalLayer','ConfirmationFilter','Confirmation Filter',  4),
  ('signalLayer','EntryTrigger',      'Entry Trigger',        5),
  ('signalLayer','Invalidation',      'Invalidation',         6),
  -- EntryOrderType
  ('entryOrderType','MarketNextOpen', 'Market Next Open',  1),
  ('entryOrderType','LimitAtClose',   'Limit At Close',    2),
  ('entryOrderType','LimitAtRetest',  'Limit At Retest',   3),
  ('entryOrderType','StopEntryOffset','Stop Entry Offset', 4),
  -- EntryTiming
  ('entryTiming','immediately',        'Immediately',           1),
  ('entryTiming','waitForCandleClose', 'Wait For Candle Close', 2),
  ('entryTiming','firstNBarsOnly',     'First N Bars Only',     3),
  -- ScalingModel
  ('scalingModel','FullImmediately','Full Immediately', 1),
  ('scalingModel','ScaleIn2',       'Scale In (2)',    2),
  ('scalingModel','Pyramid',        'Pyramid',         3),
  -- SizingMethod
  ('sizingMethod','FixedRiskPct',  'Fixed Risk %',      1),
  ('sizingMethod','KellyFraction', 'Kelly Fraction',    2),
  ('sizingMethod','VolatilityNorm','Volatility Norm',   3),
  ('sizingMethod','FixedLots',     'Fixed Lots',        4),
  ('sizingMethod','FixedCapital',  'Fixed Capital (₹)', 5),
  -- ConditionOperandKind (for rule condition left/right operand picker)
  ('conditionOperandKind','indicator',   'Indicator',    1),
  ('conditionOperandKind','window',      'Window',       2),
  ('conditionOperandKind','value',       'Value',        3),
  ('conditionOperandKind','absence',     'Absence',      4),
  ('conditionOperandKind','percentile',  'Percentile',   5),
  ('conditionOperandKind','slope',       'Slope',        6),
  ('conditionOperandKind','sessionState','Session State',7),
  -- SessionStateProperty
  ('sessionStateProperty','IsFirstSignalOfSession','Is First Signal Of Session',1),
  ('sessionStateProperty','BarsSinceSessionOpen',  'Bars Since Session Open',   2),
  ('sessionStateProperty','PreviousTradeWasLoss',  'Previous Trade Was Loss',   3),
  ('sessionStateProperty','OpenPositionCount',     'Open Position Count',       4)
ON CONFLICT (domain, value) DO NOTHING;
