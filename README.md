# Vinh Khanh Food

README này là tài liệu tổng hợp duy nhất cho toàn bộ đồ án. Repository hiện tại gồm 3 phần chính:

- `apps/admin-web`: trang quản trị viết bằng React + TypeScript + Vite, dành cho `SUPER_ADMIN` và `PLACE_OWNER`
- `apps/backend-api`: Web API viết bằng ASP.NET Core, làm trung tâm xử lý dữ liệu và kết nối SQL Server
- `apps/mobile-app`: ứng dụng .NET MAUI Android dành cho người dùng cuối

## Các lỗi đang phát sinh
- Lỗi map hiện thành phố Thủ Đức, không hiện TPHCM
- Chưa dịch được triệt để ngôn ngữ. (vẫn còn lẫn lộn ngôn ngữ)
- Quản lý tour chưa hợp lí
- Chưa xử lí backend đăng nhập bằng google, facebook
- Cần thêm chatbox AI cho app (xài API key của Gemini)
- Lỗi khi trạng thái người dùng là không hoạt động và đã ban thì vẫn đăng nhập bình thường
## Tổng quan kiến trúc

```text
Admin Web --------------------> Backend API --------------------> SQL Server
   |                                  |
   |                                  +--> Swagger
   |                                  +--> Upload file
   |                                  +--> Geocoding / Translation proxy
   |
   +-- đăng nhập, bootstrap, CRUD

Mobile App -------------------> Backend API
   |
   +-- bản đồ, POI detail, audio, My Tour, QR
   +-- nếu API chưa sẵn sàng thì dùng fallback mock data để demo giao diện
```

## Cấu trúc repository

- `apps/admin-web`: giao diện quản trị, phân quyền theo vai trò và quản lý nội dung
- `apps/backend-api`: API trung tâm, trả bootstrap dữ liệu, xác thực, CRUD, upload và nhật ký hệ thống
- `apps/mobile-app`: ứng dụng Android cho khách, hiển thị bản đồ, điểm đến, tour và audio
- `scripts`: các script hỗ trợ chạy backend và theo dõi/deploy mobile Android
- `vinh-khanh-food.sln`: solution dùng để mở backend API và mobile app trong Visual Studio

## Vai trò và cổng truy cập

| Vai trò | Cổng truy cập | Mục đích |
| --- | --- | --- |
| `SUPER_ADMIN` | `/admin/*` | Quản lý toàn bộ hệ thống, người dùng, nội dung, khuyến mãi, đánh giá, cấu hình |
| `PLACE_OWNER` | `/restaurant/*` | Quản lý dữ liệu liên quan đến POI hoặc quán mà chủ quán được phân công |
| End user | Ứng dụng mobile | Xem điểm đến, nghe audio, lưu tour, quét QR, đổi ngôn ngữ |

## Luồng tổng thể của đồ án

1. Quản trị viên hoặc chủ quán đăng nhập trên trang admin.
2. Admin web gọi `api/v1/auth/login`, lưu phiên đăng nhập, sau đó gọi `api/v1/bootstrap`.
3. Backend trả về bộ dữ liệu tổng hợp gồm users, customer users, categories, POIs, food items, translations, audio guides, media assets, routes, promotions, reviews, logs và settings.
4. Quản trị viên cập nhật nội dung trên các module như POI, tour, món ăn, media, audio, đánh giá, khuyến mãi, người dùng và cấu hình hệ thống.
5. Backend ghi dữ liệu xuống SQL Server và tạo bộ bootstrap mới cho frontend và mobile app sử dụng.
6. Mobile app đọc dữ liệu từ `api/v1/bootstrap` để tạo bản đồ, heatmap, chi tiết POI, hình ảnh, review, audio và thông tin tour.
7. Người dùng trên app xem nội dung, nghe thuyết minh, quét QR và lưu điểm đến vào hành trình cá nhân.
8. Các đánh giá và log tương tác được gửi ngược lại backend để admin theo dõi trên dashboard và activity.

## Luồng của trang admin

