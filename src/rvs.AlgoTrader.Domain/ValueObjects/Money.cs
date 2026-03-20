namespace rvs.AlgoTrader.Domain.ValueObjects;

public readonly record struct Money(decimal Amount, string Currency = "INR")
{
    public static Money Zero => new(0m);
    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount, a.Currency);
    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount, a.Currency);
    public static Money operator *(Money a, decimal factor) => new(a.Amount * factor, a.Currency);
    public bool IsPositive => Amount > 0;
    public bool IsNegative => Amount < 0;
}
