using NEM.Model.Series;
using NEM.Model.Units;
using NEM.Model.Weather;

namespace NEM.Model.Generation.Wind
{
    /// <summary>
    /// Converts a measured wind-speed trace into power using a digitized Goldwind
    /// GW 140/3MW(S) 3.4 MW reference curve and piecewise-linear interpolation.
    /// The manufacturer curve is specified at an air density of 1.225 kg/m³; this
    /// model does not apply an air-density correction.
    /// <para>
    /// Applying this nonlinear curve to an interval-mean wind speed approximates,
    /// but does not exactly equal, mean power over that interval.
    /// </para>
    /// </summary>
    public static class WindPowerCurve
    {
        /// <summary>Nameplate capacity of the reference turbine the curve is digitised from.</summary>
        public static Power ReferenceTurbineCapacity { get; } = Power.FromMegawatts(3.4);

        /// <summary>
        /// Air density the manufacturer curve is specified at. Stated because no air-density
        /// correction is applied: a site materially denser or thinner than this will be modelled
        /// slightly wrong.
        /// </summary>
        public const double ReferenceAirDensityKilogramsPerCubicMetre = 1.225;

        /// <summary>Hub height assumed when settings do not override it.</summary>
        public const double DefaultHubHeightMetres = 120.0;

        /// <summary>
        /// Wind-shear exponent assumed when settings do not override it, used to extrapolate the
        /// measured wind speed from its measurement height up to hub height.
        /// </summary>
        public const double DefaultShearExponent = 0.2;

        /// <summary>Speed below which the turbine produces nothing.</summary>
        public const double CutInWindSpeedMetresPerSecond = 2.5;

        /// <summary>
        /// Speed from which the turbine produces its full rating, holding that rating up to the
        /// cut-out speed. Above cut-out the turbine shuts down and output falls to zero.
        /// </summary>
        public const double RatedWindSpeedMetresPerSecond = 11.0;

        /// <summary>Lowest cut-out speed the brochure permits; a lower setting is rejected.</summary>
        public const double MinimumCutOutWindSpeedMetresPerSecond = 20.0;

        /// <summary>Speed above which the turbine shuts down, when settings do not override it.</summary>
        public const double DefaultCutOutWindSpeedMetresPerSecond =
            MinimumCutOutWindSpeedMetresPerSecond;

        private static readonly (double WindSpeedMetresPerSecond, double CapacityFactor)[] ReferenceCurve =
        [
            (2.5, 0.0),
            (3.0, 0.014705882352941176),
            (4.0, 0.047058823529411764),
            (5.0, 0.09411764705882353),
            (6.0, 0.1676470588235294),
            (6.5, 0.21764705882352942),
            (7.0, 0.2823529411764706),
            (7.5, 0.3558823529411765),
            (8.0, 0.4411764705882353),
            (8.5, 0.5352941176470588),
            (9.0, 0.6411764705882353),
            (9.5, 0.75),
            (10.0, 0.8647058823529412),
            (10.5, 0.9588235294117647),
            (11.0, 1.0),
        ];

        /// <summary>
        /// Extrapolates a measured wind-speed trace to hub height using the power law
        /// <c>v_hub = v_measured * (h_hub / h_measured) ^ shear</c>.
        /// </summary>
        /// <param name="measuredWindSpeed">
        /// A wind-speed trace in metres per second that carries its measurement height.
        /// </param>
        /// <param name="settings">Hub height and shear exponent, or null for the defaults.</param>
        /// <returns>A wind-speed trace tagged with the hub height it was corrected to.</returns>
        /// <exception cref="ArgumentException">
        /// The trace is not in metres per second, or carries no measurement height.
        /// </exception>
        public static TraceSeries CorrectToHubHeight(
            TraceSeries measuredWindSpeed,
            WindPowerCurveSettings? settings = null)
        {
            settings ??= WindPowerCurveSettings.Default;
            ValidateWindTrace(measuredWindSpeed);
            ValidateModelParameters(settings.HubHeightMetres, settings.ShearExponent);

            double measurementHeightMetres = measuredWindSpeed.MeasurementHeightMetres!.Value;
            double heightFactor = Math.Pow(
                settings.HubHeightMetres / measurementHeightMetres,
                settings.ShearExponent);
            var correctedMetresPerSecond = new double[measuredWindSpeed.Length];
            for (int index = 0; index < correctedMetresPerSecond.Length; index++)
            {
                correctedMetresPerSecond[index] = measuredWindSpeed[index] * heightFactor;
            }

            return TraceSeries.WindSpeed(
                measuredWindSpeed.Start,
                measuredWindSpeed.Resolution,
                correctedMetresPerSecond,
                settings.HubHeightMetres);
        }