1. Người dùng truy cập `/login`.
2. Router điều hướng theo vai trò:
   - `SUPER_ADMIN` vào `/admin/dashboard`
   - `PLACE_OWNER` vào `/restaurant/dashboard`
3. `AdminDataProvider` tải bootstrap ban đầu và đồng bộ state cho toàn bộ hệ thống quản trị.
4. Các module chính của admin gồm:
   - `Dashboard`: tổng quan số liệu
   - `POI`: quản lý địa điểm, tọa độ, geocoding và nội dung chi tiết
   - `Tours`: quản lý tuyến tham quan và các điểm dừng
   - `Content`: quản lý món ăn hoặc nội dung liên quan đến POI
   - `Users`: quản lý tài khoản admin và chủ quán
   - `End Users`: theo dõi người dùng cuối
   - `Promotions`: tạo và cập nhật ưu đãi
   - `Reviews`: duyệt hoặc thay đổi trạng thái đánh giá
   - `Activity`: xem audit log
   - `Settings`: cấu hình chung của hệ thống
5. Mỗi thao tác tạo hoặc cập nhật trên admin sẽ gọi API tương ứng như `pois`, `tours`, `food-items`, `translations`, `audio-guides`, `media-assets`, `promotions`, `reviews`, `settings`.
6. Sau khi lưu, frontend sẽ refresh bootstrap để giao diện đồng bộ lại dữ liệu mới nhất.
7. Nếu đăng nhập bằng vai trò `PLACE_OWNER`, backend và frontend đều giới hạn dữ liệu theo POI mà chủ quán phụ trách.

## Luồng của ứng dụng mobile

1. Ứng dụng khởi động tại `LoginPage`.
2. Người dùng có thể vào `LanguageSelectionPage` để chọn ngôn ngữ.
3. Luồng đăng nhập hiện tại là mock flow phục vụ demo giao diện; sau khi tiếp tục, app chuyển vào `HomeMapPage`.
4. `HomeMapPage` hiển thị danh sách POI, heatmap, bottom sheet chi tiết, hình ảnh, rating và audio narration.
5. Khi app có cấu hình API hợp lệ trong `apps/mobile-app/Resources/Raw/appsettings.json`, dữ liệu sẽ được nạp từ backend thông qua `api/v1/bootstrap`.
6. Nếu backend chưa sẵn sàng, mobile app sẽ fallback sang mock data để vẫn có thể demo giao diện và luồng sử dụng.
7. Từ `HomeMapPage`, người dùng có thể chọn POI trên bản đồ, nghe narration/audio, mở chỉ đường và lưu POI vào `MyTourPage`.
8. `QrScannerPage` quét mã QR, chuyển sang `LanguageSelectionPage`, sau đó mở đúng POI tương ứng trên `HomeMapPage`.
9. `MyTourPage` hiển thị hành trình đã lưu và tiến độ tham quan.
10. `SettingsPage` hiển thị hồ sơ, cho phép đổi ngôn ngữ và đăng xuất.

## Luồng dữ liệu từ admin sang app

1. Admin tạo hoặc cập nhật POI, món ăn, media, bản dịch, audio guide, route, trạng thái review và settings.
2. Backend tổng hợp toàn bộ dữ liệu này trong `api/v1/bootstrap`.
3. Mobile app biến đổi bootstrap thành:
   - danh sách POI hiển thị trên bản đồ
   - heatmap mức độ quan tâm
   - chi tiết POI theo ngôn ngữ
   - audio narration sẵn sàng để phát
   - tour và thông tin hồ sơ người dùng
4. Nhờ luồng này, khi admin thay đổi nội dung trên web thì mobile app có thể đọc lại và hiển thị nội dung mới.

## Công nghệ sử dụng

- Admin web: React 19, TypeScript, Vite, Tailwind CSS, React Router, Recharts, Leaflet/OpenStreetMap
- Backend: ASP.NET Core `.NET 10`, SQL Server, Swagger
- Mobile app: .NET MAUI `net10.0-android`, ZXing QR Scanner, Plugin.Maui.Audio

## Cấu hình cần lưu ý

