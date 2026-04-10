using System.Globalization;
using System.Text;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed record PoiAddressParts(
    string? DisplayName = null,
    string? Venue = null,
    string? HouseNumber = null,
    string? Road = null,
    string? Neighbourhood = null,
    string? Suburb = null,
    string? Quarter = null,
    string? CityDistrict = null,
    string? StateDistrict = null,
    string? County = null,
    string? City = null,
    string? State = null,
    string? Country = null,
    string? CountryCode = null,
    string? Iso3166Level4 = null,
    double? Lat = null,
    double? Lng = null);

public sealed record NormalizedPoiAddress(
    string Address,
    string District,
    string Ward,
    string City,
    string Street,
    string SourceDistrict,
    string SourceWard,
    bool HasAdministrativeOverride,
    string? OverrideReason);

public static class PoiAddressNormalizer
{
    private const string HoChiMinhCityDisplayName = "TP.HCM";
    private const string VietnamDisplayName = "Việt Nam";
    private const string DistrictFourDisplayName = "Quận 4";

    private static readonly HashSet<string> HoChiMinhCityAliases =
    [
        "tp hcm",
        "tphcm",
        "thanh pho ho chi minh",
        "ho chi minh city",
        "hcmc",
        "sai gon",
        "saigon"
    ];

    private static readonly HashSet<string> VietnamAliases =
    [
        "viet nam",
        "vietnam"
    ];

    private static readonly HashSet<string> DistrictFourAliases =
    [
        "quan 4",
        "district 4"
    ];

    private static readonly Dictionary<string, string> HoChiMinhWardDistrictOverrides = new()
    {
        [NormalizeLookupKey("Phường Khánh Hội")] = DistrictFourDisplayName,
        [NormalizeLookupKey("Khánh Hội")] = DistrictFourDisplayName,
        [NormalizeLookupKey("Khanh Hoi Ward")] = DistrictFourDisplayName,
        [NormalizeLookupKey("Phường Vĩnh Hội")] = DistrictFourDisplayName,
        [NormalizeLookupKey("Vĩnh Hội")] = DistrictFourDisplayName,
        [NormalizeLookupKey("Vinh Hoi Ward")] = DistrictFourDisplayName,
    };

    public static NormalizedPoiAddress NormalizeGeocodingAddress(PoiAddressParts parts)
    {
        var sourceWard = FirstNonEmpty(parts.Suburb, parts.Neighbourhood, parts.Quarter);
        var ward = NormalizeText(sourceWard);
        var sourceDistrict = NormalizeText(FirstNonEmpty(parts.CityDistrict, parts.StateDistrict, parts.County, parts.City));
        var city = ResolveCity(parts.DisplayName, sourceDistrict, parts.City, parts.State, parts.CountryCode, parts.Iso3166Level4, ward);
        var (district, overrideReason) = ResolveDistrict(
            city,
            sourceDistrict,
            ward,
            parts.Road,
            parts.DisplayName,
            parts.Lat,
            parts.Lng);
        var country = ResolveCountry(parts.Country, parts.CountryCode);
        var street = JoinNonEmpty(parts.HouseNumber, parts.Road);
        var baseSegments = ResolveBaseSegments(
            parts.DisplayName,
            parts.Venue,
            street,
            ward,
            sourceDistrict,
            district,
            city,
            country);
        var address = BuildAddress(baseSegments, ward, district, city, country);

        return new NormalizedPoiAddress(
            address,
            district,
            ward,
            city,
            street,
            sourceDistrict,
            NormalizeText(sourceWard),
            overrideReason is not null,
            overrideReason);
    }

    public static NormalizedPoiAddress NormalizeStoredPoiAddress(
        string? address,
        string? district,
        string? ward,
        double? lat = null,
        double? lng = null)
    {
        var normalizedWard = NormalizeText(ward);
        var sourceDistrict = NormalizeText(district);
        var city = ResolveCity(
            address,
            sourceDistrict,
            null,
            null,
            null,
            null,
            normalizedWard);
        var (normalizedDistrict, overrideReason) = ResolveDistrict(
            city,
            sourceDistrict,
            normalizedWard,
            null,
            address,
            lat,
            lng);
        var country = ResolveCountryFromAddress(address);
        var baseSegments = ResolveBaseSegments(
            address,
            null,
            null,
            normalizedWard,
            sourceDistrict,
            normalizedDistrict,
            city,
            country);
        var normalizedAddress = BuildAddress(baseSegments, normalizedWard, normalizedDistrict, city, country);

        return new NormalizedPoiAddress(
            normalizedAddress,
            normalizedDistrict,
            normalizedWard,
            city,
            string.Empty,
            sourceDistrict,
            normalizedWard,
            overrideReason is not null,
            overrideReason);
    }

