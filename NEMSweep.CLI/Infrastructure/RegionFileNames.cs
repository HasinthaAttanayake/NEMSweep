using System.Text;
using NEMSweep.Contracts;

namespace NEMSweep.CLI.Infrastructure;

internal static class RegionFileNames
{
    private const string CustomRegionPrefix = "region-";
    private const int MaximumEncodedRegionBytes = 100;

    public static string For(string regionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);

        if (NemRegions.IsKnown(regionId))
        {
            return regionId.ToLowerInvariant();
        }

        string canonicalIdentity = regionId.ToUpperInvariant();
        byte[] identityBytes;
        try
        {
            identityBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetBytes(canonicalIdentity);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Region identifier contains invalid UTF-16 and cannot be represented in a portable file name.",
                nameof(regionId),
                exception);
        }

        if (identityBytes.Length > MaximumEncodedRegionBytes)
        {
            throw new ArgumentException(
                $"Region identifier is too long to create a portable file name (maximum "
                + $"{MaximumEncodedRegionBytes} UTF-8 bytes after case normalization).",
                nameof(regionId));
        }

        return CustomRegionPrefix + Convert.ToHexString(identityBytes);
    }
}
