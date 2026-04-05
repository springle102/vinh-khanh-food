using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService
{
    private const string CurrentCustomerIdPreferenceKey = "vkfood.current.customer.id";

    public async Task<UserProfileCard?> SelectUserProfileAsync(string identifier)
    {
        var normalizedIdentifier = identifier?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedIdentifier))
        {
            return await GetUserProfileAsync();
        }

        var customers = await GetCustomerUsersAsync();
        var matchedCustomer = FindCustomerByIdentifier(customers, normalizedIdentifier);
        if (matchedCustomer is null)
        {
            return null;
        }

        SaveCurrentCustomerId(matchedCustomer.Id);
        InvalidateBootstrapSnapshot();
        return MapUserProfile(matchedCustomer);
    }

    public async Task<UserProfileCard> UpdateUserProfileAsync(UserProfileUpdateRequest request)
    {
        var customer = await GetResolvedCurrentCustomerAsync()
            ?? throw new InvalidOperationException("Không xác định được khách hàng hiện tại để cập nhật hồ sơ.");

        var client = await GetClientAsync()
            ?? throw new InvalidOperationException("Mobile app chưa cấu hình API base URL.");

        using var response = await client.PutAsJsonAsync(
            $"api/v1/customer-users/{Uri.EscapeDataString(customer.Id)}/profile",
            request,
            JsonOptions);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CustomerUserDto>>(JsonOptions);

        if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
        {
            throw new InvalidOperationException(envelope?.Message ?? "Không thể cập nhật hồ sơ khách hàng.");
        }

        SaveCurrentCustomerId(envelope.Data.Id);
        InvalidateBootstrapSnapshot();
        return MapUserProfile(envelope.Data);
    }

    private async Task<CustomerUserDto?> GetResolvedCurrentCustomerAsync()
    {
        var currentCustomerId = ReadCurrentCustomerId();
        if (!string.IsNullOrWhiteSpace(currentCustomerId))
        {
            var currentCustomer = await GetCustomerByIdAsync(currentCustomerId);
            if (currentCustomer is not null)
            {
                return currentCustomer;
            }
        }

        var customers = await GetCustomerUsersAsync();
        var resolvedCustomer = ResolvePreferredCustomer(customers);
        if (resolvedCustomer is not null)
        {
            SaveCurrentCustomerId(resolvedCustomer.Id);
        }

        return resolvedCustomer;
    }

    private async Task<IReadOnlyList<CustomerUserDto>> GetCustomerUsersAsync()
    {
        var client = await GetClientAsync();
        if (client is null)
        {
            return [];
        }

        try
        {
            var envelope = await client.GetFromJsonAsync<ApiEnvelope<List<CustomerUserDto>>>(
                "api/v1/customer-users",
                JsonOptions);

            return envelope?.Success == true && envelope.Data is not null
                ? envelope.Data
                : [];
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load customer users from backend.");
            return [];
        }
    }

    private async Task<CustomerUserDto?> GetCustomerByIdAsync(string customerId)
    {
        var client = await GetClientAsync();
        if (client is null)
        {
            return null;
        }

        try
        {
            var envelope = await client.GetFromJsonAsync<ApiEnvelope<CustomerUserDto>>(
                $"api/v1/customer-users/{Uri.EscapeDataString(customerId)}",
                JsonOptions);

            return envelope?.Success == true ? envelope.Data : null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load customer profile {CustomerId} from backend.", customerId);
            return null;
        }
    }

    private static CustomerUserDto? ResolvePreferredCustomer(IReadOnlyList<CustomerUserDto> customerUsers)
    {
        return customerUsers
            .Where(item => item.IsActive && !item.IsBanned)
            .OrderByDescending(item => item.LastActiveAt ?? item.CreatedAt)
            .ThenByDescending(item => item.TotalScans)
            .FirstOrDefault()
            ?? customerUsers
                .Where(item => !item.IsBanned)
                .OrderByDescending(item => item.LastActiveAt ?? item.CreatedAt)
                .FirstOrDefault()
            ?? customerUsers.FirstOrDefault();
    }

    private static CustomerUserDto? FindCustomerByIdentifier(
        IReadOnlyList<CustomerUserDto> customerUsers,
        string identifier)
    {
        var normalizedIdentifier = identifier.Trim();
        var normalizedPhone = NormalizePhone(normalizedIdentifier);

        return customerUsers.FirstOrDefault(item =>
                   string.Equals(item.Email, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.Username, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.DeviceId, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.Id, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(item.Phone) &&
                    string.Equals(NormalizePhone(item.Phone), normalizedPhone, StringComparison.OrdinalIgnoreCase)))
               ?? customerUsers.FirstOrDefault(item =>
                   string.Equals(item.Name, normalizedIdentifier, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePhone(string? value)
        => string.Concat((value ?? string.Empty).Where(char.IsDigit));

    private static string ReadCurrentCustomerId()
        => Preferences.Default.Get(CurrentCustomerIdPreferenceKey, string.Empty);

    private static void SaveCurrentCustomerId(string customerId)
    {
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            Preferences.Default.Set(CurrentCustomerIdPreferenceKey, customerId);
        }
    }

    private void InvalidateBootstrapSnapshot()
    {
        _bootstrapSnapshot = null;
        _syncState = null;
        _lastSyncCheckAt = DateTimeOffset.MinValue;
        _detailCache.Clear();
    }

    private UserProfileCard MapUserProfile(CustomerUserDto customer)
    {
        return new UserProfileCard
        {
            FullName = FirstNonEmpty(customer.Name, customer.Username, customer.Email, customer.Id),
            Email = customer.Email,
            Phone = customer.Phone,
            AvatarInitials = BuildInitials(customer.Name, customer.Username, customer.Email),
            MetaLine = $"ID {customer.Id} • {customer.DeviceType} • {_languageService.CurrentLanguage}"
        };
    }
}