    private static string ResolveCity(
        string? addressLike,
        string? districtLike,
        string? cityLike,
        string? stateLike,
        string? countryCode,
        string? iso3166Level4,
        string? wardLike)
    {
        if (IsHoChiMinhCityContext(addressLike, districtLike, cityLike, stateLike, countryCode, iso3166Level4, wardLike))
        {
            return HoChiMinhCityDisplayName;
        }

        return NormalizeText(stateLike);
    }

    private static bool IsHoChiMinhCityContext(
        string? addressLike,
        string? districtLike,
        string? cityLike,
        string? stateLike,
        string? countryCode,
        string? iso3166Level4,
        string? wardLike)
    {
        if (string.Equals(NormalizeText(iso3166Level4), "VN-SG", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(NormalizeText(countryCode), "vn", StringComparison.OrdinalIgnoreCase))
        {
            if (ContainsHoChiMinhAlias(addressLike) ||
                ContainsHoChiMinhAlias(cityLike) ||
                ContainsHoChiMinhAlias(stateLike) ||
                ContainsDistrictFourAlias(districtLike) ||
                MatchesWardDistrictOverride(wardLike))
            {
                return true;
            }
        }

        return ContainsHoChiMinhAlias(addressLike) ||
               ContainsHoChiMinhAlias(cityLike) ||
               ContainsHoChiMinhAlias(stateLike) ||
               ContainsDistrictFourAlias(districtLike) ||
               MatchesWardDistrictOverride(wardLike);
    }

    private static (string District, string? OverrideReason) ResolveDistrict(
        string city,
        string sourceDistrict,
        string ward,
        string? road,
        string? addressLike,
        double? lat,
        double? lng)
    {
        if (MatchesWardDistrictOverride(ward) && IsHoChiMinhCityAlias(city))
        {
            return (DistrictFourDisplayName, BuildOverrideReason(sourceDistrict));
        }

        if (IsHoChiMinhCityAlias(city) &&
            IsThuDucCityAlias(sourceDistrict) &&
            (IsLikelyVinhKhanhStreet(road) || IsLikelyDistrictFourZone(lat, lng) || MatchesWardDistrictOverride(ward) || ContainsDistrictFourAlias(addressLike)))
        {
            return (DistrictFourDisplayName, "hcmc-vinh-khanh-normalized-from-thu-duc");
        }

        if (IsHoChiMinhCityAlias(city) && IsHoChiMinhCityAlias(sourceDistrict))
        {
            return (string.Empty, null);
        }

        return (sourceDistrict, null);
    }

    private static string BuildOverrideReason(string sourceDistrict)
    {
        if (IsThuDucCityAlias(sourceDistrict))
        {
            return "hcmc-khanh-hoi-was-mapped-to-thu-duc";
        }

        if (string.IsNullOrWhiteSpace(sourceDistrict))
        {
            return "hcmc-khanh-hoi-filled-missing-district";
        }

        return "hcmc-khanh-hoi-normalized-district";
    }

    private static List<string> ResolveBaseSegments(
        string? addressLike,
        string? venue,
        string? street,
        string ward,
        string sourceDistrict,
        string district,
        string city,
        string country)
    {
        var items = new List<string>();
        AddDistinct(items, venue);
        AddDistinct(items, street);

        foreach (var segment in SplitAddressSegments(addressLike))
        {
            if (ShouldIgnoreSegment(segment, ward, sourceDistrict, district, city, country))
            {
                continue;
            }

            AddDistinct(items, segment);
        }

        return items;
    }

    private static bool ShouldIgnoreSegment(
        string segment,
        string ward,
        string sourceDistrict,
        string district,
        string city,
        string country)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            return true;
        }

        if (IsPostalCode(segment))
        {
            return true;
        }

