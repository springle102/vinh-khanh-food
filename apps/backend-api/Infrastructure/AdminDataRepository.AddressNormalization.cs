using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private const double DefaultPoiTriggerRadius = 20d;
    private const int DefaultPoiPriority = 100;
    private const int ImportantPoiPriority = 200;

    private PoiUpsertRequest NormalizePoiRequestForPersistence(PoiUpsertRequest request, bool isFeatured)
    {
        var normalized = PoiAddressNormalizer.NormalizeStoredPoiAddress(
            request.Address,
            request.District,
            request.Ward,
            request.Lat,
            request.Lng);
        var normalizedTriggerRadius = NormalizePoiTriggerRadius(request.TriggerRadius);
        var normalizedPriority = NormalizePoiPriority(request.Priority, isFeatured);

        if (!HasPoiAddressChanges(request.Address, request.District, request.Ward, normalized) &&
            Math.Abs(normalizedTriggerRadius - request.TriggerRadius) < 0.001d &&
            normalizedPriority == request.Priority)
        {
            return request;
        }

        _logger.LogInformation(
            "Normalized POI payload before save. slug={Slug}, rawAddress={RawAddress}, rawDistrict={RawDistrict}, rawWard={RawWard}, normalizedAddress={NormalizedAddress}, normalizedDistrict={NormalizedDistrict}, normalizedWard={NormalizedWard}, triggerRadius={TriggerRadius}, priority={Priority}, override={OverrideReason}",
            request.Slug,
            request.Address,
            request.District,
            request.Ward,
            normalized.Address,
            normalized.District,
            normalized.Ward,
            normalizedTriggerRadius,
            normalizedPriority,
            normalized.OverrideReason);

        return request with
        {
            Address = normalized.Address,
            District = normalized.District,
            Ward = normalized.Ward,
            TriggerRadius = normalizedTriggerRadius,
            Priority = normalizedPriority
        };
    }

    private Poi NormalizePoiForResponse(Poi poi)
    {
        var normalized = PoiAddressNormalizer.NormalizeStoredPoiAddress(
            poi.Address,
            poi.District,
            poi.Ward,
            poi.Lat,
            poi.Lng);
        var normalizedTriggerRadius = NormalizePoiTriggerRadius(poi.TriggerRadius);
        var normalizedPriority = NormalizePoiPriority(poi.Priority, poi.Featured);

        if (!HasPoiAddressChanges(poi.Address, poi.District, poi.Ward, normalized) &&
            Math.Abs(normalizedTriggerRadius - poi.TriggerRadius) < 0.001d &&
            normalizedPriority == poi.Priority)
        {
            return poi;
        }

        _logger.LogInformation(
            "Normalized POI while reading from DB. poiId={PoiId}, rawAddress={RawAddress}, rawDistrict={RawDistrict}, rawWard={RawWard}, normalizedAddress={NormalizedAddress}, normalizedDistrict={NormalizedDistrict}, normalizedWard={NormalizedWard}, triggerRadius={TriggerRadius}, priority={Priority}, override={OverrideReason}",
            poi.Id,
            poi.Address,
            poi.District,
            poi.Ward,
            normalized.Address,
            normalized.District,
            normalized.Ward,
            normalizedTriggerRadius,
            normalizedPriority,
            normalized.OverrideReason);

        poi.Address = normalized.Address;
        poi.District = normalized.District;
        poi.Ward = normalized.Ward;
        poi.TriggerRadius = normalizedTriggerRadius;
        poi.Priority = normalizedPriority;
        return poi;
    }

    private static double NormalizePoiTriggerRadius(double triggerRadius)
        => double.IsFinite(triggerRadius) && triggerRadius >= DefaultPoiTriggerRadius
            ? triggerRadius
            : DefaultPoiTriggerRadius;

    private static int NormalizePoiPriority(int priority, bool isFeatured)
        => priority > 0
            ? priority
            : isFeatured
                ? ImportantPoiPriority
                : DefaultPoiPriority;

    private void NormalizePersistedPoiAddresses(SqlConnection connection)
    {
        const string selectSql = """
            SELECT Id, AddressLine, District, Ward, Latitude, Longitude
            FROM dbo.Pois
            ORDER BY Id;
            """;

        var updates = new List<(string Id, string Address, string District, string Ward, string? OverrideReason)>();

        using (var command = CreateCommand(connection, null, selectSql))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var currentAddress = ReadString(reader, "AddressLine");
                var currentDistrict = ReadString(reader, "District");
                var currentWard = ReadString(reader, "Ward");
                var normalized = PoiAddressNormalizer.NormalizeStoredPoiAddress(
                    currentAddress,
                    currentDistrict,
                    currentWard,
                    ReadDouble(reader, "Latitude"),
                    ReadDouble(reader, "Longitude"));

                if (!HasPoiAddressChanges(currentAddress, currentDistrict, currentWard, normalized))
                {
                    continue;
                }

                updates.Add((
                    ReadString(reader, "Id"),
                    normalized.Address,
                    normalized.District,
                    normalized.Ward,
                    normalized.OverrideReason));
            }
        }

        if (updates.Count == 0)
        {
            return;
        }

        foreach (var update in updates)
        {
            ExecuteNonQuery(
                connection,
                null,
                """
                UPDATE dbo.Pois
                SET AddressLine = ?,
                    District = ?,
                    Ward = ?
                WHERE Id = ?;
                """,
                update.Address,
                update.District,
                update.Ward,
                update.Id);
        }

        _logger.LogInformation(
            "Repaired persisted POI administrative addresses. updatedCount={UpdatedCount}, poiIds={PoiIds}",
            updates.Count,
            string.Join(", ", updates.Select(update => update.Id)));
    }

    private static bool HasPoiAddressChanges(
        string? currentAddress,
        string? currentDistrict,
        string? currentWard,
        NormalizedPoiAddress normalized)
    {
        return !string.Equals(currentAddress?.Trim(), normalized.Address, StringComparison.Ordinal) ||
               !string.Equals(currentDistrict?.Trim(), normalized.District, StringComparison.Ordinal) ||
               !string.Equals(currentWard?.Trim(), normalized.Ward, StringComparison.Ordinal);
    }
}
