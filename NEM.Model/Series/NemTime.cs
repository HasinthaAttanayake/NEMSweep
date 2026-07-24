namespace NEM.Model.Series
{
    /// <summary>
    /// NEM market time: fixed AEST (UTC+10), no daylight saving, all regions and all
    /// year. Never infer an offset from the machine locale.
    /// </summary>
    internal static class NemTime
    {
        public static readonly TimeSpan Offset = TimeSpan.FromHours(10);

        public static void Require(DateTimeOffset instant, string paramName)
        {
            if (instant.Offset != Offset)
            {
                throw new ArgumentException(
                    $"Timestamps must be in NEM market time (UTC+10); got offset {instant.Offset}. " +
                    "Do not infer the offset from the machine locale.",
                    paramName);
            }
        }
    }
}