        return IsSameLookup(segment, ward) ||
               IsSameLookup(segment, sourceDistrict) ||
               IsSameLookup(segment, district) ||
               IsSameLookup(segment, city) ||
               (IsHoChiMinhCityAlias(city) && IsHoChiMinhCityAlias(segment)) ||
               IsCountryAlias(segment, country);
    }

    private static string BuildAddress(
        IReadOnlyList<string> baseSegments,
        string ward,
        string district,
        string city,
        string country)
    {
        var parts = new List<string>();

        foreach (var segment in baseSegments)
        {
            AddDistinct(parts, segment);
        }

        AddDistinct(parts, ward);
        AddDistinct(parts, district);
        AddDistinct(parts, city);
        AddDistinct(parts, country);

        return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static void AddDistinct(List<string> items, string? value)
    {
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (items.Any(item => IsSameLookup(item, normalized)))
        {
            return;
        }

        items.Add(normalized);
    }

    private static IEnumerable<string> SplitAddressSegments(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeText)
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

    private static bool MatchesWardDistrictOverride(string? ward)
        => HoChiMinhWardDistrictOverrides.ContainsKey(NormalizeLookupKey(ward));

    private static bool ContainsDistrictFourAlias(string? value)
    {
        var lookup = NormalizeLookupKey(value);
        return DistrictFourAliases.Contains(lookup) ||
               SplitAddressSegments(value).Any(segment => DistrictFourAliases.Contains(NormalizeLookupKey(segment)));
    }

    private static bool ContainsHoChiMinhAlias(string? value)
    {
        var lookup = NormalizeLookupKey(value);
        return HoChiMinhCityAliases.Contains(lookup) ||
               SplitAddressSegments(value).Any(segment => HoChiMinhCityAliases.Contains(NormalizeLookupKey(segment)));
    }

    private static bool IsHoChiMinhCityAlias(string? value)
        => HoChiMinhCityAliases.Contains(NormalizeLookupKey(value));

    private static bool IsThuDucCityAlias(string? value)
        => NormalizeLookupKey(value) is "thanh pho thu duc" or "thu duc city";

    private static bool IsLikelyVinhKhanhStreet(string? road)
        => NormalizeLookupKey(road).Contains("vinh khanh", StringComparison.Ordinal);

    private static bool IsLikelyDistrictFourZone(double? lat, double? lng)
    {
        if (!lat.HasValue || !lng.HasValue)
        {
            return false;
        }

        return lat.Value is >= 10.7580 and <= 10.7648 &&
               lng.Value is >= 106.6990 and <= 106.7068;
    }

    private static bool IsCountryAlias(string? value, string? country)
    {
        var lookup = NormalizeLookupKey(value);
        if (VietnamAliases.Contains(lookup))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(country) && IsSameLookup(value, country);
    }

    private static string ResolveCountry(string? country, string? countryCode)
    {
        if (string.Equals(NormalizeText(countryCode), "vn", StringComparison.OrdinalIgnoreCase) ||
            VietnamAliases.Contains(NormalizeLookupKey(country)))
        {
            return VietnamDisplayName;
        }

        return NormalizeText(country);
    }

    private static string ResolveCountryFromAddress(string? address)
        => VietnamAliases.Contains(NormalizeLookupKey(address)) || SplitAddressSegments(address).Any(segment => VietnamAliases.Contains(NormalizeLookupKey(segment)))
            ? VietnamDisplayName
            : string.Empty;

    private static bool IsPostalCode(string value)
        => value.Length is >= 5 and <= 6 && value.All(char.IsDigit);

    private static bool IsSameLookup(string? left, string? right)
        => string.Equals(
            NormalizeLookupKey(left),
            NormalizeLookupKey(right),
            StringComparison.Ordinal);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string JoinNonEmpty(params string?[] values)
        => string.Join(" ", values.Select(NormalizeText).Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string NormalizeText(string? value)
        => string.Join(" ", (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeLookupKey(string? value)
    {
        var normalized = RemoveDiacritics(NormalizeText(value)).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSeparator = false;
                continue;
            }

            if (previousWasSeparator)
            {
                continue;
            }

            builder.Append(' ');
            previousWasSeparator = true;
        }

        return builder.ToString().Trim();
    }

    private static string RemoveDiacritics(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
