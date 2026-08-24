namespace NEMSweep.Model.Units
{
    /// <summary>
    /// Power in megawatts (MW): a rate, not a quantity of energy.
    /// <para>
    /// A power figure is either an average over an interval (e.g. metered demand
    /// over a block) or a capacity (e.g. a generator's nameplate rating). To get
    /// energy you must multiply by a duration; see <see cref="Energy.From"/>.
    /// When the power is an interval average, energy = power × duration is exact.
    /// </para>
    /// <para>
    /// Power is signed (e.g. an interconnector reversing direction, or residual
    /// demand net of generation), so negatives are allowed; only NaN and infinity
    /// are rejected.
    /// </para>
    /// <para>
    /// Adding two powers is meaningful across space (summing a fleet at the same
    /// instant), but not across time: two consecutive interval averages do not add
    /// to the average over the combined interval. For that you sum energy, not
    /// power. The operator here does not distinguish the two; combining across time
    /// is guarded where a series and its resolution are known, not on the scalar.
    /// </para>
    /// </summary>
    public readonly record struct Power : IComparable<Power>
    {
        /// <summary>The value in megawatts. Signed.</summary>
        public double Megawatts { get; }

        private Power(double megawatts) => Megawatts = megawatts;

        /// <summary>Zero power. Seed for summing a collection of powers.</summary>
        public static Power Zero { get; } = new(0);

        /// <summary>
        /// Creates a <see cref="Power"/> from a value in megawatts. Naming the unit
        /// in the factory keeps the unit explicit at the call site.
        /// </summary>
        public static Power FromMegawatts(double megawatts)
        {
            if (double.IsNaN(megawatts) || double.IsInfinity(megawatts))
            {
                throw new ArgumentException(
                    "Power must be a finite number.",
                    nameof(megawatts));
            }

            return new Power(megawatts);
        }

        /// <summary>Sums two powers. Meaningful across space, not across time; see the type remarks.</summary>
        public static Power operator +(Power a, Power b)
            => FromMegawatts(a.Megawatts + b.Megawatts);

        /// <summary>Subtracts one power from another.</summary>
        public static Power operator -(Power a, Power b)
            => FromMegawatts(a.Megawatts - b.Megawatts);

        /// <summary>Scales a power by a dimensionless factor.</summary>
        public static Power operator *(Power power, double factor)
            => FromMegawatts(power.Megawatts * factor);

        /// <summary>Scales a power by a dimensionless factor.</summary>
        public static Power operator *(double factor, Power power)
            => FromMegawatts(power.Megawatts * factor);

        /// <summary>Energy from this power sustained over <paramref name="interval"/>.</summary>
        public static Energy operator *(Power power, TimeSpan interval)
            => Energy.From(power, interval);

        /// <summary>Dimensionless ratio between two power values.</summary>
        public static double operator /(Power numerator, Power denominator)
        {
            if (denominator.Megawatts == 0)
            {
                throw new DivideByZeroException("Cannot divide power by zero power.");
            }

            return numerator.Megawatts / denominator.Megawatts;
        }

        /// <summary>Whether the left power is less than the right.</summary>
        public static bool operator <(Power a, Power b) => a.Megawatts < b.Megawatts;

        /// <summary>Whether the left power is greater than the right.</summary>
        public static bool operator >(Power a, Power b) => a.Megawatts > b.Megawatts;

        /// <summary>Whether the left power is less than or equal to the right.</summary>
        public static bool operator <=(Power a, Power b) => a.Megawatts <= b.Megawatts;

        /// <summary>Whether the left power is greater than or equal to the right.</summary>
        public static bool operator >=(Power a, Power b) => a.Megawatts >= b.Megawatts;

        /// <summary>Orders this power against another by megawatts.</summary>
        public int CompareTo(Power other) => Megawatts.CompareTo(other.Megawatts);

        /// <summary>The lesser of two powers.</summary>
        public static Power Min(Power a, Power b) => a.Megawatts <= b.Megawatts ? a : b;

        /// <summary>The greater of two powers.</summary>
        public static Power Max(Power a, Power b) => a.Megawatts >= b.Megawatts ? a : b;
    }
}