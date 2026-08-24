namespace NEMSweep.Model.Scenarios;

/// <summary>
/// The common valuation basis for scenario cost parameters.
/// <para>
/// <see cref="Year"/> identifies the real-dollar basis year. The dimensionless
/// <see cref="RealDiscountRate"/> is expressed as a fraction, so 0.07 means 7%.
/// It must be greater than -1; annuity or escalation calculations are
/// intentionally outside this value object.
/// </para>
/// </summary>
public sealed record CostBasis
{
    /// <summary>Validates and creates a cost basis.</summary>
    /// <param name="year">The calendar year whose real Australian dollars are represented.</param>
    /// <param name="realDiscountRate">
    /// The dimensionless real discount rate as a fraction, so 0.07 means 7%. Must be greater than -1.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="year"/> is outside 1-9999, or <paramref name="realDiscountRate"/> is -1 or less.
    /// </exception>
    public CostBasis(int year, decimal realDiscountRate)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (realDiscountRate <= -1m)
        {
            throw new ArgumentOutOfRangeException(nameof(realDiscountRate));
        }

        Year = year;
        RealDiscountRate = realDiscountRate;
    }

    /// <summary>The calendar year whose real Australian dollars are represented.</summary>
    public int Year { get; }

    /// <summary>The dimensionless real discount rate expressed as a fraction.</summary>
    public decimal RealDiscountRate { get; }
}