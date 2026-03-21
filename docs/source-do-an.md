# Source của đồ án

## 1. Workspace

Source chính nằm trong workspace:

`C:\Users\ADMIN\OneDrive\Tài liệu\Playground 2`

Cấu trúc gốc:

```text
Playground 2/
|-- apps/
|   |-- admin-web/
|   `-- backend-api/
|-- docs/
|-- package.json
|-- Playground 2.sln
|-- NuGet.Config
`-- README.md
```

Ý nghĩa:

- `apps/admin-web`: frontend quản trị React + Vite
- `apps/backend-api`: backend ASP.NET Core Web API
- `docs/`: tài liệu luồng và mô tả source
- `package.json`: script chạy frontend/backend

## 2. Source frontend

Thư mục:

`apps/admin-web`

### 2.1 Cấu trúc chính

```text
apps/admin-web/
|-- src/
|   |-- app/
|   |-- components/
|   |-- data/
|   |-- features/
|   |-- lib/
|   |-- styles/
|   `-- main.tsx
|-- index.html
|-- package.json
`-- vite.config.ts
```

### 2.2 Các file frontend quan trọng nhất

#### `src/app`

- `App.tsx`
  - bọc `AdminDataProvider`
  - bọc `AuthProvider`
  - chặn router cho tới khi bootstrap từ backend hoàn tất
- `router.tsx`
  - định nghĩa route admin

#### `src/data`

- `store.tsx`
  - state dùng chung của admin web
  - gọi `GET /api/v1/bootstrap` khi khởi động
  - các hàm `save*` giờ gọi API backend rồi refresh bootstrap
- `types.ts`
  - định nghĩa shape dữ liệu frontend theo contract backend
- `sql/admin-seed.sql`
  - file dữ liệu đang được backend persist ngược về

Lưu ý quan trọng:

- frontend không còn dùng `localStorage` để giữ `places`, `users`, `promotions`, `settings`, `qrCodes`, `foodItems`, `audioGuides`
- `localStorage` chỉ còn giữ session đăng nhập admin

#### `src/lib`

- `api.ts`
  - nơi gom toàn bộ lệnh gọi backend
  - có `getBootstrap`, `login`, `logout`, `uploadFile`
  - có các hàm CRUD như `savePlace`, `saveUser`, `savePromotion`, `saveAudioGuide`, `saveFoodItem`, `saveQrCodeImage`, ...
- `selectors.ts`
  - helper chọn dữ liệu từ state bootstrap
- `utils.ts`
  - helper format, slug, label

#### `src/components`

- `layout/`
  - `AppShell.tsx`
  - `Sidebar.tsx`
  - `Topbar.tsx`
- `ui/`
  - `Button.tsx`
  - `Card.tsx`
  - `DataTable.tsx`
  - `ImageSourceField.tsx`
  - `Input.tsx`
  - `Modal.tsx`
  - `Select.tsx`
  - `StatusBadge.tsx`

`ImageSourceField.tsx` hiện đã đổi sang upload file qua backend storage, không đọc file thành `data:` URL để lưu cục bộ nữa.

#### `src/features`

Các màn nghiệp vụ chính:

- `auth/`
  - `AuthContext.tsx`
  - `LoginPage.tsx`
  - `RequireAuth.tsx`
- `places/`
  - `PlacesPage.tsx`
  - `OpenStreetMapPicker.tsx`
- `content/`
  - `ContentPage.tsx`
- `media/`
  - `MediaPage.tsx`
  - `useNarrationPreview.ts`
- `qr/`
  - `QrRoutesPage.tsx`
- `users/`
  - `UsersPage.tsx`
- `promotions/`
  - `PromotionsPage.tsx`
- `reviews/`
  - `ReviewsPage.tsx`
- `settings/`
  - `SettingsPage.tsx`
- `dashboard/`
  - `DashboardPage.tsx`
- `activity/`
  - `ActivityPage.tsx`

### 2.3 Những file frontend gắn trực tiếp với luồng hiện tại

| File | Vai trò |
|---|---|
| `src/app/App.tsx` | Chờ bootstrap xong mới cho app render |
| `src/data/store.tsx` | Nguồn state frontend, gọi bootstrap và CRUD API |
| `src/lib/api.ts` | Tầng HTTP client gọi backend |
| `src/features/auth/AuthContext.tsx` | Login/logout và restore session |
| `src/features/content/ContentPage.tsx` | Food CRUD + upload ảnh thật |
| `src/features/media/MediaPage.tsx` | Audio CRUD + upload MP3 thật |
| `src/features/qr/QrRoutesPage.tsx` | QR image/state update + upload ảnh thật |
| `src/features/settings/SettingsPage.tsx` | Lưu settings qua backend và reload bootstrap |
| `vite.config.ts` | Proxy `/api` và `/storage` về backend khi chạy dev |