        /// <summary>
        /// Converts a measured wind-speed trace into a generation series for an installed fleet.
        /// Corrects to hub height, reads the reference curve by piecewise-linear interpolation, and
        /// scales the resulting capacity factor by installed capacity.
        /// </summary>
        /// <param name="measuredWindSpeed">A wind-speed trace with its measurement height.</param>
        /// <param name="installedCapacity">Fleet nameplate capacity in MW. Must not be negative.</param>
        /// <param name="settings">Curve settings, or null for the defaults.</param>
        /// <returns>Generation in MW over the trace's timeline.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Installed capacity is negative, or the cut-out speed is below
        /// <see cref="MinimumCutOutWindSpeedMetresPerSecond"/>.
        /// </exception>
        public static FlowSeries Calculate(
            TraceSeries measuredWindSpeed,
            Power installedCapacity,
            WindPowerCurveSettings? settings = null)
        {
            settings ??= WindPowerCurveSettings.Default;

            if (installedCapacity.Megawatts < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(installedCapacity),
                    installedCapacity.Megawatts,
                    "Installed wind capacity cannot be negative.");
            }

            if (!double.IsFinite(settings.CutOutWindSpeedMetresPerSecond)
                || settings.CutOutWindSpeedMetresPerSecond < MinimumCutOutWindSpeedMetresPerSecond)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings.CutOutWindSpeedMetresPerSecond),
                    settings.CutOutWindSpeedMetresPerSecond,
                    $"Cut-out wind speed must be at least {MinimumCutOutWindSpeedMetresPerSecond} m/s, as specified by the brochure.");
            }

            TraceSeries hubHeightWindSpeed = CorrectToHubHeight(
                measuredWindSpeed,
                settings);
            var megawatts = new double[hubHeightWindSpeed.Length];
            for (int index = 0; index < megawatts.Length; index++)
            {
                double capacityFactor = InterpolateCapacityFactor(
                    hubHeightWindSpeed[index],
                    settings.CutOutWindSpeedMetresPerSecond);
                Power output = installedCapacity * capacityFactor;
                megawatts[index] = output.Megawatts;
            }

            return new FlowSeries(
                hubHeightWindSpeed.Start,
                hubHeightWindSpeed.Resolution,
                megawatts);
        }

        private static double InterpolateCapacityFactor(
            double windSpeedMetresPerSecond,
            double cutOutWindSpeedMetresPerSecond)
        {
            if (windSpeedMetresPerSecond < CutInWindSpeedMetresPerSecond
                || windSpeedMetresPerSecond > cutOutWindSpeedMetresPerSecond)
            {
                return 0.0;
            }

            if (windSpeedMetresPerSecond >= RatedWindSpeedMetresPerSecond)
            {
                return 1.0;
            }

            for (int upperIndex = 1; upperIndex < ReferenceCurve.Length; upperIndex++)
            {
                (double upperWindSpeed, double upperCapacityFactor) = ReferenceCurve[upperIndex];
                if (windSpeedMetresPerSecond <= upperWindSpeed)
                {
                    (double lowerWindSpeed, double lowerCapacityFactor) = ReferenceCurve[upperIndex - 1];
                    double fraction = (windSpeedMetresPerSecond - lowerWindSpeed)
                        / (upperWindSpeed - lowerWindSpeed);
                    return lowerCapacityFactor
                        + fraction * (upperCapacityFactor - lowerCapacityFactor);
                }
            }

            throw new InvalidOperationException("The reference wind-power curve does not cover the requested speed.");
        }

        private static void ValidateWindTrace(TraceSeries windSpeed)
        {
            ArgumentNullException.ThrowIfNull(windSpeed);

            if (windSpeed.Unit != TraceUnit.MetresPerSecond
                || windSpeed.MeasurementHeightMetres is null)
            {
                throw new ArgumentException(
                    "Expected a wind-speed trace in metres per second with a measurement height.",
                    nameof(windSpeed));
            }
        }

        private static void ValidateModelParameters(double hubHeightMetres, double shearExponent)
        {
            if (!double.IsFinite(hubHeightMetres) || hubHeightMetres <= 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(hubHeightMetres), hubHeightMetres, "Hub height must be positive and finite.");
            }

            if (!double.IsFinite(shearExponent) || shearExponent < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(shearExponent), shearExponent, "Wind-shear exponent cannot be negative or non-finite.");
            }
        }
    }
}