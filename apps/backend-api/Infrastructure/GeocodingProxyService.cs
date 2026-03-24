using System.Text.Json;
using VinhKhanh.BackendApi.Contracts;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class GeocodingProxyService(HttpClient httpClient)
{
    public async Task<GeocodingLocationResponse?> ReverseAsync(double lat, double lng, CancellationToken cancellationToken)
    {
        var requestUri =
            $"https://nominatim.openstreetmap.org/reverse?format=json&addressdetails=1&accept-language=vi&lat={lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={lng.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return BuildLocation(document.RootElement, lat, lng);
    }

    public async Task<GeocodingLocationResponse?> ForwardAsync(string query, CancellationToken cancellationToken)
    {
        var requestUri =
            $"https://nominatim.openstreetmap.org/search?format=json&addressdetails=1&accept-language=vi&limit=1&q={Uri.EscapeDataString(query)}";

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
        {
            return null;
        }

        var first = document.RootElement[0];
        if (!double.TryParse(ReadString(first, "lat"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(ReadString(first, "lon"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lng))
        {
            return null;
        }

        return BuildLocation(first, lat, lng);
    }

    private static GeocodingLocationResponse BuildLocation(JsonElement root, double lat, double lng)
    {
        var address = root.TryGetProperty("address", out var addressElement) && addressElement.ValueKind == JsonValueKind.Object
            ? addressElement
            : default;

        var displayName = ReadString(root, "display_name");
        var name = ReadString(root, "name");
        var roadLine = string.Join(" ", new[]
        {
            ReadString(address, "house_number"),
            ReadString(address, "road")
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        var parts = new[]
        {
            name,
            roadLine,
            ReadString(address, "neighbourhood"),
            ReadString(address, "suburb"),
            ReadString(address, "quarter"),
            ReadString(address, "city_district"),
            ReadString(address, "state_district"),
            ReadString(address, "city"),
            ReadString(address, "state"),
            ReadString(address, "country")
        }
        .Select(NormalizeText)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        var resolvedAddress = NormalizeText(parts.Length > 0 ? string.Join(", ", parts) : displayName);
        var district = NormalizeText(
            ReadString(address, "city_district") ??
            ReadString(address, "state_district") ??
            ReadString(address, "county") ??
            ReadString(address, "city"));
        var ward = NormalizeText(
            ReadString(address, "suburb") ??
            ReadString(address, "neighbourhood") ??
            ReadString(address, "quarter") ??
            ReadString(address, "city_block"));

        return new GeocodingLocationResponse(
            resolvedAddress,
            district,
            ward,
            lat,
            lng);
    }

    private static string NormalizeText(string? value)
        => string.Join(" ", (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }
}
