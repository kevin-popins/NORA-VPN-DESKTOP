using System.Text;
using System.Text.RegularExpressions;

namespace Nvp;

// Presentation metadata only: never use this value to identify or route a profile.
internal static class NoraDisplayRegion
{
    internal const string AntarcticaCode = "AQ";
    internal const string AntarcticaCountry = "Antarctica";
    internal const string AntarcticaLabel = "Антарктида";

    internal static bool HasAntarcticaName(string? name)
        => Regex.IsMatch((name ?? "").Normalize(NormalizationForm.FormC),
            @"(?<![\p{L}\p{N}])(?:Антарктида|Antarctica)(?!\p{L})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool IsAntarcticaCountry(string? country)
        => new[] { AntarcticaCode, AntarcticaCountry, AntarcticaLabel }
            .Contains(country?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);

    internal static bool IsAntarctica(string? name, string? country)
        => HasAntarcticaName(name) || IsAntarcticaCountry(country);

    internal static string Country(string? name, string? country)
        => IsAntarctica(name, country) ? AntarcticaLabel : country ?? "";

    internal static string Name(string name, string? country)
    {
        if (!IsAntarctica(name, country))
            return name;
        // This provider's compatibility flag must not leak into NORA's display.
        // The adjacent flag control renders the canonical bundled AQ image.
        return Regex.Replace(name.Replace("🇸🇴", " ", StringComparison.Ordinal), @"\s+", " ").Trim();
    }
}
