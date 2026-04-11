# Vinh Khanh Food

README này mô tả luồng hệ thống và công nghệ hiện tại của đồ án Vinh Khanh Food. Hệ thống gồm admin web, backend API và ứng dụng mobile Android cho khách hàng.

## Các lỗi đang phát sinh cần fix và cần cải thiện
- Tour chưa hợp lí
- Chưa làm backend khi khách hàng quên mật khẩu
- Cần thêm chatbox AI cho app (xài API key của Gemini)
- Chưa tạo định vị ảo để khi người dùng vào bán kính 10m của quán thì tự động phát thuyết minh quán đó.

## Thành phần chính

| Thành phần | Thư mục | Công nghệ | Vai trò |
| --- | --- | --- | --- |
| Admin web | `apps/admin-web` | React 19, TypeScript, Vite 6, React Router 7, Tailwind CSS, Recharts, Leaflet/OpenStreetMap | Quản trị nội dung, POI, tour, media, thuyết minh, người dùng, đánh giá, khuyến mãi và cấu hình |
| Backend API | `apps/backend-api` | ASP.NET Core `.NET 10`, SQL Server, Swagger, SPA Proxy, HttpClient, MemoryCache | Xử lý nghiệp vụ, xác thực, phân quyền, bootstrap dữ liệu, dịch nội dung, TTS, upload, geocoding và premium |
| Mobile app | `apps/mobile-app` | .NET MAUI `net10.0-android`, Plugin.Maui.Audio, ZXing.Net.Maui, WebView + Leaflet/OpenStreetMap | Trải nghiệm khách hàng: đăng nhập, đổi ngôn ngữ, bản đồ, POI, thuyết minh, My Tour, QR, premium và tài khoản |
| Tools | `tools` | .NET console smoke tests | Kiểm tra nhanh các luồng i18n, TTS và chuẩn hóa địa chỉ POI |
| Scripts | `scripts` | Node.js và PowerShell | Chạy backend, admin web, mobile watcher và toàn bộ môi trường local |

## Kiến trúc tổng thể

```text
Admin Web
  |
  |  /api/v1/auth, /api/v1/bootstrap, /api/v1/pois, /api/v1/tts, ...
  v
Backend API  -------------------->  SQL Server
  |                                    |
  |                                    +-- AdminUsers, CustomerUsers, Pois, Translations
  |                                    +-- AudioGuides, MediaAssets, Routes, Promotions
  |                                    +-- Reviews, ViewLogs, AudioListenLogs, Settings
  |
  +-- Geocoding proxy
  +-- Google Translate proxy cho nội dung thiếu bản dịch
  +-- ElevenLabs Text-to-Speech proxy
  +-- Upload/static file qua /storage
  +-- Premium language gate
  |
  v
Mobile App
  |
  +-- Đọc bootstrap theo customerUserId và languageCode
  +-- Hiển thị bản đồ OpenStreetMap/Leaflet trong WebView
  +-- Resolve nội dung POI, popup, audio và thuyết minh theo ngôn ngữ đang chọn
```

Backend API là nguồn dữ liệu trung tâm. Admin web tạo và cập nhật dữ liệu; mobile app đọc dữ liệu đã chuẩn hóa từ backend để hiển thị cho khách hàng.

## Vai trò người dùng

| Vai trò | Nơi sử dụng | Phạm vi |
| --- | --- | --- |
| `SUPER_ADMIN` | `/admin/*` trên admin web | Quản lý toàn bộ dữ liệu hệ thống |
| `PLACE_OWNER` | `/restaurant/*` trên admin web | Quản lý dữ liệu của POI/quán được phân công |
| Khách hàng | Mobile app | Xem bản đồ, POI, thuyết minh, tour, QR, tài khoản, ngôn ngữ và premium |

## Luồng admin web

1. Người dùng vào `/login`, `/admin/login` hoặc `/restaurant/login`.
2. Admin web gọi `api/v1/auth/login` để nhận phiên đăng nhập, access token, refresh token và vai trò.
3. Router điều hướng theo vai trò:
   - `SUPER_ADMIN` vào `/admin/dashboard`.
   - `PLACE_OWNER` vào `/restaurant/dashboard`.
