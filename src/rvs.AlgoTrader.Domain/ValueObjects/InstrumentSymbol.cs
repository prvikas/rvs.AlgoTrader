namespace rvs.AlgoTrader.Domain.ValueObjects;

public readonly record struct InstrumentSymbol(string Value)
{
    public static implicit operator string(InstrumentSymbol s) => s.Value;
    public static implicit operator InstrumentSymbol(string v) => new(v);
    public bool IsValid => !string.IsNullOrWhiteSpace(Value);
    public override string ToString() => Value;
}
