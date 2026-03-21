using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using VinhKhanh.BackendApi.Contracts;
using VinhKhanh.BackendApi.Models;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed class AdminDataRepository
{
    private static readonly Regex InsertPattern = new(
        @"INSERT INTO seed_payloads\s*\(table_name,\s*payload_json\)\s*VALUES\s*\(\s*'([^']+)'\s*,\s*N'([\s\S]*?)'\s*\);",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string[] TableOrder =
    [
        "users",
        "customerUsers",
        "categories",
        "places",
        "foodItems",
        "translations",
        "audioGuides",
        "mediaAssets",
        "qrCodes",
        "routes",
        "promotions",
        "reviews",
        "viewLogs",
        "audioListenLogs",
        "auditLogs",
        "settings"
    ];

    private readonly object _syncRoot = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };
    private readonly string _seedSqlPath;
    private readonly AdminRepositoryState _state;

    public AdminDataRepository(IConfiguration configuration, IWebHostEnvironment environment)
    {
        _seedSqlPath = ResolveSeedSqlPath(configuration, environment);
        _state = LoadState(_seedSqlPath);
    }

    public IReadOnlyList<AdminUser> GetUsers() => Clone(_state.Users);
    public IReadOnlyList<CustomerUser> GetCustomerUsers() => Clone(_state.CustomerUsers);
    public IReadOnlyList<PlaceCategory> GetCategories() => Clone(_state.Categories);
    public IReadOnlyList<Place> GetPlaces() => Clone(_state.Places);
    public IReadOnlyList<Translation> GetTranslations() => Clone(_state.Translations);
    public IReadOnlyList<AudioGuide> GetAudioGuides() => Clone(_state.AudioGuides);
    public IReadOnlyList<MediaAsset> GetMediaAssets() => Clone(_state.MediaAssets);
    public IReadOnlyList<FoodItem> GetFoodItems() => Clone(_state.FoodItems);
    public IReadOnlyList<QRCodeRecord> GetQrCodes() => Clone(_state.QrCodes);
    public IReadOnlyList<TourRoute> GetRoutes() => Clone(_state.Routes);
    public IReadOnlyList<Promotion> GetPromotions() => Clone(_state.Promotions);
    public IReadOnlyList<Review> GetReviews() => Clone(_state.Reviews);
    public IReadOnlyList<ViewLog> GetViewLogs() => Clone(_state.ViewLogs);
    public IReadOnlyList<AudioListenLog> GetAudioListenLogs() => Clone(_state.AudioListenLogs);
    public IReadOnlyList<AuditLog> GetAuditLogs() => Clone(_state.AuditLogs);
    public SystemSetting GetSettings() => Clone(_state.Settings);

    public AdminBootstrapResponse GetBootstrap()
    {
        return new AdminBootstrapResponse(
            GetUsers(),
            GetCustomerUsers(),
            GetCategories(),
            GetPlaces(),
            GetTranslations(),
            GetAudioGuides(),
            GetMediaAssets(),
            GetFoodItems(),
            GetQrCodes(),
            GetRoutes(),
            GetPromotions(),
            GetReviews(),
            GetViewLogs(),
            GetAudioListenLogs(),
            GetAuditLogs(),
            GetSettings());
    }

    public DashboardSummaryResponse GetDashboardSummary()
    {
        return new DashboardSummaryResponse(
            _state.ViewLogs.Count,
            _state.AudioListenLogs.Count,
            _state.Places.Count(item => item.Status == "published"),
            _state.AudioGuides.Count(item => item.Status != "ready"),
            _state.QrCodes.Count(item => item.IsActive),
            _state.Reviews.Count(item => item.Status == "pending"),
            _state.Settings.PremiumLanguages.Count);
    }

    public AuthTokensResponse? Login(string email, string password)
    {
        lock (_syncRoot)
        {
            var user = _state.Users.FirstOrDefault(item =>
                item.Email.Equals(email, StringComparison.OrdinalIgnoreCase) &&
                item.Password == password &&
                item.Status == "active");

            if (user is null)
            {
                return null;
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            AppendAuditLog(user.Name, user.Role, "Dang nhap admin", user.Email);
            return CreateSession(user);
        }
    }

    public AuthTokensResponse? Refresh(string refreshToken)
    {
        lock (_syncRoot)
        {
            var session = _state.RefreshSessions.FirstOrDefault(item =>
                item.RefreshToken == refreshToken &&
                item.ExpiresAt > DateTimeOffset.UtcNow);

            if (session is null)
            {
                return null;
            }

            var user = _state.Users.FirstOrDefault(item => item.Id == session.UserId && item.Status == "active");
            if (user is null)
            {
                return null;
            }

            _state.RefreshSessions.Remove(session);
            AppendAuditLog(user.Name, user.Role, "Lam moi phien dang nhap", user.Email);
            return CreateSession(user);
        }
    }

    public void Logout(string refreshToken)
    {
        lock (_syncRoot)
        {
            _state.RefreshSessions.RemoveAll(item => item.RefreshToken == refreshToken);
            PersistLocked();
        }
    }

    public AdminUser SaveUser(string? id, AdminUserUpsertRequest request)
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = !string.IsNullOrWhiteSpace(id) ? _state.Users.FirstOrDefault(item => item.Id == id) : null;
            var isNew = existing is null;

            if (existing is null)
            {
                existing = new AdminUser
                {
                    Id = id ?? CreateId("user"),
                    CreatedAt = now
                };
                _state.Users.Insert(0, existing);
            }

            existing.Name = request.Name;
            existing.Email = request.Email;
            existing.Phone = request.Phone;
            existing.Role = request.Role;
            existing.Status = request.Status;
            existing.AvatarColor = request.AvatarColor;
            existing.ManagedPlaceId = request.Role == "PLACE_OWNER" ? request.ManagedPlaceId : null;
            existing.Password = !string.IsNullOrWhiteSpace(request.Password)
                ? request.Password
                : existing.Password is { Length: > 0 } ? existing.Password : "Admin@123";

            AppendAuditLog(
                request.ActorName,
                request.ActorRole,
                isNew ? "Tao tai khoan admin" : "Cap nhat tai khoan admin",
                existing.Email);

            PersistLocked();
            return Clone(existing);
        }
    }

    public Place SavePlace(string? id, PlaceUpsertRequest request)
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = !string.IsNullOrWhiteSpace(id) ? _state.Places.FirstOrDefault(item => item.Id == id) : null;
            var isNew = existing is null;

            if (existing is null)
            {
                existing = new Place
                {
                    Id = id ?? CreateId("place"),
                    CreatedAt = now
                };
                _state.Places.Insert(0, existing);
            }

            existing.Slug = request.Slug;
            existing.Address = request.Address;
            existing.Lat = request.Lat;
            existing.Lng = request.Lng;
            existing.CategoryId = request.CategoryId;
            existing.Status = request.Status;
            existing.Featured = request.Featured;
            existing.DefaultLanguageCode = request.DefaultLanguageCode;
            existing.District = request.District;
            existing.Ward = request.Ward;
            existing.PriceRange = request.PriceRange;
            existing.AverageVisitDuration = request.AverageVisitDuration;
            existing.PopularityScore = request.PopularityScore;
            existing.Tags = request.Tags;
            existing.OwnerUserId = request.OwnerUserId;
            existing.UpdatedBy = request.UpdatedBy;
            existing.UpdatedAt = now;

            UpsertPlaceQr(existing);
            AppendAuditLog(request.UpdatedBy, "SYSTEM", isNew ? "Tao dia diem" : "Cap nhat dia diem", existing.Slug);

            PersistLocked();
            return Clone(existing);
        }
    }

    public bool DeletePlace(string id)
    {
        lock (_syncRoot)
        {
            var place = _state.Places.FirstOrDefault(item => item.Id == id);
            if (place is null)
            {
                return false;
            }

            _state.Places.Remove(place);
            _state.Translations.RemoveAll(item => item.EntityType == "place" && item.EntityId == id);
            _state.AudioGuides.RemoveAll(item => item.EntityType == "place" && item.EntityId == id);
            _state.MediaAssets.RemoveAll(item => item.EntityType == "place" && item.EntityId == id);
            _state.FoodItems.RemoveAll(item => item.PlaceId == id);
            _state.Promotions.RemoveAll(item => item.PlaceId == id);
            _state.Reviews.RemoveAll(item => item.PlaceId == id);
            _state.ViewLogs.RemoveAll(item => item.PlaceId == id);
            _state.AudioListenLogs.RemoveAll(item => item.PlaceId == id);
            _state.QrCodes.RemoveAll(item => item.EntityType == "place" && item.EntityId == id);

            foreach (var user in _state.Users.Where(item => item.ManagedPlaceId == id))
            {
                user.ManagedPlaceId = null;
            }

            foreach (var route in _state.Routes)
            {
                route.StopPlaceIds = route.StopPlaceIds.Where(item => item != id).ToList();
            }

            AppendAuditLog("SYSTEM", "SYSTEM", "Xoa dia diem", place.Slug);
            PersistLocked();
            return true;
        }
    }

    public Translation SaveTranslation(string? id, TranslationUpsertRequest request)
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = !string.IsNullOrWhiteSpace(id) ? _state.Translations.FirstOrDefault(item => item.Id == id) : null;

            if (existing is null)
            {
                existing = _state.Translations.FirstOrDefault(item =>
                    item.EntityType == request.EntityType &&
                    item.EntityId == request.EntityId &&
                    item.LanguageCode == request.LanguageCode);
            }

            var isNew = existing is null;

            if (existing is null)
            {
                existing = new Translation
                {
                    Id = id ?? CreateId("trans")
                };
                _state.Translations.Insert(0, existing);
            }

            existing.EntityType = request.EntityType;
            existing.EntityId = request.EntityId;
            existing.LanguageCode = request.LanguageCode;
            existing.Title = request.Title;
            existing.ShortText = request.ShortText;
            existing.FullText = request.FullText;
            existing.SeoTitle = request.SeoTitle;
            existing.SeoDescription = request.SeoDescription;
            existing.IsPremium = request.IsPremium;
            existing.UpdatedBy = request.UpdatedBy;
            existing.UpdatedAt = now;

            AppendAuditLog(
                request.UpdatedBy,
                "SYSTEM",
                isNew ? "Tao noi dung thuyet minh" : "Cap nhat noi dung thuyet minh",
                $"{existing.EntityType}:{existing.LanguageCode}:{existing.EntityId}");

            PersistLocked();
            return Clone(existing);
        }
    }

    public bool DeleteTranslation(string id)
    {
        lock (_syncRoot)
        {
            var deleted = _state.Translations.RemoveAll(item => item.Id == id) > 0;
            if (deleted)
            {
                AppendAuditLog("SYSTEM", "SYSTEM", "Xoa noi dung thuyet minh", id);
                PersistLocked();
            }

            return deleted;
        }
    }

    public AudioGuide SaveAudioGuide(string? id, AudioGuideUpsertRequest request)
    {
        lock (_syncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            var existing = !string.IsNullOrWhiteSpace(id) ? _state.AudioGuides.FirstOrDefault(item => item.Id == id) : null;

            if (existing is null)
            {
                existing = _state.AudioGuides.FirstOrDefault(item =>
                    item.EntityType == request.EntityType &&
                    item.EntityId == request.EntityId &&
                    item.LanguageCode == request.LanguageCode);
            }

            var isNew = existing is null;

            if (existing is null)
            {
                existing = new AudioGuide
                {
                    Id = id ?? CreateId("audio")
                };
                _state.AudioGuides.Insert(0, existing);
            }

            existing.EntityType = request.EntityType;
            existing.EntityId = request.EntityId;
            existing.LanguageCode = request.LanguageCode;
            existing.AudioUrl = request.AudioUrl;
            existing.VoiceType = request.VoiceType;
            existing.SourceType = request.SourceType;
            existing.Status = request.Status;
            existing.UpdatedBy = request.UpdatedBy;
            existing.UpdatedAt = now;

            AppendAuditLog(
                request.UpdatedBy,
                "SYSTEM",
                isNew ? "Tao audio guide" : "Cap nhat audio guide",
                $"{existing.EntityType}:{existing.LanguageCode}:{existing.EntityId}");

            PersistLocked();
            return Clone(existing);
        }
    }

    public bool DeleteAudioGuide(string id)
    {
        lock (_syncRoot)
        {
            var deleted = _state.AudioGuides.RemoveAll(item => item.Id == id) > 0;
            if (deleted)
            {
                AppendAuditLog("SYSTEM", "SYSTEM", "Xoa audio guide", id);
                PersistLocked();
            }

            return deleted;
        }
    }

    public MediaAsset SaveMediaAsset(string? id, MediaAssetUpsertRequest request)
    {
        lock (_syncRoot)
        {
            var existing = !string.IsNullOrWhiteSpace(id) ? _state.MediaAssets.FirstOrDefault(item => item.Id == id) : null;
            var isNew = existing is null;

            if (existing is null)
            {
                existing = new MediaAsset
                {
                    Id = id ?? CreateId("media"),
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _state.MediaAssets.Insert(0, existing);
            }

            existing.EntityType = request.EntityType;
            existing.EntityId = request.EntityId;
            existing.Type = request.Type;
            existing.Url = request.Url;
            existing.AltText = request.AltText;

            AppendAuditLog("SYSTEM", "SYSTEM", isNew ? "Tao media asset" : "Cap nhat media asset", existing.Id);
            PersistLocked();
            return Clone(existing);
        }
    }

    public bool DeleteMediaAsset(string id)
    {
        lock (_syncRoot)
        {
            var deleted = _state.MediaAssets.RemoveAll(item => item.Id == id) > 0;
            if (deleted)
            {
                AppendAuditLog("SYSTEM", "SYSTEM", "Xoa media asset", id);
                PersistLocked();
            }

            return deleted;
        }
    }

    public FoodItem SaveFoodItem(string? id, FoodItemUpsertRequest request)
    {
        lock (_syncRoot)
        {
            var existing = !string.IsNullOrWhiteSpace(id) ? _state.FoodItems.FirstOrDefault(item => item.Id == id) : null;
            var isNew = existing is null;

            if (existing is null)
            {
                existing = new FoodItem
                {
                    Id = id ?? CreateId("food")
                };
                _state.FoodItems.Insert(0, existing);
            }

            existing.PlaceId = request.PlaceId;
            existing.Name = request.Name;
            existing.Description = request.Description;
            existing.PriceRange = request.PriceRange;
            existing.ImageUrl = request.ImageUrl;
            existing.SpicyLevel = request.SpicyLevel;

            AppendAuditLog("SYSTEM", "SYSTEM", isNew ? "Tao mon an" : "Cap nhat mon an", existing.Name);
            PersistLocked();
            return Clone(existing);
        }
    }

    public bool DeleteFoodItem(string id)
    {
        lock (_syncRoot)
        {
            var deleted = _state.FoodItems.RemoveAll(item => item.Id == id) > 0;
            if (deleted)
            {
                AppendAuditLog("SYSTEM", "SYSTEM", "Xoa mon an", id);
                PersistLocked();
            }

            return deleted;
        }
    }

    public TourRoute SaveRoute(string? id, TourRouteUpsertRequest request)
    {
        lock (_syncRoot)
        {
            var existing = !string.IsNullOrWhiteSpace(id) ? _state.Routes.FirstOrDefault(item => item.Id == id) : null;
            var isNew = existing is null;

            if (existing is null)
            {
                existing = new TourRoute
                {
                    Id = id ?? CreateId("route")
                };
                _state.Routes.Insert(0, existing);
            }

            existing.Name = request.Name;
            existing.Description = request.Description;
            existing.DurationMinutes = request.DurationMinutes;
            existing.Difficulty = request.Difficulty;
            existing.StopPlaceIds = request.StopPlaceIds;
            existing.IsFeatured = request.IsFeatured;

            AppendAuditLog(request.ActorName, request.ActorRole, isNew ? "Tao tuyen tham quan" : "Cap nhat tuyen tham quan", existing.Name);
            PersistLocked();
            return Clone(existing);
        }
    }

    public bool DeleteRoute(string id)
    {
        lock (_syncRoot)
        {
            var deleted = _state.Routes.RemoveAll(item => item.Id == id) > 0;
            if (deleted)
            {
                AppendAuditLog("SYSTEM", "SYSTEM", "Xoa tuyen tham quan", id);
                PersistLocked();
            }

            return deleted;
        }
    }

    public Promotion SavePromotion(string? id, PromotionUpsertRequest request)
    {
        lock (_syncRoot)
        {
            var existing = !string.IsNullOrWhiteSpace(id) ? _state.Promotions.FirstOrDefault(item => item.Id == id) : null;
            var isNew = existing is null;

            if (existing is null)
            {
                existing = new Promotion
                {
                    Id = id ?? CreateId("promo")
                };
                _state.Promotions.Insert(0, existing);
            }

            existing.PlaceId = request.PlaceId;
            existing.Title = request.Title;
            existing.Description = request.Description;
            existing.StartAt = request.StartAt;
            existing.EndAt = request.EndAt;
            existing.Status = request.Status;

            AppendAuditLog(request.ActorName, request.ActorRole, isNew ? "Tao uu dai" : "Cap nhat uu dai", existing.Title);
            PersistLocked();
            return Clone(existing);
        }
    }

    public bool DeletePromotion(string id)
    {
        lock (_syncRoot)
        {
            var deleted = _state.Promotions.RemoveAll(item => item.Id == id) > 0;
            if (deleted)
            {
                AppendAuditLog("SYSTEM", "SYSTEM", "Xoa uu dai", id);
                PersistLocked();
            }

            return deleted;
        }
    }

    public Review? UpdateReviewStatus(string id, ReviewStatusRequest request)
    {
        lock (_syncRoot)
        {
            var review = _state.Reviews.FirstOrDefault(item => item.Id == id);
            if (review is null)
            {
                return null;
            }

            review.Status = request.Status;
            AppendAuditLog(request.ActorName, request.ActorRole, "Cap nhat trang thai danh gia", id);
            PersistLocked();
            return Clone(review);
        }
    }

    public QRCodeRecord? UpdateQrState(string id, QrCodeStateRequest request)
    {
        lock (_syncRoot)
        {
            var qr = _state.QrCodes.FirstOrDefault(item => item.Id == id);
            if (qr is null)
            {
                return null;
            }

            qr.IsActive = request.IsActive;
            AppendAuditLog(request.ActorName, request.ActorRole, "Cap nhat trang thai QR", id);
            PersistLocked();
            return Clone(qr);
        }
    }

    public QRCodeRecord? UpdateQrImage(string id, QrCodeImageRequest request)
    {
        lock (_syncRoot)
        {
            var qr = _state.QrCodes.FirstOrDefault(item => item.Id == id);
            if (qr is null)
            {
                return null;
            }

            qr.QrImageUrl = request.QrImageUrl;
            AppendAuditLog(request.ActorName, request.ActorRole, "Cap nhat anh QR", id);
            PersistLocked();
            return Clone(qr);
        }
    }

    public SystemSetting SaveSettings(SystemSettingUpsertRequest request)
    {
        lock (_syncRoot)
        {
            _state.Settings.AppName = request.AppName;
            _state.Settings.SupportEmail = request.SupportEmail;
            _state.Settings.DefaultLanguage = request.DefaultLanguage;
            _state.Settings.FallbackLanguage = request.FallbackLanguage;
            _state.Settings.FreeLanguages = request.FreeLanguages;
            _state.Settings.PremiumLanguages = request.PremiumLanguages;
            _state.Settings.PremiumUnlockPriceUsd = request.PremiumUnlockPriceUsd;
            _state.Settings.MapProvider = request.MapProvider;
            _state.Settings.StorageProvider = request.StorageProvider;
            _state.Settings.TtsProvider = request.TtsProvider;
            _state.Settings.GeofenceRadiusMeters = request.GeofenceRadiusMeters;
            _state.Settings.QrAutoPlay = request.QrAutoPlay;
            _state.Settings.GuestReviewEnabled = request.GuestReviewEnabled;
            _state.Settings.AnalyticsRetentionDays = request.AnalyticsRetentionDays;

            AppendAuditLog(request.ActorName, request.ActorRole, "Cap nhat cai dat he thong", request.AppName);
            PersistLocked();
            return Clone(_state.Settings);
        }
    }

    private void UpsertPlaceQr(Place place)
    {
        var existingQr = _state.QrCodes.FirstOrDefault(item => item.EntityType == "place" && item.EntityId == place.Id);
        var qrValue = $"https://guide.vinhkhanh.vn/scan/{place.Slug}";
        var qrImageUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=320x320&data={Uri.EscapeDataString(qrValue)}";

        if (existingQr is null)
        {
            _state.QrCodes.Insert(0, new QRCodeRecord
            {
                Id = CreateId("qr"),
                EntityType = "place",
                EntityId = place.Id,
                QrValue = qrValue,
                QrImageUrl = qrImageUrl,
                IsActive = place.Status == "published"
            });

            return;
        }

        existingQr.QrValue = qrValue;
        existingQr.QrImageUrl = qrImageUrl;
        existingQr.IsActive = place.Status == "published";
    }

    private AuthTokensResponse CreateSession(AdminUser user)
    {
        var accessToken = $"vk_access_{Guid.NewGuid():N}";
        var refreshToken = $"vk_refresh_{Guid.NewGuid():N}";
        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);

        _state.RefreshSessions.Add(new RefreshSession
        {
            UserId = user.Id,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });

        PersistLocked();

        return new AuthTokensResponse(
            user.Id,
            user.Name,
            user.Email,
            user.Role,
            accessToken,
            refreshToken,
            expiresAt);
    }

    private void AppendAuditLog(string actorName, string actorRole, string action, string target)
    {
        _state.AuditLogs.Insert(0, new AuditLog
        {
            Id = CreateId("audit"),
            ActorName = actorName,
            ActorRole = actorRole,
            Action = action,
            Target = target,
            CreatedAt = DateTimeOffset.UtcNow
        });

        if (_state.AuditLogs.Count > 120)
        {
            _state.AuditLogs.RemoveRange(120, _state.AuditLogs.Count - 120);
        }
    }

    private void PersistLocked()
    {
        var sql = BuildSqlDocument(_state);
        File.WriteAllText(_seedSqlPath, sql, new UTF8Encoding(false));
    }

    private AdminRepositoryState LoadState(string seedSqlPath)
    {
        if (!File.Exists(seedSqlPath))
        {
            throw new FileNotFoundException($"Khong tim thay file SQL seed: {seedSqlPath}", seedSqlPath);
        }

        var sql = File.ReadAllText(seedSqlPath, Encoding.UTF8);
        var payloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in InsertPattern.Matches(sql))
        {
            payloads[match.Groups[1].Value] = match.Groups[2].Value.Replace("''", "'");
        }

        return new AdminRepositoryState
        {
            Users = ReadPayload<List<AdminUser>>(payloads, "users"),
            CustomerUsers = ReadPayload<List<CustomerUser>>(payloads, "customerUsers"),
            Categories = ReadPayload<List<PlaceCategory>>(payloads, "categories"),
            Places = ReadPayload<List<Place>>(payloads, "places"),
            FoodItems = ReadPayload<List<FoodItem>>(payloads, "foodItems"),
            Translations = ReadPayload<List<Translation>>(payloads, "translations"),
            AudioGuides = ReadPayload<List<AudioGuide>>(payloads, "audioGuides"),
            MediaAssets = ReadPayload<List<MediaAsset>>(payloads, "mediaAssets"),
            QrCodes = ReadPayload<List<QRCodeRecord>>(payloads, "qrCodes"),
            Routes = ReadPayload<List<TourRoute>>(payloads, "routes"),
            Promotions = ReadPayload<List<Promotion>>(payloads, "promotions"),
            Reviews = ReadPayload<List<Review>>(payloads, "reviews"),
            ViewLogs = ReadPayload<List<ViewLog>>(payloads, "viewLogs"),
            AudioListenLogs = ReadPayload<List<AudioListenLog>>(payloads, "audioListenLogs"),
            AuditLogs = ReadPayload<List<AuditLog>>(payloads, "auditLogs"),
            Settings = ReadPayload<SystemSetting>(payloads, "settings")
        };
    }

    private T ReadPayload<T>(IReadOnlyDictionary<string, string> payloads, string key)
    {
        if (!payloads.TryGetValue(key, out var payload))
        {
            throw new InvalidOperationException($"Khong tim thay payload '{key}' trong SQL seed.");
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var json = document.RootElement.ValueKind == JsonValueKind.Object &&
                       document.RootElement.TryGetProperty("value", out var wrappedValue)
                ? wrappedValue.GetRawText()
                : payload;

            return JsonSerializer.Deserialize<T>(json, _jsonOptions)
                ?? throw new InvalidOperationException($"Khong the parse payload '{key}'.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Khong the parse payload '{key}'.", exception);
        }
    }

    private string BuildSqlDocument(AdminRepositoryState state)
    {
        var payloadMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["users"] = SerializePayload(state.Users),
            ["customerUsers"] = SerializePayload(state.CustomerUsers),
            ["categories"] = SerializePayload(state.Categories),
            ["places"] = SerializePayload(state.Places),
            ["foodItems"] = SerializePayload(state.FoodItems),
            ["translations"] = SerializePayload(state.Translations),
            ["audioGuides"] = SerializePayload(state.AudioGuides),
            ["mediaAssets"] = SerializePayload(state.MediaAssets),
            ["qrCodes"] = SerializePayload(state.QrCodes),
            ["routes"] = SerializePayload(state.Routes),
            ["promotions"] = SerializePayload(state.Promotions),
            ["reviews"] = SerializePayload(state.Reviews),
            ["viewLogs"] = SerializePayload(state.ViewLogs),
            ["audioListenLogs"] = SerializePayload(state.AudioListenLogs),
            ["auditLogs"] = SerializePayload(state.AuditLogs),
            ["settings"] = SerializePayload(state.Settings)
        };

        var builder = new StringBuilder()
            .AppendLine("-- Demo seed for Vinh Khanh admin web")
            .AppendLine("IF OBJECT_ID(N'dbo.seed_payloads', N'U') IS NULL")
            .AppendLine("BEGIN")
            .AppendLine("  CREATE TABLE dbo.seed_payloads (")
            .AppendLine("    table_name NVARCHAR(100) NOT NULL,")
            .AppendLine("    payload_json NVARCHAR(MAX) NOT NULL")
            .AppendLine("  );")
            .AppendLine("END;")
            .AppendLine();

        foreach (var tableName in TableOrder)
        {
            builder
                .Append("INSERT INTO seed_payloads (table_name, payload_json) VALUES ('")
                .Append(tableName)
                .Append("', N'")
                .Append(payloadMap[tableName].Replace("'", "''"))
                .AppendLine("');")
                .AppendLine();
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private string SerializePayload<T>(T value) => JsonSerializer.Serialize(value, _jsonOptions);

    private T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        return JsonSerializer.Deserialize<T>(json, _jsonOptions)!;
    }

    private static string CreateId(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix}-{suffix}";
    }

    private static string ResolveSeedSqlPath(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["SeedData:SqlPath"];
        var relativePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine("..", "admin-web", "src", "data", "sql", "admin-seed.sql")
            : configuredPath;

        return Path.GetFullPath(relativePath, environment.ContentRootPath);
    }
}

public sealed class AdminRepositoryState
{
    public List<AdminUser> Users { get; init; } = [];
    public List<CustomerUser> CustomerUsers { get; init; } = [];
    public List<PlaceCategory> Categories { get; init; } = [];
    public List<Place> Places { get; init; } = [];
    public List<Translation> Translations { get; init; } = [];
    public List<AudioGuide> AudioGuides { get; init; } = [];
    public List<MediaAsset> MediaAssets { get; init; } = [];
    public List<FoodItem> FoodItems { get; init; } = [];
    public List<QRCodeRecord> QrCodes { get; init; } = [];
    public List<TourRoute> Routes { get; init; } = [];
    public List<Promotion> Promotions { get; init; } = [];
    public List<Review> Reviews { get; init; } = [];
    public List<ViewLog> ViewLogs { get; init; } = [];
    public List<AudioListenLog> AudioListenLogs { get; init; } = [];
    public List<AuditLog> AuditLogs { get; init; } = [];
    public List<RefreshSession> RefreshSessions { get; init; } = [];
    public SystemSetting Settings { get; init; } = new();
}