4. `AdminDataProvider` gọi `api/v1/bootstrap`, sau đó giữ state dùng chung cho dashboard, POI, tour, user, review, promotion, activity và settings.
5. Khi tạo hoặc cập nhật dữ liệu, admin web gọi các API tương ứng như `pois`, `translations`, `audio-guides`, `media-assets`, `food-items`, `tours`, `promotions`, `reviews`, `admin-users` hoặc `settings`.
6. Backend kiểm tra quyền bằng `AdminRequestContextResolver`. Với `PLACE_OWNER`, dữ liệu được giới hạn theo POI/quán mà chủ quán phụ trách.
7. Sau khi lưu thành công, admin web refresh lại bootstrap hoặc cập nhật state để đồng bộ dữ liệu mới nhất.

## Luồng POI, bản đồ và nội dung quản trị

1. Admin quản lý POI trong `PoisPage`.
2. Form POI dùng `OpenStreetMapPicker` để chọn tọa độ, xem marker, xem POI lân cận và cập nhật địa chỉ.
3. Bản đồ admin dùng React Leaflet + OpenStreetMap tile. Khi bản đồ nằm trong popup/modal sửa POI, component có `ResizeObserver` và nhiều lần `invalidateSize()` để tránh lỗi bản đồ bị xanh hoặc không render đủ tile.
4. Geocoding đi qua backend `api/v1/geocoding`, sau đó chuẩn hóa địa chỉ bằng `PoiAddressNormalizer` để tránh gán sai khu Vĩnh Khánh sang Thành phố Thủ Đức.
5. Media, món ăn, khuyến mãi và thuyết minh của POI được quản lý cùng dữ liệu bản dịch và audio guide để mobile app có thể đọc lại qua bootstrap.

## Luồng mobile app

1. App khởi động bằng `AppShell` tại `LoginPage`.
2. `AppLanguageService` khởi tạo ngôn ngữ từ `Preferences` với key `vkfood.language.code`.
3. Người dùng đăng nhập, đăng ký hoặc dùng tài khoản khách hàng hiện có qua `api/v1/customer-users`.
4. `FoodStreetApiDataService` đọc cấu hình API từ `appsettings.json` hoặc file override `.android-settings/appsettings.json`.
5. App gọi `api/v1/bootstrap?customerUserId=...&languageCode=...` để lấy dữ liệu theo khách hàng và ngôn ngữ hiện tại.
6. `HomeMapPage` hiển thị bản đồ OpenStreetMap/Leaflet trong WebView, gồm marker, heat point, popup và bottom sheet chi tiết POI.
7. Khi khách chọn POI, app resolve tên, mô tả, món ăn, khuyến mãi, tag, ảnh, audio và thuyết minh theo ngôn ngữ đang chọn.
8. `MyTourPage` lưu POI đã chọn bằng `PoiTourStoreService` vào file local `vkfood.saved-pois.json`.
9. `QrScannerPage` dùng ZXing để quét QR, sau đó điều hướng về luồng chọn ngôn ngữ hoặc mở POI tương ứng.
10. `SettingsPage` cho phép đổi ngôn ngữ, cập nhật hồ sơ, bật/tắt auto narration, mua premium và đăng xuất.

## Luồng đa ngôn ngữ hiện tại

1. Mobile UI có file resource trong `apps/mobile-app/Resources/Localization` cho `vi`, `en`, `zh-CN`, `ko`, `ja` và `fr`.
2. Luồng nội dung khách hàng/premium trên backend hiện chuẩn hóa chính cho `vi`, `en`, `zh-CN`, `ko` và `ja`.
3. Ngôn ngữ miễn phí: `vi`, `en`.
4. Ngôn ngữ premium: `zh-CN`, `ko`, `ja`.
5. Khi đổi ngôn ngữ, `AppLanguageService` lưu lựa chọn, cập nhật `CultureInfo`, phát sự kiện `LanguageChanged` và các ViewModel kế thừa `LocalizedViewModelBase` reload lại state/binding.
6. `FoodStreetApiDataService` lắng nghe `LanguageChanged`, xóa bootstrap cache cũ và tải lại dữ liệu theo `languageCode` mới.
7. Backend `BootstrapLocalizationService` tự tạo bản dịch thiếu cho bootstrap khách hàng bằng Google Translate proxy khi ngôn ngữ đích là `en`, `zh-CN`, `ko` hoặc `ja` và khách có quyền dùng ngôn ngữ đó.
8. `LocalizationFallbackPolicy` ở cả backend và mobile chặn việc dùng text tiếng Việt hoặc text bị lỗi mã hóa làm fallback cho ngôn ngữ khác.
9. Fallback chỉ áp dụng theo từng trường hoặc từng key. Nếu thiếu một trường dịch, hệ thống ưu tiên bản dịch cùng ngôn ngữ, sau đó mới xét tiếng Anh; không kéo cả màn hình quay về tiếng Việt.

