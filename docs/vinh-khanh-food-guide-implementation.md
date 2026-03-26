# VinhKhanhFoodGuide

## Phần A. Phân tích yêu cầu

### 1. Bài toán

`VinhKhanhFoodGuide` là hệ thống thuyết minh tự động đa ngôn ngữ cho phố ẩm thực Vĩnh Khánh gồm:

- Mobile app cho khách du lịch bằng `.NET MAUI`
- Backend API bằng `ASP.NET Core Web API`
- Dữ liệu trên `SQL Server`
- Bản đồ POI, định vị người dùng, geofence tự phát audio/TTS
- Quét QR hoặc chọn POI trên bản đồ để mở đúng nội dung theo ngôn ngữ hiện tại

### 2. Module chính

- `Mobile Experience`
  - Splash
  - Chọn ngôn ngữ
  - Trang chủ
  - Bản đồ POI
  - Danh sách địa điểm
  - Chi tiết địa điểm
  - Quét QR
  - Cài đặt
- `Public API`
  - Lấy settings mobile
  - Danh sách POI / nearby / detail / routes
  - Ghi nhận lượt xem, lượt nghe
- `Admin API`
  - JWT login
  - CRUD POI
  - CRUD translation
  - CRUD audio guide
  - CRUD food item
  - CRUD media asset
  - Thống kê
- `Data Layer`
  - Kế thừa schema SQL Server từ admin-web
  - Bổ sung `QrCode` và `OpeningHours` cho `Pois`

### 3. Luồng người dùng

1. Mở app, app tải mobile settings và ngôn ngữ đã lưu.
2. Nếu chưa có ngôn ngữ, chuyển sang màn hình chọn ngôn ngữ.
3. Vào trang chủ, người dùng chọn map, list, QR hoặc settings.
4. Ở map/list, app tải POI theo ngôn ngữ hiện tại.
5. Chạm marker hoặc item list:
   - focus đúng POI
   - mở thông tin rút gọn / chi tiết
   - tự gọi narration ngay
6. Khi bật auto narration, app theo dõi vị trí gần realtime:
   - xin quyền location
   - tính khoảng cách tới POI
   - nếu trong bán kính và chưa phát gần đây thì tự phát
7. Nếu app offline, dùng cache JSON để hiển thị dữ liệu cơ bản gần nhất.

## Phần B. Thiết kế kiến trúc

### 1. Kiến trúc tổng thể

```text
Mobile MAUI
-> Services / ViewModels / Localization / Geofence
-> REST API /api/guide/v1/*
-> ASP.NET Core Controllers
-> Service Layer
-> Repository Layer
-> EF Core DbContext
-> SQL Server (schema gốc admin-web + mobile extension)
```

### 2. Luồng dữ liệu

```text
User Location
-> LocationTrackerService
-> GeoFenceHelper
-> GuideApiService.GetPoiByIdAsync(...)
-> NarrationService
-> Audio file OR TextToSpeech fallback
-> GuideApiService.TrackAudioAsync(...)
```

### 3. Mobile giao tiếp API

- Tất cả request đi qua `GuideApiService`
- API wrapper dùng `ApiEnvelope<T>`
- Có retry đơn giản `0ms -> 500ms -> 1200ms`
- Nếu lỗi mạng:
  - fallback sang `OfflineCacheService`
  - lấy cache list/detail/routes/settings gần nhất

### 4. Geolocation + map + auto voice

- `LocationTrackerService`
  - xin `LocationWhenInUse`
  - poll vị trí theo chu kỳ 8 giây
  - phát hiện POI trong bán kính cài đặt
  - có cooldown 15 phút mỗi POI
- `MapPage`
  - render pin từ danh sách POI
  - tap marker sẽ focus map và tự gọi narration
- `NarrationService`
  - ưu tiên audio có sẵn nếu có
  - nếu không có thì dùng `TextToSpeech.Default.SpeakAsync`

## Phần C. Database

### 1. Cơ sở dữ liệu gốc

Lấy trực tiếp schema từ:

- `apps/admin-web/src/data/sql/admin-seed-sqlserver.sql`

Các bảng chính đã có:

- `AdminUsers`
- `Pois`
- `PoiTranslations`
- `AudioGuides`
- `MediaAssets`
- `FoodItems`
- `Routes`
- `RouteStops`
- `ViewLogs`
- `AudioListenLogs`
- `SystemSettings`
- `SystemSettingLanguages`

### 2. Mở rộng cho mobile

