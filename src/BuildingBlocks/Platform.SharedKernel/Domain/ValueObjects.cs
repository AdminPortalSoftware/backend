namespace Platform.SharedKernel.Domain;

/// <summary>Postal address. Mapped as an EF Core complex type (columns inlined on the owner table).</summary>
public sealed record Address
{
    public string? Line1 { get; init; }
    public string? Line2 { get; init; }
    public string? City { get; init; }
    public string? State { get; init; }
    public string? PostalCode { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code.</summary>
    public string? Country { get; init; }

    public static Address Empty { get; } = new();
}

/// <summary>Monetary amount. Always stored with its ISO 4217 currency; never mix currencies.</summary>
public sealed record Money
{
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "USD";

    private Money() { }

    public static Money Of(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new DomainException("Money amount cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new DomainException("Currency must be an ISO 4217 code.");
        }

        return new Money { Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero), Currency = currency.ToUpperInvariant() };
    }

    public static Money Zero(string currency) => Of(0, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return Of(Amount + other.Amount, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainException($"Cannot combine {Currency} with {other.Currency}.");
        }
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
