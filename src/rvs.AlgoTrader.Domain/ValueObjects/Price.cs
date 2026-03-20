namespace rvs.AlgoTrader.Domain.ValueObjects;

public readonly record struct Price(decimal Value)
{
    public static implicit operator decimal(Price p) => p.Value;
    public static implicit operator Price(decimal v) => new(v);
    public bool IsValid => Value > 0m;
}
