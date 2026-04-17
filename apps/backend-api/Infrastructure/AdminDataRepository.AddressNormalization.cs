using Microsoft.Data.SqlClient;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private PoiUpsertRequest NormalizePoiRequestForPersistence(PoiUpsertRequest request)
    {
        var normalizedTriggerRadius = request.TriggerRadius switch
        {
            <= 0 => 20,
            < 20 => 20,
            _ => request.TriggerRadius
        };
        var normalizedPriority = request.Priority < 0 ? 0 : request.Priority;
        var normalized = PoiAddressNormalizer.NormalizeStoredPoiAddress(
            request.Address,
            request.District,
            request.Ward,
            request.Lat,
            request.Lng);

        if (!HasPoiAddressChanges(request.Address, request.District, request.Ward, normalized) &&
            normalizedTriggerRadius == request.TriggerRadius &&
            normalizedPriority == request.Priority)
        {
            return request;
        }

        _logger.LogInformation(
            "Normalized POI address before save. slug={Slug}, rawAddress={RawAddress}, rawDistrict={RawDistrict}, rawWard={RawWard}, normalizedAddress={NormalizedAddress}, normalizedDistrict={NormalizedDistrict}, normalizedWard={NormalizedWard}, override={OverrideReason}",
            request.Slug,
            request.Address,
            request.District,
            request.Ward,
            normalized.Address,
            normalized.District,
            normalized.Ward,
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

        if (!HasPoiAddressChanges(poi.Address, poi.District, poi.Ward, normalized))
        {
            return poi;
        }

        _logger.LogInformation(
            "Normalized POI address while reading from DB. poiId={PoiId}, rawAddress={RawAddress}, rawDistrict={RawDistrict}, rawWard={RawWard}, normalizedAddress={NormalizedAddress}, normalizedDistrict={NormalizedDistrict}, normalizedWard={NormalizedWard}, override={OverrideReason}",
            poi.Id,
            poi.Address,
            poi.District,
            poi.Ward,
            normalized.Address,
            normalized.District,
            normalized.Ward,
            normalized.OverrideReason);

        poi.Address = normalized.Address;
        poi.District = normalized.District;
        poi.Ward = normalized.Ward;
        return poi;
    }

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