- `apps/admin-web/.env.example`
  - `VITE_API_BASE_URL=http://localhost:5080/api/v1`
  - `VITE_API_PROXY_TARGET=http://localhost:5080`
- `apps/backend-api/appsettings.Development.json`
  - cấu hình `ConnectionStrings:AdminSqlServer`
- `apps/mobile-app/Resources/Raw/appsettings.json`
  - `ApiBaseUrl` cho môi trường local
  - `PlatformApiBaseUrls.Android` nên đổi thành IP của máy đang chạy backend trong cùng mạng hoặc emulator

## Google Translate TTS

- Audio guide đã upload vẫn được ưu tiên cho admin web và mobile app
- Khi không có audio sẵn, admin web và mobile app đều fallback sang Google Translate TTS từ cùng một nội dung narration đã resolve từ backend
- Backend `api/v1/pois/{id}/narration` tiếp tục đồng bộ text, ngôn ngữ hiệu lực và audio guide giữa admin và app

## Các lệnh root thường dùng

```powershell
npm run install:admin
npm run dev
npm run dev:all
.\scripts\dev-backend.cmd
npm run dev:mobile:android
npm run build
.\scripts\build-backend.cmd
npm run lint
```

Nếu dùng PowerShell và gặp lỗi `npm.ps1` do execution policy, hãy thay `npm` bằng `npm.cmd`.

## Cách chạy local

Mở đúng workspace trước khi chạy lệnh:

- VS Code: mở thư mục `D:\vinh-khanh-food` hoặc file workspace/solution của repo. Repo đã có `.vscode/settings.json` để terminal mặc định mở tại root workspace.
- Visual Studio: mở `vinh-khanh-food.sln` chứ không mở `vinh-khanh-food.slnLaunch`. File `.slnLaunch` chỉ là cấu hình profile chạy dùng chung cho solution.
- Khi chạy web trong Visual Studio, ưu tiên profile backend (`VinhKhanh.BackendApi` hoặc `https`). Backend đã được cấu hình SPA proxy để tự bật Vite dev server cho `apps/admin-web` tại `http://localhost:5173`.
- Nếu muốn chạy đa dự án trong Visual Studio, solution cũng có profile `Backend + Admin Web` trong file `vinh-khanh-food.slnLaunch`.

### 1. Yêu cầu môi trường

- Node.js + npm
- .NET SDK 10
- SQL Server hoặc SQL Server Express
- Android SDK + emulator nếu muốn chạy mobile app

### 2. Chạy backend API

```powershell
.\scripts\dev-backend.cmd
```

Hoặc dùng trực tiếp:

```powershell
node .\scripts\run-backend.cjs dev
```

Mặc định backend chạy tại:

- `http://localhost:5080`
- `http://localhost:5080/swagger`

### 3. Chạy admin web

```powershell
npm run install:admin
npm run dev
```

Mặc định admin web chạy tại:

- `http://localhost:5173`

### 4. Chạy mobile Android

```powershell
npm run dev:mobile:android
```

Script này sẽ build, cài lên emulator Android và tiếp tục watch để deploy lại khi file trong `apps/mobile-app` thay đổi.

### 5. Chạy đồng thời backend + admin web + mobile

```powershell
npm run dev:all
```

Lệnh này sẽ mở đồng thời backend, admin web và mobile watcher trong cùng một terminal. Nhấn `Ctrl+C` để dừng toàn bộ.

## Tài khoản mẫu

Nếu database đang dùng seed mặc định, có thể đăng nhập bằng:

- `superadmin@vinhkhanh.vn` / `Admin@123`
- `bbq@vinhkhanh.vn` / `Admin@123`
- `oc@vinhkhanh.vn` / `Admin@123`

## Các entry point nên đọc đầu tiên

- `apps/admin-web/src/app/router.tsx`
- `apps/admin-web/src/data/store.tsx`
- `apps/backend-api/Program.cs`
- `apps/backend-api/Infrastructure/AdminDataRepository.cs`
- `apps/mobile-app/App.xaml.cs`
- `apps/mobile-app/ViewModels/HomeMapViewModel.cs`
