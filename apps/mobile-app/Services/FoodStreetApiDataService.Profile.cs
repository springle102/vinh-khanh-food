using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;
using VinhKhanh.MobileApp.Helpers;
using VinhKhanh.MobileApp.Models;

namespace VinhKhanh.MobileApp.Services;

public sealed partial class FoodStreetApiDataService
{
    public async Task<UserProfileCard?> LoginCustomerAsync(string identifier, string password)
    {
        var normalizedIdentifier = identifier?.Trim() ?? string.Empty;
        var normalizedPassword = password?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedIdentifier) || string.IsNullOrWhiteSpace(normalizedPassword))
        {
            return null;
        }

        var client = await GetClientAsync();
        if (client is null)
        {
            throw new MobileBackendConnectionException("The mobile app has no API base URL configured.");
        }

        try
        {
            using var response = await client.PostAsJsonAsync(
                "api/v1/customer-users/login",
                new
                {
                    Identifier = normalizedIdentifier,
                    Password = normalizedPassword
                },
                JsonOptions);
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CustomerUserDto>>(JsonOptions);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
            {
                throw new InvalidOperationException(envelope?.Message ?? "Unable to sign in right now.");
            }

            SaveCurrentCustomerId(envelope.Data.Id);
            InvalidateBootstrapSnapshot();
            await EnsureAllowedLanguageSelectionAsync();
            return MapUserProfile(envelope.Data);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "Unable to log in customer {Identifier}.", normalizedIdentifier);
            throw new MobileBackendConnectionException("Unable to connect to the backend for sign-in.", exception);
        }
        catch (TaskCanceledException exception)
        {
            _logger.LogWarning(exception, "Customer login timed out for {Identifier}.", normalizedIdentifier);
            throw new MobileBackendConnectionException("Unable to connect to the backend for sign-in.", exception);
        }
    }

    public async Task<UserProfileCard?> SelectUserProfileAsync(string identifier)
    {
        var normalizedIdentifier = identifier?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedIdentifier))
        {
            return await GetUserProfileAsync();
        }

        var customers = await GetCustomerUsersAsync(requireSuccess: true);
        var matchedCustomer = FindCustomerByIdentifier(customers, normalizedIdentifier);
        if (matchedCustomer is null)
        {
            return null;
        }

        SaveCurrentCustomerId(matchedCustomer.Id);
        InvalidateBootstrapSnapshot();
        await EnsureAllowedLanguageSelectionAsync();
        return MapUserProfile(matchedCustomer);
    }

    public async Task<UserProfileCard> RegisterUserProfileAsync(CustomerRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var client = await GetClientAsync()
            ?? throw new InvalidOperationException("The mobile app has no API base URL configured.");

        var payload = new CustomerRegistrationRequest
        {
            Name = request.Name?.Trim() ?? string.Empty,
            Username = request.Username?.Trim() ?? string.Empty,
            Email = request.Email?.Trim() ?? string.Empty,
            Phone = request.Phone?.Trim() ?? string.Empty,
            Password = request.Password ?? string.Empty,
            PreferredLanguage = AppLanguage.NormalizeCode(
                string.IsNullOrWhiteSpace(request.PreferredLanguage)
                    ? _languageService.CurrentLanguage
                    : request.PreferredLanguage),
            Country = string.IsNullOrWhiteSpace(request.Country)
                ? "VN"
                : request.Country.Trim().ToUpperInvariant()
        };

        using var response = await client.PostAsJsonAsync(
            "api/v1/customer-users",
            payload,
            JsonOptions);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CustomerUserDto>>(JsonOptions);

        if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
        {
            throw new InvalidOperationException(envelope?.Message ?? "Unable to create a new account right now.");
        }

        SaveCurrentCustomerId(envelope.Data.Id);
        InvalidateBootstrapSnapshot();
        await EnsureAllowedLanguageSelectionAsync();
        return MapUserProfile(envelope.Data);
    }

    public async Task<UserProfileCard> UpdateUserProfileAsync(UserProfileUpdateRequest request)
    {
        var customer = await GetResolvedCurrentCustomerAsync()
            ?? throw new InvalidOperationException("Unable to resolve the current customer profile.");

        var client = await GetClientAsync()
            ?? throw new InvalidOperationException("The mobile app has no API base URL configured.");

        using var response = await client.PutAsJsonAsync(
            $"api/v1/customer-users/{Uri.EscapeDataString(customer.Id)}/profile",
            request,
            JsonOptions);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CustomerUserDto>>(JsonOptions);

        if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
        {
            throw new InvalidOperationException(envelope?.Message ?? "Unable to update the customer profile.");
        }

        SaveCurrentCustomerId(envelope.Data.Id);
        InvalidateBootstrapSnapshot();
        return MapUserProfile(envelope.Data);
    }

    public async Task<PremiumPurchaseOffer> GetPremiumOfferAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        if (snapshot?.PremiumOffer is not null)
        {
            return snapshot.PremiumOffer;
        }

        _logger.LogWarning(
            "Premium offer was not available from bootstrap data. Falling back to default price {FallbackPriceUsd} USD.",
            DefaultPremiumPriceUsd);

        return new PremiumPurchaseOffer
        {
            PriceUsd = DefaultPremiumPriceUsd,
            FreeLanguageCodes = ["vi", "en"],
            PremiumLanguageCodes = ["zh-CN", "ko", "ja"]
        };
    }

    public async Task<PremiumPurchaseResult> PurchasePremiumAsync(PremiumCheckoutRequest request)
    {
        var customer = await GetResolvedCurrentCustomerAsync()
            ?? throw new InvalidOperationException("Please sign in before purchasing Premium.");
        var client = await GetClientAsync()
            ?? throw new InvalidOperationException("The mobile app has no API base URL configured.");
        var offer = await GetPremiumOfferAsync();

        ArgumentNullException.ThrowIfNull(request);
        request.ExpectedPriceUsd = offer.PriceUsd;
        request.ClientRequestId = string.IsNullOrWhiteSpace(request.ClientRequestId)
            ? Guid.NewGuid().ToString("N")
            : request.ClientRequestId.Trim();

        using var response = await client.PostAsJsonAsync(
            $"api/v1/customer-users/{Uri.EscapeDataString(customer.Id)}/premium/purchase",
            request,
            JsonOptions);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PremiumPurchaseResponseDto>>(JsonOptions);

        if (!response.IsSuccessStatusCode || envelope?.Success != true || envelope.Data is null)
        {
            throw new InvalidOperationException(envelope?.Message ?? "Unable to activate Premium right now.");
        }

        SaveCurrentCustomerId(envelope.Data.Customer.Id);
        InvalidateBootstrapSnapshot();
        await EnsureAllowedLanguageSelectionAsync();

        return new PremiumPurchaseResult
        {
            Profile = MapUserProfile(envelope.Data.Customer),
            ChargedAmountUsd = envelope.Data.ChargedAmountUsd,
            CurrencyCode = envelope.Data.CurrencyCode,
            PaymentProvider = envelope.Data.PaymentProvider,
            PaymentMethod = envelope.Data.PaymentMethod,
            TransactionId = envelope.Data.TransactionId
        };
    }

    public async Task<string> EnsureAllowedLanguageSelectionAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        var languages = snapshot?.SupportedLanguages.Count > 0
            ? snapshot.SupportedLanguages
            : BuildSupportedLanguages(null);
        var currentLanguageCode = AppLanguage.NormalizeCode(_languageService.CurrentLanguage);
        var currentLanguage = languages.FirstOrDefault(item =>
            string.Equals(item.Code, currentLanguageCode, StringComparison.OrdinalIgnoreCase));

        // ✅ FIX: If current language is available and not locked, return it without resetting
        if (currentLanguage is not null && !currentLanguage.IsLocked)
        {
            _logger?.LogInformation("Current language '{CurrentLanguage}' is valid and allowed.", currentLanguageCode);
            return currentLanguage.Code;
        }

        // ✅ FIX: Find a fallback language without automatically resetting
        var allowedLanguage = languages.FirstOrDefault(item => !item.IsLocked)
            ?? languages.FirstOrDefault()
            ?? new LanguageOption
            {
                Code = AppLanguage.DefaultLanguage,
                Flag = "🇻🇳",
                DisplayName = "Tiếng Việt"
            };

        // ✅ FIX: CRITICAL - DO NOT auto-reset here. Just log and return the recommendation.
        // The caller must decide whether to reset. This prevents the silent auto-reset bug.
        if (!string.Equals(currentLanguageCode, allowedLanguage.Code, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning(
                "Current language '{CurrentLanguage}' is locked or unavailable. Recommended fallback: '{AllowedLanguage}'. " +
                "Call RestoreToAllowedLanguageAsync() if you want to change it.",
                currentLanguageCode,
                allowedLanguage.Code);
        }

        return allowedLanguage.Code;
    }

    // ✅ NEW: Explicit method for restoring to allowed language when needed
    public async Task<string> RestoreToAllowedLanguageAsync()
    {
        var snapshot = await GetBootstrapSnapshotAsync();
        var languages = snapshot?.SupportedLanguages.Count > 0
            ? snapshot.SupportedLanguages
            : BuildSupportedLanguages(null);
        var currentLanguageCode = AppLanguage.NormalizeCode(_languageService.CurrentLanguage);
        var currentLanguage = languages.FirstOrDefault(item =>
            string.Equals(item.Code, currentLanguageCode, StringComparison.OrdinalIgnoreCase));

        // If current is allowed, don't reset
        if (currentLanguage is not null && !currentLanguage.IsLocked)
        {
            return currentLanguageCode;
        }

        // Find and restore to fallback
        var fallbackLanguage = languages.FirstOrDefault(item => !item.IsLocked)
            ?? languages.FirstOrDefault()
            ?? new LanguageOption
            {
                Code = AppLanguage.DefaultLanguage,
                Flag = "🇻🇳",
                DisplayName = "Tiếng Việt"
            };

        if (!string.Equals(currentLanguageCode, fallbackLanguage.Code, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogWarning(
                "Resetting language from '{CurrentLanguage}' to '{FallbackLanguage}' due to access restrictions.",
                currentLanguageCode,
                fallbackLanguage.Code);
            await _languageService.SetLanguageAsync(fallbackLanguage.Code);
        }

        return fallbackLanguage.Code;
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

        return null;
    }

    private async Task<IReadOnlyList<CustomerUserDto>> GetCustomerUsersAsync(bool requireSuccess = false)
    {
        var client = await GetClientAsync();
        if (client is null)
        {
            if (requireSuccess)
            {
                throw new MobileBackendConnectionException("The mobile app has no API base URL configured.");
            }

            return [];
        }

        try
        {
            var envelope = await client.GetFromJsonAsync<ApiEnvelope<List<CustomerUserDto>>>(
                "api/v1/customer-users",
                JsonOptions);

            if (envelope?.Success == true && envelope.Data is not null)
            {
                return envelope.Data;
            }

            if (requireSuccess)
            {
                throw new MobileBackendConnectionException(
                    envelope?.Message ?? "Unable to load customers from the backend.");
            }

            return [];
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Unable to load customer users from backend.");

            if (requireSuccess)
            {
                throw new MobileBackendConnectionException(
                    "Unable to connect to the backend to load customers.",
                    exception);
            }

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

    private static CustomerUserDto? FindCustomerByIdentifier(
        IReadOnlyList<CustomerUserDto> customerUsers,
        string identifier)
    {
        var normalizedIdentifier = identifier.Trim();
        var normalizedPhone = NormalizePhone(normalizedIdentifier);

        return customerUsers.FirstOrDefault(item =>
                   string.Equals(item.Email, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.Username, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(item.Id, normalizedIdentifier, StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrWhiteSpace(item.Phone) &&
                    string.Equals(NormalizePhone(item.Phone), normalizedPhone, StringComparison.OrdinalIgnoreCase)))
               ?? customerUsers.FirstOrDefault(item =>
                   string.Equals(item.Name, normalizedIdentifier, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePhone(string? value)
        => string.Concat((value ?? string.Empty).Where(char.IsDigit));

    private static string ReadCurrentCustomerId()
        => Preferences.Default.Get(AppPreferenceKeys.CurrentCustomerId, string.Empty);

    private static void SaveCurrentCustomerId(string customerId)
    {
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            Preferences.Default.Set(AppPreferenceKeys.CurrentCustomerId, customerId);
        }
    }

    private void InvalidateBootstrapSnapshot()
    {
        _bootstrapSource = null;
        _bootstrapSnapshot = null;
        _bootstrapSnapshotLanguageCode = null;
        _syncState = null;
        _lastSyncCheckAt = DateTimeOffset.MinValue;
    }

    private UserProfileCard MapUserProfile(CustomerUserDto customer)
    {
        return new UserProfileCard
        {
            CustomerId = customer.Id,
            FullName = FirstNonEmpty(customer.Name, customer.Username, customer.Email, customer.Id),
            Username = FirstNonEmpty(customer.Username, customer.Email, customer.Id),
            Email = customer.Email,
            Phone = customer.Phone,
            AvatarInitials = BuildInitials(customer.Name, customer.Username, customer.Email),
            MetaLine = $"ID {customer.Id} • {(customer.IsPremium ? "Premium" : "Free")} • {customer.PreferredLanguage.ToUpperInvariant()}",
            IsPremium = customer.IsPremium
        };
    }

    private sealed class PremiumPurchaseResponseDto
    {
        public CustomerUserDto Customer { get; set; } = new();
        public int ChargedAmountUsd { get; set; }
        public string CurrencyCode { get; set; } = "USD";
        public string PaymentProvider { get; set; } = "mock";
        public string PaymentMethod { get; set; } = PremiumPaymentMethodIds.BankCard;
        public string TransactionId { get; set; } = string.Empty;
    }
}