Chạy thêm script:

- `docs/sql/vinh-khanh-food-guide-mobile-extension.sql`

Script này:

- thêm `QrCode` vào `dbo.Pois`
- thêm `OpeningHours` vào `dbo.Pois`
- seed/normalize QR code và giờ mở cửa cho 3 POI mẫu

### 3. Seed data mẫu

Ba POI mẫu dùng xuyên suốt hệ thống:

- `poi-bbq-night`
- `poi-snail-signature`
- `poi-sweet-lane`

Mỗi POI đã có:

- tên tiếng Việt / English
- mô tả ngắn / dài
- audio guide
- ảnh
- món ăn nổi bật
- tọa độ map

## Phần D. Backend API

### 1. Cấu trúc triển khai

- `apps/backend-api/Authentication/JwtOptions.cs`
- `apps/backend-api/Application/Services/GuideServices.cs`
- `apps/backend-api/Controllers/GuidePublicControllers.cs`
- `apps/backend-api/Controllers/GuideAdminControllers.cs`
- `apps/backend-api/Domain/Entities/GuideDomainEntities.cs`
- `apps/backend-api/DTOs/GuideDtos.cs`
- `apps/backend-api/Infrastructure/Persistence/GuidePersistence.cs`
- `apps/backend-api/Mappings/GuideMappings.cs`
- `apps/backend-api/Middlewares/ApiExceptionMiddleware.cs`
- `apps/backend-api/Repositories/GuideRepositories.cs`

### 2. API public quan trọng

- `GET /api/guide/v1/settings/mobile`
- `GET /api/guide/v1/pois`
- `GET /api/guide/v1/pois/nearby`
- `GET /api/guide/v1/pois/routes`
- `GET /api/guide/v1/pois/{id}`
- `GET /api/guide/v1/pois/slug/{slug}`
- `GET /api/guide/v1/pois/qr/{qrCode}`
- `POST /api/guide/v1/pois/{id}/events/view`
- `POST /api/guide/v1/pois/{id}/events/audio`

### 3. API admin

- `POST /api/guide/v1/auth/admin/login`
- `POST /api/guide/v1/auth/admin/refresh`
- `POST /api/guide/v1/auth/admin/logout`
- `GET /api/guide/v1/admin/pois`
- `POST /api/guide/v1/admin/pois`
- `PUT /api/guide/v1/admin/pois/{id}`
- `DELETE /api/guide/v1/admin/pois/{id}`
- `PUT /api/guide/v1/admin/pois/{poiId}/translations/{languageCode}`
- `PUT /api/guide/v1/admin/pois/{poiId}/audio-guides/{languageCode}`
- `POST /api/guide/v1/admin/pois/{poiId}/food-items`
- `POST /api/guide/v1/admin/pois/{poiId}/media-assets`
- `GET /api/guide/v1/admin/analytics/overview`

### 4. JWT

- cấu hình tại `appsettings.json` mục `Jwt`
- access token có role claim và `managed_poi_id`
- `PLACE_OWNER` bị chặn nếu thao tác POI không thuộc quyền

### 5. Swagger

- backend bật `Swagger` trong môi trường development
- endpoint gốc `/swagger`

### 6. JSON mẫu

#### Admin login request

```json
{
  "email": "superadmin@vinhkhanh.vn",
  "password": "Admin@123"
}
```

#### Poi nearby response

```json
{
  "success": true,
  "data": [
    {
      "id": "poi-bbq-night",
      "title": "Quảng trường Ẩm thực BBQ Night",
      "latitude": 10.7594,
      "longitude": 106.7016,
      "distanceMeters": 18.4
    }
  ]
}
```

## Phần E. Mobile App .NET MAUI

### 1. Cấu trúc

- `apps/mobile-app/Views`
- `apps/mobile-app/ViewModels`
- `apps/mobile-app/Models`
- `apps/mobile-app/Services`
- `apps/mobile-app/Helpers`
- `apps/mobile-app/Resources/Localization`
- `apps/mobile-app/Resources/Raw`
- `apps/mobile-app/Platforms`

### 2. Màn hình đã có

- `SplashPage`
- `LanguageSelectionPage`
- `HomePage`
- `MapPage`
- `PoiListPage`
- `PoiDetailPage`
- `QrScannerPage`
- `SettingsPage`

### 3. Thành phần kỹ thuật chính

- `MauiProgram.cs`
  - đăng ký map, barcode reader, audio manager, services, viewmodels