## Luồng thuyết minh và TTS

1. Khi cần phát thuyết minh, admin web và mobile gọi `api/v1/pois/{poiId}/narration?languageCode=...&voiceType=...`.
2. `PoiNarrationService` resolve nội dung theo thứ tự:
   - bản dịch đúng ngôn ngữ đã lưu;
   - bản dịch tự động từ nguồn phù hợp nếu bản dịch đích thiếu hoặc quá cũ;
   - fallback tối thiểu nếu không có nội dung đủ an toàn.
3. Nếu có audio guide đã upload, trạng thái `ready`, đúng ngôn ngữ và URL hợp lệ, hệ thống ưu tiên phát file audio đó.
4. Nếu không có audio upload hợp lệ, frontend tạo các URL `api/v1/tts` theo từng đoạn ngắn để backend gọi ElevenLabs Text-to-Speech.
5. Backend TTS dùng `ElevenLabsTextToSpeechService`, model mặc định `eleven_flash_v2_5`, output mặc định `mp3_44100_128` và cache audio bằng `MemoryCache`.
6. Admin web phát TTS bằng blob playback queue để không phải tải toàn bộ đoạn thuyết minh trước khi phát. Nếu audio web bị chặn hoặc lỗi, admin web fallback sang `speechSynthesis` của trình duyệt.
7. Mobile app phát audio remote bằng `Plugin.Maui.Audio`; nếu remote audio hoặc proxy TTS lỗi/quá lâu, app fallback sang `TextToSpeech.Default` của thiết bị theo locale đã resolve.

## Luồng premium

1. Backend định nghĩa ngôn ngữ miễn phí và premium trong `PremiumAccessCatalog`.
2. Khách hàng miễn phí chỉ dùng `vi` và `en`.
3. Khi khách chọn `zh-CN`, `ko` hoặc `ja`, mobile kiểm tra quyền ngôn ngữ từ bootstrap.
4. Nếu bị khóa, mobile điều hướng tới `PremiumCheckoutPage`.
5. API mua premium là `api/v1/customer-users/{id}/premium/purchase`.
6. Luồng thanh toán hiện là demo/mock qua `MockPremiumPaymentProcessor`, hỗ trợ bank card và e-wallet trong giao diện mobile.
7. Sau khi mua thành công, backend cập nhật trạng thái premium của customer, mobile xóa cache bootstrap và kiểm tra lại ngôn ngữ đang chọn.

## Endpoint chính

| Endpoint | Mục đích |
| --- | --- |
| `GET /api/v1/bootstrap` | Trả bootstrap cho admin web hoặc mobile app |
| `GET /api/v1/sync-state` | Trả version dữ liệu để mobile biết khi nào cần refresh |
| `POST /api/v1/auth/login` | Đăng nhập admin/chủ quán |
| `GET /api/v1/auth/login-options` | Lấy tài khoản mẫu theo portal đăng nhập |
| `POST /api/v1/customer-users` | Đăng ký khách hàng |
| `POST /api/v1/customer-users/login` | Đăng nhập khách hàng |
| `PUT /api/v1/customer-users/{id}/profile` | Cập nhật hồ sơ khách hàng |
| `POST /api/v1/customer-users/{id}/premium/purchase` | Mua premium demo |
| `GET /api/v1/pois/{id}/detail` | Lấy chi tiết POI cho admin |
| `GET /api/v1/pois/{id}/narration` | Resolve thuyết minh POI theo ngôn ngữ |
| `GET /api/v1/tts` | Tạo audio TTS qua ElevenLabs |
| `GET /api/v1/geocoding/*` | Tìm kiếm hoặc reverse geocode địa chỉ |
| `POST /api/v1/storage/upload` | Upload file media/audio |

## Cấu hình quan trọng

| File hoặc biến môi trường | Ý nghĩa |
| --- | --- |
| `apps/admin-web/.env.example` | Cấu hình `VITE_API_BASE_URL` và `VITE_API_PROXY_TARGET` cho admin web |
| `apps/backend-api/appsettings.json` | CORS, SQL Server, seed path, cấu hình mặc định ElevenLabs |
| `apps/backend-api/appsettings.Development.json` | Connection string SQL Server local |
| `apps/mobile-app/Resources/Raw/appsettings.json` | API base URL mặc định của mobile |
| `.android-settings/appsettings.json` | File override local cho Android/emulator hoặc điện thoại thật |
| `ELEVENLABS_API_KEY` | API key bắt buộc để backend tạo TTS thật |
| `ELEVENLABS_DEFAULT_VOICE_ID` | Voice ID mặc định cho ElevenLabs |
| `ELEVENLABS_MODEL_ID` | Model TTS, mặc định `eleven_flash_v2_5` |
| `ELEVENLABS_OUTPUT_FORMAT` | Output audio, mặc định `mp3_44100_128` |
| `VK_BACKEND_URLS` | URL backend khi muốn listen trên LAN, ví dụ `http://0.0.0.0:5080` |

