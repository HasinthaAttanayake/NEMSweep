namespace NEMSweep.Model.Units;

/// <summary>
/// A monetary amount in Australian dollars (AUD).
/// <para>
/// Money uses <see cref="decimal"/> because cost arithmetic is base-10 and
/// should not accumulate binary floating-point artefacts. It is signed so the
/// same quantity can represent costs, credits, and net adjustments.
/// </para>
/// <para>
/// Physical quantities remain <see cref="double"/>. They cross into monetary
/// arithmetic only through typed conversion methods on the economic quantity
/// involved; callers should not combine their raw scalar values.
/// </para>
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>The signed amount in Australian dollars.</summary>
    public decimal Aud { get; }

    private Money(decimal aud) => Aud = aud;

    /// <summary>Zero Australian dollars. Seed for summing monetary amounts.</summary>
    public static Money Zero { get; } = new(0);

    /// <summary>Creates a monetary amount from Australian dollars.</summary>
    public static Money FromAud(decimal aud) => new(aud);

    /// <summary>Sums two monetary amounts.</summary>
    public static Money operator +(Money left, Money right)
        => FromAud(left.Aud + right.Aud);

    /// <summary>Subtracts one monetary amount from another.</summary>
    public static Money operator -(Money left, Money right)
        => FromAud(left.Aud - right.Aud);

    /// <summary>Scales a monetary amount by a dimensionless factor.</summary>
    public static Money operator *(Money money, decimal factor)
        => FromAud(money.Aud * factor);

    /// <summary>Scales a monetary amount by a dimensionless factor.</summary>
    public static Money operator *(decimal factor, Money money) => money * factor;

    /// <summary>
    /// Price per unit of delivered energy: AUD/MWh delivered = AUD ÷ MWh
    /// delivered. Delivered energy must be positive and finite.
    /// </summary>
    public EnergyPrice Per(Energy deliveredEnergy)
    {
        decimal megawattHours = DecimalPhysicalBoundary.RequireNonNegativeFinite(
            deliveredEnergy.MegawattHours,
            nameof(deliveredEnergy));
        if (megawattHours <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deliveredEnergy),
                "Delivered energy must be positive when deriving an energy price.");
        }

        return EnergyPrice.FromAudPerMwhDelivered(Aud / megawattHours);
    }

    /// <summary>Orders this monetary amount against another by AUD.</summary>
    public int CompareTo(Money other) => Aud.CompareTo(other.Aud);
}
