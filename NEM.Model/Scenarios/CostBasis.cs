namespace NEM.Model.Scenarios;

/// <summary>
/// The common valuation basis for scenario cost parameters.
/// <para>
/// <see cref="Year"/> identifies the real-dollar basis year. The dimensionless
/// <see cref="RealDiscountRate"/> is expressed as a fraction, so 0.07 means 7%.
/// It must be finite and greater than -1; annuity or escalation calculations are
/// intentionally outside this value object.
/// </para>
/// </summary>
public sealed record CostBasis
{
    public CostBasis(int year, double realDiscountRate)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (double.IsNaN(realDiscountRate)
            || double.IsInfinity(realDiscountRate)
            || realDiscountRate <= -1)
        {
            throw new ArgumentOutOfRangeException(nameof(realDiscountRate));
        }

        Year = year;
        RealDiscountRate = realDiscountRate;
    }

    /// <summary>The calendar year whose real Australian dollars are represented.</summary>
    public int Year { get; }

    /// <summary>The dimensionless real discount rate expressed as a fraction.</summary>
    public double RealDiscountRate { get; }
}