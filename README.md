# Vinh Khanh Food — Tổng quan dự án

Repository này là monorepo chứa toàn bộ mã nguồn cho đồ án Vinh Khanh Food: Admin web (quản trị), Backend API (ASP.NET Core) và Mobile app (MAUI). Tài liệu này tóm tắt kiến trúc, cấu trúc thư mục, cách chạy và các điểm cần biết khi phát triển.

## Nội dung chính
- Admin web: `apps/admin-web` (React + TypeScript + Vite) — giao diện quản trị nội dung, POI, tour, media và người dùng.
- Backend API: `apps/backend-api` (ASP.NET Core, .NET 10) — nguồn dữ liệu trung tâm, authentication, TTS proxy, geocoding, premium.
- Mobile app: `apps/mobile-app` (.NET MAUI) — trải nghiệm khách hàng trên Android (bản đồ, POI, thuyết minh, tour, QR).
- Tools: `tools` — các console app để chạy smoke tests (TTS, localization, POI normalization).
- Scripts: `scripts` — tiện ích để chạy môi trường dev, khởi động nhiều service cùng lúc.
- `sql/` — script DB hỗ trợ seed/patch.

## Cấu trúc thư mục (chính)

- `apps/admin-web/` — frontend quản trị (Vite, Tailwind, React).
- `apps/backend-api/` — ASP.NET Core API (Program.cs, Controllers, Infrastructure).
- `apps/mobile-app/` — .NET MAUI mobile app (Android focus).
- `tools/` — dự án nhỏ để kiểm tra TTS, localization, POI normalization.
- `scripts/` — lệnh tiện ích (PowerShell/Node) để chạy dev environment.
- `sql/` — các script DB hỗ trợ seed/patch.

## Yêu cầu môi trường (Local)

- Windows (phát triển MAUI) với Visual Studio + MAUI workload.
- .NET 10 SDK (để build backend & mobile).
- Node.js (LTS) và npm/yarn (cho admin-web và scripts).
- Docker (tuỳ chọn, để chạy docker-compose cho admin/nginx).

## Chạy nhanh trên máy dev

Tại root repo (PowerShell trên Windows):

```powershell
cd D:\vinh-khanh-food
```

1) Cài dependency cho admin web và chạy dev server

```powershell
cd apps\admin-web
npm install
npm run dev
```

Admin web mặc định chạy trên `http://localhost:5173`.

2) Chạy Backend API (local)

```powershell
cd D:\vinh-khanh-food
dotnet run --project apps\backend-api\VinhKhanh.BackendApi.csproj
```

Backend mặc định lắng nghe `http://localhost:5080` (kiểm tra Swagger tại `/swagger`).

3) Chạy Mobile (Android emulator)

- Mở Visual Studio (MAUI) hoặc dùng CLI để chạy trên emulator/thiết bị.
- Nếu backend chạy trên máy dev và mobile chạy trên emulator, tạo file override:

```text
Copy apps/mobile-app/appsettings.android.sample.json -> .android-settings/appsettings.json
Sửa API base URL sang IP máy dev (ví dụ http://192.168.1.10:5080)
```

4) Chạy toàn bộ môi trường (script tiện ích)

```powershell
scripts\dev-backend.cmd        # chạy backend trên Windows
node scripts\dev-all.cjs      # chạy nhiều service cùng lúc (nếu có)
```

Hoặc dùng docker-compose cho admin/nginx:

```powershell
docker compose -f apps\admin-web\docker-compose.yml up --build
```

## Luồng chính của hệ thống

Dưới đây là các luồng chính (flow) giúp hiểu cách các thành phần tương tác trong hệ thống.

- Luồng Admin Web (quản trị):
	1. Admin đăng nhập tại `/admin/login` hoặc `/restaurant/login`.
	2. Frontend gọi `POST /api/v1/auth/login` để lấy access/refresh token và roles.
	3. Sau login, AdminDataProvider gọi `GET /api/v1/bootstrap` để nạp dữ liệu khởi tạo.
	4. Khi tạo/cập nhật POI, media, tour hoặc translation, frontend gọi các endpoint tương ứng (`/api/v1/pois`, `/api/v1/media-assets`, `/api/v1/translations`, ...).
	5. Backend kiểm tra quyền theo `AdminRequestContextResolver`; nếu thành công thì lưu và trả về kết quả để frontend cập nhật state hoặc refresh bootstrap.

- Luồng Backend (API & dữ liệu):
	1. Backend nhận yêu cầu từ admin hoặc mobile.
	2. Với các yêu cầu nội dung, backend truy vấn DB (POIs, Translations, Media, AudioGuides) và resolve dữ liệu theo `languageCode` và quyền premium.
	3. Nếu cần tts/audio, backend dùng `ElevenLabsTextToSpeechService` hoặc trả URL audio đã upload.
	4. Các proxy (geocoding, translate) được gọi khi backend cần normalize address hoặc auto-translate nội dung thiếu.