- `Services/MobileServices.cs`
  - settings
  - localization
  - API client
  - offline cache
  - narration
  - location tracker
- `ViewModels/MobileViewModels.cs`
  - MVVM cho toàn bộ màn hình chính
- `Views/MapPage.xaml.cs`
  - render pin và auto playback khi chạm marker

### 4. Auto trigger TTS/audio

- `MapViewModel.LoadAsync()` bật `LocationTrackerService.StartAsync(...)`
- khi POI lọt vào geofence:
  - tải detail
  - gọi `NarrationService.PlayAsync(...)`
  - gọi API analytics audio

## Phần F. Tính năng nâng cao

### 1. Offline cache

- `OfflineCacheService`
- lưu:
  - settings
  - list POI
  - detail POI
  - routes

### 2. Logging

- backend: `ILogger + console/debug`
- mobile: `ILogger<T>` trong API/location/narration services

### 3. Error handling

- backend: `ApiExceptionMiddleware`
- mobile: fallback cache nếu API lỗi

### 4. Retry API

- nằm trong `GuideApiService.RetryAsync`
- backoff ngắn 3 lần

### 5. Analytics cơ bản

- track view
- track audio play
- API thống kê top POI

## Phần G. Hướng dẫn chạy dự án

### 1. Tạo database

1. Chạy script gốc:
   - `apps/admin-web/src/data/sql/admin-seed-sqlserver.sql`
2. Chạy script mở rộng:
   - `docs/sql/vinh-khanh-food-guide-mobile-extension.sql`

### 2. Chạy backend

```powershell
dotnet build apps/backend-api/VinhKhanh.BackendApi.csproj
dotnet run --project apps/backend-api/VinhKhanh.BackendApi.csproj
```

### 3. Chạy MAUI Windows

```powershell
dotnet restore apps/mobile-app/VinhKhanh.MobileApp.csproj --source https://api.nuget.org/v3/index.json
dotnet build apps/mobile-app/VinhKhanh.MobileApp.csproj -f net10.0-windows10.0.19041.0
dotnet run --project apps/mobile-app/VinhKhanh.MobileApp.csproj -f net10.0-windows10.0.19041.0
```

### 4. Chạy Android

```powershell
dotnet build apps/mobile-app/VinhKhanh.MobileApp.csproj -f net10.0-android
dotnet run --project apps/mobile-app/VinhKhanh.MobileApp.csproj -f net10.0-android
```

### 5. Cấu hình base URL mobile

- mặc định `UserSettings.ApiBaseUrl`
- hiện tại đặt trong code là `https://localhost:7055`
- file raw mẫu:
  - `apps/mobile-app/Resources/Raw/appsettings.json`

### 6. Quyền cần cấp

- Android:
  - `INTERNET`
  - `ACCESS_COARSE_LOCATION`
  - `ACCESS_FINE_LOCATION`
  - `CAMERA`
- iOS:
  - `NSCameraUsageDescription`
  - `NSLocationWhenInUseUsageDescription`
  - `NSLocationAlwaysAndWhenInUseUsageDescription`

## Phần H. Code hoàn chỉnh

### 1. File trọng tâm backend

- `apps/backend-api/Program.cs`
- `apps/backend-api/Controllers/GuidePublicControllers.cs`
- `apps/backend-api/Controllers/GuideAdminControllers.cs`
- `apps/backend-api/Application/Services/GuideServices.cs`
- `apps/backend-api/Infrastructure/Persistence/GuidePersistence.cs`
- `apps/backend-api/Repositories/GuideRepositories.cs`

### 2. File trọng tâm mobile

- `apps/mobile-app/MauiProgram.cs`
- `apps/mobile-app/Services/MobileServices.cs`
- `apps/mobile-app/ViewModels/MobileViewModels.cs`
- `apps/mobile-app/Views/MapPage.xaml`
- `apps/mobile-app/Views/PoiDetailPage.xaml`
- `apps/mobile-app/Views/QrScannerPage.xaml`
- `apps/mobile-app/Views/SettingsPage.xaml`

### 3. Ghi chú thực tế

- Build backend: thành công
- Build MAUI Windows: thành công
- MAUI còn warning từ `ZXing.Net.Maui.Controls` và `Frame` obsolete, nhưng không chặn build
- Nếu cần production hardening tiếp:
  - hash password admin
  - đổi `DangerousAcceptAnyServerCertificateValidator`
  - thay `Frame` bằng `Border`
  - tách interfaces/services/entities thành từng file nhỏ riêng