## 3. Source backend

Thư mục:

`apps/backend-api`

### 3.1 Cấu trúc chính

```text
apps/backend-api/
|-- Controllers/
|-- Contracts/
|-- Infrastructure/
|-- Models/
|-- Program.cs
|-- appsettings.json
`-- VinhKhanh.BackendApi.csproj
```

### 3.2 Các file backend quan trọng nhất

#### `Program.cs`

- đăng ký `AdminDataRepository`
- đăng ký `StorageService`
- bật CORS cho admin web
- bật `UseStaticFiles()` để phục vụ `/storage/...`
- map controllers

#### `Contracts/ApiResponse.cs`

Chứa:

- `ApiResponse<T>`
- `AuthTokensResponse`
- các request/response contract cho places, users, promotions, reviews, settings, QR
- `StoredFileResponse` cho upload

#### `Models/Entities.cs`

Định nghĩa entity backend:

- `AdminUser`
- `Place`
- `Translation`
- `AudioGuide`
- `FoodItem`
- `QRCodeRecord`
- `Promotion`
- `Review`
- `SystemSetting`
- các log và entity phụ trợ khác

#### `Infrastructure/AdminDataRepository.cs`

Đây là nơi backend đang giữ nguồn dữ liệu chuẩn cho admin:

- đọc dữ liệu từ `admin-seed.sql`
- trả bootstrap cho frontend
- xử lý login/refesh/logout
- xử lý save/update/delete cho từng entity
- append audit log
- persist lại dữ liệu backend sau mỗi thay đổi

Quan trọng:

- frontend không tự ghi state vào trình duyệt nữa
- dữ liệu chuẩn do `AdminDataRepository` quyết định

#### `Infrastructure/StorageService.cs`

Chịu trách nhiệm:

- nhận `IFormFile`
- chuẩn hóa folder upload
- lưu file vào `wwwroot/storage/...`
- trả URL `/storage/...`

### 3.3 Controllers chính

| Controller | Endpoint chính | Trách nhiệm |
|---|---|---|
| `BootstrapController.cs` | `GET /api/v1/bootstrap` | Trả bootstrap state cho frontend |
| `AuthController.cs` | `POST /api/v1/auth/login` | Đăng nhập admin |
| `PlacesController.cs` | `/api/v1/places` | CRUD place |
| `UsersController.cs` | `/api/v1/users` | Create/update admin user |
| `PromotionsController.cs` | `/api/v1/promotions` | CRUD promotions |
| `ReviewsController.cs` | `/api/v1/reviews/{id}/status` | Duyệt review |
| `SettingsController.cs` | `/api/v1/settings` | Update system settings |
| `FoodItemsController.cs` | `/api/v1/food-items` | CRUD food items |
| `AudioGuidesController.cs` | `/api/v1/audio-guides` | CRUD audio guides |
| `TranslationsController.cs` | `/api/v1/translations` | CRUD translations |
| `QrRoutesController.cs` | `/api/v1/qr-codes/...` | Bật/tắt QR, cập nhật ảnh QR, route CRUD |
| `StorageController.cs` | `POST /api/v1/storage/upload` | Upload file thật |
| `ActivityController.cs` | `GET /api/v1/activity/audit-logs` | Audit trail |

## 4. Quan hệ giữa frontend và backend

Luồng source hiện tại:

```text
UI page
-> useAdminData()
-> src/lib/api.ts
-> backend controller tương ứng
-> AdminDataRepository / StorageService
-> dữ liệu persisted ở backend
-> GET /api/v1/bootstrap
-> state React mới
```

Điểm khác với trước:

- không còn `seed.ts -> localStorage -> state` là luồng chính
- không còn auth frontend tự kiểm tra password trên `state.users`
- không còn upload MP3/QR/image thành `data:` URL để lưu tại chỗ

## 5. Những file cần xem đầu tiên nếu muốn hiểu hệ thống

Nếu muốn hiểu nhanh source hiện tại, nên mở theo thứ tự:

1. `apps/admin-web/src/app/App.tsx`
2. `apps/admin-web/src/data/store.tsx`
3. `apps/admin-web/src/lib/api.ts`
4. `apps/admin-web/src/features/auth/AuthContext.tsx`
5. `apps/backend-api/Program.cs`
6. `apps/backend-api/Contracts/ApiResponse.cs`
7. `apps/backend-api/Controllers/BootstrapController.cs`
8. `apps/backend-api/Controllers/AuthController.cs`
9. `apps/backend-api/Infrastructure/AdminDataRepository.cs`
10. `apps/backend-api/Infrastructure/StorageService.cs`

## 6. Tóm tắt một câu

Source hiện tại đã được chỉnh để frontend vận hành bằng bootstrap + CRUD API + upload thật qua backend storage, còn backend giữ vai trò nguồn dữ liệu duy nhất cho admin web.
