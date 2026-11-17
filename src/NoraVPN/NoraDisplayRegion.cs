using System.Text;
using System.Text.RegularExpressions;

namespace Nvp;

// Presentation metadata only: never use this value to identify or route a profile.
internal static class NoraDisplayRegion
{
    internal const string AntarcticaCode = "AQ";
    internal const string AntarcticaCountry = "Antarctica";
    internal const string AntarcticaLabel = "Антарктида";
    internal const string BermudaCode = "BM";
    internal const string BermudaLabel = "Бермуды";
    internal const string VaticanCode = "VA";
    internal const string VaticanLabel = "Ватикан";

    internal static bool HasAntarcticaName(string? name)
        => Regex.IsMatch((name ?? "").Normalize(NormalizationForm.FormC),
            @"(?<![\p{L}\p{N}])(?:Антарктида|Antarctica)(?!\p{L})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool IsAntarcticaCountry(string? country)
        => new[] { AntarcticaCode, AntarcticaCountry, AntarcticaLabel }
            .Contains(country?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);

    internal static bool IsAntarctica(string? name, string? country)
        => HasAntarcticaName(name) || IsAntarcticaCountry(country);

    internal static bool HasBermudaName(string? name)
        => HasName(name, @"Bermudas?|Бермуды|Бермудские\s+острова|Бермудский\s+треугольник");

    internal static bool IsBermudaCountry(string? country)
        => new[] { BermudaCode, "BMU", "Bermuda", "Bermudas", BermudaLabel, "Бермудские острова" }
            .Contains(country?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);

    internal static bool IsBermuda(string? name, string? country)
        => HasBermudaName(name) || IsBermudaCountry(country);

    internal static bool HasVaticanName(string? name)
        => HasName(name, @"Vatican(?:\s+City)?|Holy\s+See|Ватикан|Святой\s+престол");

    internal static bool IsVaticanCountry(string? country)
        => new[] { VaticanCode, "VAT", "Vatican", "Vatican City", "Holy See", VaticanLabel, "Святой престол" }
            .Contains(country?.Trim() ?? "", StringComparer.OrdinalIgnoreCase);

    internal static bool IsVatican(string? name, string? country)
        => HasVaticanName(name) || IsVaticanCountry(country);

    private static bool HasName(string? name, string aliases)
        => Regex.IsMatch((name ?? "").Normalize(NormalizationForm.FormC),
            @"(?<![\p{L}\p{N}])(?:" + aliases + @")(?:\d+)?(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string Country(string? name, string? country)
    {
        if (IsAntarctica(name, country)) return AntarcticaLabel;
        if (IsBermuda(name, country)) return BermudaLabel;
        if (IsVatican(name, country)) return VaticanLabel;
        return country ?? "";
    }

    internal static string Name(string name, string? country)
    {
        if (!IsAntarctica(name, country))
            return name;
        // This provider's compatibility flag must not leak into NORA's display.
        // The adjacent flag control renders the canonical bundled AQ image.
        return Regex.Replace(name.Replace("🇸🇴", " ", StringComparison.Ordinal), @"\s+", " ").Trim();
    }
}
