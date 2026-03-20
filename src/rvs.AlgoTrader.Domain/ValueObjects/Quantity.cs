namespace rvs.AlgoTrader.Domain.ValueObjects;

public readonly record struct Quantity(int Value)
{
    public static implicit operator int(Quantity q) => q.Value;
    public static implicit operator Quantity(int v) => new(v);
    public bool IsValid => Value > 0;
}
