using System.Text.Json;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class GeocodingProxyService(
    HttpClient httpClient,
    ILogger<GeocodingProxyService> logger)
{
    public async Task<GeocodingLocationResponse?> ReverseAsync(double lat, double lng, CancellationToken cancellationToken)
    {
        var requestUri =
            $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&addressdetails=1&accept-language=vi&lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        return await SendAsync(
            requestUri,
            "reverse",
            lat,
            lng,
            null,
            cancellationToken);
    }

    public async Task<GeocodingLocationResponse?> ForwardAsync(string query, CancellationToken cancellationToken)
    {
        var requestUri =
            $"https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&accept-language=vi&limit=1&q={Uri.EscapeDataString(query)}";

        return await SendAsync(
            requestUri,
            "forward",
            null,
            null,
            query,
            cancellationToken);
    }

    private async Task<GeocodingLocationResponse?> SendAsync(
        string requestUri,
        string operation,
        double? lat,
        double? lng,
        string? query,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rawPayload = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogDebug(
            "Geocoding raw response. operation={Operation}, lat={Lat}, lng={Lng}, query={Query}, rawPayload={RawPayload}",
            operation,
            lat,
            lng,
            query,
            rawPayload);

        using var document = JsonDocument.Parse(rawPayload);
        var root = document.RootElement;

        if (string.Equals(operation, "forward", StringComparison.OrdinalIgnoreCase))
        {
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            var first = root[0];
            if (!double.TryParse(ReadString(first, "lat"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedLat) ||
                !double.TryParse(ReadString(first, "lon"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedLng))
            {
                return null;
            }

            return BuildLocation(first, parsedLat, parsedLng, operation, query);
        }

        if (root.ValueKind != JsonValueKind.Object || !lat.HasValue || !lng.HasValue)
        {
            return null;
        }

        return BuildLocation(root, lat.Value, lng.Value, operation, query);
    }

    private GeocodingLocationResponse BuildLocation(
        JsonElement root,
        double lat,
        double lng,
        string operation,
        string? query)
    {
        var address = root.TryGetProperty("address", out var addressElement) && addressElement.ValueKind == JsonValueKind.Object
            ? addressElement
            : default;

        var normalized = PoiAddressNormalizer.NormalizeGeocodingAddress(new PoiAddressParts(
            DisplayName: ReadString(root, "display_name"),
            Venue: ReadString(root, "name"),
            HouseNumber: ReadString(address, "house_number"),
            Road: ReadString(address, "road"),
            Neighbourhood: ReadString(address, "neighbourhood"),
            Suburb: ReadString(address, "suburb"),
            Quarter: ReadString(address, "quarter"),
            CityDistrict: ReadString(address, "city_district"),
            StateDistrict: ReadString(address, "state_district"),
            County: ReadString(address, "county"),
            City: ReadString(address, "city"),
            State: ReadString(address, "state"),
            Country: ReadString(address, "country"),
            CountryCode: ReadString(address, "country_code"),
            Iso3166Level4: ReadString(address, "ISO3166-2-lvl4"),
            Lat: lat,
            Lng: lng));

        if (normalized.HasAdministrativeOverride)
        {
            logger.LogWarning(
                "Geocoding administrative override applied. operation={Operation}, lat={Lat}, lng={Lng}, query={Query}, rawDistrict={RawDistrict}, rawWard={RawWard}, normalizedDistrict={NormalizedDistrict}, normalizedWard={NormalizedWard}, normalizedCity={NormalizedCity}, override={OverrideReason}",
                operation,
                lat,
                lng,
                query,
                normalized.SourceDistrict,
                normalized.SourceWard,
                normalized.District,
                normalized.Ward,
                normalized.City,
                normalized.OverrideReason);
        }

        logger.LogInformation(
            "Geocoding normalized response. operation={Operation}, lat={Lat}, lng={Lng}, query={Query}, address={Address}, district={District}, ward={Ward}, city={City}",
            operation,
            lat,
            lng,
            query,
            normalized.Address,
            normalized.District,
            normalized.Ward,
            normalized.City);

        return new GeocodingLocationResponse(
            normalized.Address,
            normalized.District,
            normalized.Ward,
            lat,
            lng);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }
}