- Luồng Mobile (khách hàng):
	1. App khởi động, `AppLanguageService` load language từ preferences.
	2. App gọi `GET /api/v1/bootstrap?customerUserId=...&languageCode=...` để lấy dữ liệu theo user và ngôn ngữ.
	3. HomeMapPage hiển thị POI trên bản đồ (WebView + Leaflet tiles). Khi chọn POI, app hiển thị popup/detail và cung cấp nút phát thuyết minh.
	4. Nếu audio có sẵn (uploaded), app phát trực tiếp; nếu không, app gọi endpoint TTS backend để nhận/stream audio.
	5. Các tương tác như lưu tour, quét QR, mua premium được chuyển thành các request tương ứng đến backend.

- Luồng Localization / Bootstrap:
	1. Bootstrap service trả payload đã chuẩn hóa gồm các bản dịch, media và audio metadata.
	2. Nếu trường dịch thiếu, `BootstrapLocalizationService` có thể gọi Google Translate proxy để sinh bản dịch tạm thời (chỉ khi khách có quyền ngôn ngữ đó).
	3. Mobile và admin dùng policy `LocalizationFallbackPolicy` để quyết định fallback theo từng field.

- Luồng Narration / TTS:
	1. Khi cần narration, frontend gọi `GET /api/v1/pois/{id}/narration?languageCode=...&voiceType=...`.
	2. `PoiNarrationService` resolve nội dung theo thứ tự: uploaded audio (ready) → stored translation → auto-translate → fallback.
	3. Nếu dùng TTS, backend chia text thành đoạn nhỏ, gọi `GET /api/v1/tts` (proxy ElevenLabs), cache kết quả, và trả URL/stream để client phát.

- Luồng Premium (quyền ngôn ngữ / nội dung):
	1. Backend quản lý catalog ngôn ngữ premium (`PremiumAccessCatalog`).
	2. Khi client yêu cầu nội dung premium (ví dụ `zh-CN`), bootstrap trả flag quyền; nếu chưa mua, mobile chuyển sang `PremiumCheckout`.
	3. Sau purchase (mock/demo hoặc thực), backend cập nhật trạng thái premium của user và client xóa cache/bootstrap để tải lại.

Các luồng này là bản tóm tắt; nếu bạn muốn tôi mở rộng bằng sequence diagram, sơ đồ tuần tự, hoặc thêm các endpoint chi tiết cho từng bước, tôi có thể bổ sung.

## Build & kiểm tra

- Build solution:

```powershell
dotnet build vinh-khanh-food.sln -c Release
```

- Build admin web production:

```powershell
cd apps\admin-web
npm run build
```

- Chạy smoke tests:

```powershell
dotnet run --project tools\LocalizationFallbackSmoke\LocalizationFallbackSmoke.csproj
dotnet run --project tools\TtsPlaybackSmoke\TtsPlaybackSmoke.csproj
dotnet run --project tools\PoiAddressNormalizationSmoke\PoiAddressNormalizationSmoke.csproj
```

## Cấu hình & biến môi trường quan trọng

- `apps/backend-api/appsettings.json` và `apps/backend-api/appsettings.Development.json` — connection string, CORS, cấu hình TTS.
- `.android-settings/appsettings.json` — override cấu hình mobile chạy trên emulator/thiết bị.
- `ELEVENLABS_API_KEY` — key dùng bởi backend để tạo TTS (bảo mật tại env/secrets).

## Điểm bắt đầu khi bảo trì (entry points)

- Backend: [apps/backend-api/Program.cs](apps/backend-api/Program.cs)
- Bootstrap API: [apps/backend-api/Controllers/BootstrapController.cs](apps/backend-api/Controllers/BootstrapController.cs)
- POI/Narration: [apps/backend-api/Controllers/PoisController.cs](apps/backend-api/Controllers/PoisController.cs)
- TTS service: [apps/backend-api/Infrastructure/ElevenLabsTextToSpeechService.cs](apps/backend-api/Infrastructure/ElevenLabsTextToSpeechService.cs)
- Admin router/state: [apps/admin-web/src/app/router.tsx](apps/admin-web/src/app/router.tsx), [apps/admin-web/src/data/store.tsx](apps/admin-web/src/data/store.tsx)
- Mobile entry: [apps/mobile-app/App.xaml.cs](apps/mobile-app/App.xaml.cs), [apps/mobile-app/MauiProgram.cs](apps/mobile-app/MauiProgram.cs)

---

Nếu bạn muốn tôi mở rộng phần nào (ví dụ: hướng dẫn cấu hình ElevenLabs, cách build CI/CD, hoặc kịch bản dev-all chi tiết), cho biết mục tiêu cụ thể để tôi bổ sung.