Khi chạy mobile trên emulator hoặc điện thoại thật, không dùng `localhost` nếu backend đang chạy trên máy tính. Hãy tạo `.android-settings/appsettings.json` từ `apps/mobile-app/appsettings.android.sample.json` và đổi API base URL sang IP LAN của máy chạy backend, ví dụ `http://192.168.1.10:5080`.

## Cách chạy local

Mở terminal tại root repo:

```powershell
cd D:\vinh-khanh-food
```

Cài dependency cho admin web:

```powershell
npm.cmd run install:admin
```

Chạy backend API:

```powershell
npm.cmd run dev:backend
```

Backend mặc định chạy tại:

- `http://localhost:5080`
- `http://localhost:5080/swagger`

Chạy admin web:

```powershell
npm.cmd run dev
```

Admin web mặc định chạy tại:

- `http://localhost:5173`

Chạy mobile Android watcher:

```powershell
npm.cmd run dev:mobile:android
```

Chạy backend, admin web và mobile watcher cùng lúc:

```powershell
npm.cmd run dev:all
```

Lệnh `dev:all` mở backend, admin web và watcher deploy Android trong cùng một terminal. Nhấn `Ctrl+C` để dừng toàn bộ.

## Build, lint và smoke test

Build admin web:

```powershell
npm.cmd run build
```

Lint/typecheck admin web:

```powershell
npm.cmd run lint
```

Build backend:

```powershell
npm.cmd run build:backend
```

Smoke test fallback đa ngôn ngữ:

```powershell
dotnet run --project tools\LocalizationFallbackSmoke\LocalizationFallbackSmoke.csproj
```

Smoke test TTS:

```powershell
dotnet run --project tools\TtsPlaybackSmoke\TtsPlaybackSmoke.csproj
```

Smoke test chuẩn hóa địa chỉ POI:

```powershell
dotnet run --project tools\PoiAddressNormalizationSmoke\PoiAddressNormalizationSmoke.csproj
```

## Tài khoản mẫu

Nếu database đang dùng seed mặc định, có thể đăng nhập admin web bằng:

- `superadmin@vinhkhanh.vn` / `Admin@123`
- `bbq@vinhkhanh.vn` / `Admin@123`
- `oc@vinhkhanh.vn` / `Admin@123`

## Entry point nên đọc khi bảo trì

| Phần | File |
| --- | --- |
| Router admin | `apps/admin-web/src/app/router.tsx` |
| State admin | `apps/admin-web/src/data/store.tsx` |
| API client admin | `apps/admin-web/src/lib/api.ts` |
| TTS/narration admin | `apps/admin-web/src/lib/narration.ts` |
| Playback POI admin | `apps/admin-web/src/features/pois/usePoiNarrationPlayback.ts` |
| Map POI admin | `apps/admin-web/src/features/pois/OpenStreetMapPicker.tsx` |
| Backend startup | `apps/backend-api/Program.cs` |
| Bootstrap backend | `apps/backend-api/Controllers/BootstrapController.cs` |
| POI/narration backend | `apps/backend-api/Controllers/PoisController.cs` và `apps/backend-api/Infrastructure/PoiNarrationService.cs` |
| TTS backend | `apps/backend-api/Controllers/TtsController.cs` và `apps/backend-api/Infrastructure/ElevenLabsTextToSpeechService.cs` |
| Premium backend | `apps/backend-api/Infrastructure/PremiumAccessCatalog.cs` và `apps/backend-api/Infrastructure/PremiumPurchaseService.cs` |
| Mobile startup | `apps/mobile-app/App.xaml.cs` và `apps/mobile-app/MauiProgram.cs` |
| Mobile language | `apps/mobile-app/Services/AppLanguageService.cs` |
| Mobile data | `apps/mobile-app/Services/FoodStreetMockDataService.cs` và `apps/mobile-app/Services/FoodStreetApiDataService.Profile.cs` |
| Mobile POI map | `apps/mobile-app/Pages/HomeMapPage.xaml.cs` |
| Mobile POI/TTS | `apps/mobile-app/Services/PoiExperienceServices.cs` |
