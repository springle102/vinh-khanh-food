# Vĩnh Khánh Food Guide

Đồ án này xây dựng hệ thống thuyết minh đa ngôn ngữ cho Phố Ẩm Thực Vĩnh Khánh, gồm một trang quản trị để vận hành nội dung và một backend API để quản lý dữ liệu, xác thực, tải tệp lên và kết nối SQL Server.

## 1. Mục tiêu đồ án

Hệ thống phục vụ các bài toán chính:

- quản lý địa điểm, món ăn, QR, audio guide và nội dung thuyết minh
- hỗ trợ đa ngôn ngữ cho du khách
- quản lý tài khoản quản trị và phân quyền
- lưu trữ dữ liệu tập trung trên backend và SQL Server
- tải tệp thật qua backend storage thay vì lưu dữ liệu demo trong trình duyệt

## Các tính năng cần cải thiện
- thêm tính năng duyệt cho super admin
- sửa lỗi khi nhấn vào poi phải phát đươc audio

## 2. Công nghệ được sử dụng

### Frontend admin web

- `React 19` để xây dựng giao diện
- `TypeScript 5` để viết mã có kiểu dữ liệu rõ ràng
- `Vite 6` để chạy môi trường phát triển và build nhanh
- `React Router 7` để điều hướng các màn hình quản trị
- `Tailwind CSS 3`, `PostCSS`, `Autoprefixer` để xây dựng giao diện
- `Recharts` để hiển thị biểu đồ ở dashboard
- `Leaflet` để làm bản đồ, chọn vị trí và hiển thị marker
- `QRCode` để tạo và quản lý mã QR
- `clsx` để xử lý class name linh hoạt

### Backend API

- `ASP.NET Core Web API` trên `.NET 10`
- `C#` cho toàn bộ phần backend
- `Microsoft.Data.SqlClient` để kết nối và thao tác với SQL Server
- `Static Files middleware` để phục vụ tệp tải lên trong `wwwroot/storage`

### Cơ sở dữ liệu và hạ tầng

- `SQL Server` là nơi lưu dữ liệu chính
- file seed SQL Server nằm tại `apps/admin-web/src/data/sql/admin-seed-sqlserver.sql`
- backend có thể khởi tạo database và schema từ file seed này nếu database chưa có
- tệp tải lên được lưu trong `apps/backend-api/wwwroot/storage`

### Công cụ và script

- `npm` để chạy frontend
- `dotnet CLI` để chạy và build backend
- `PowerShell` để thao tác trong môi trường Windows

## 3. Kiến trúc tổng quan

Hệ thống hiện tại có 2 khối chính:

- `apps/admin-web`: giao diện quản trị
- `apps/backend-api`: Web API, storage và repository SQL Server

Luồng tổng quan:

```text
Admin Web
-> src/lib/api.ts
-> ASP.NET Core Controllers
-> AdminDataRepository / StorageService
-> SQL Server + wwwroot/storage
```

Backend là `source of truth` cho toàn bộ trạng thái quản trị. Frontend không còn lưu dữ liệu vận hành dài hạn trong `localStorage`; frontend chỉ giữ session đăng nhập.

## 4. Luồng hệ thống hiện tại

### 4.1 Luồng khởi động

```text
Trình duyệt mở admin web
-> App.tsx
-> AdminDataProvider mount
-> GET /api/v1/bootstrap
-> BootstrapController
-> AdminDataRepository.GetBootstrap()
-> SQL Server
-> frontend set state
-> router được render
```

Ý nghĩa:

- frontend nạp dữ liệu ban đầu từ backend
- state được đồng bộ theo response bootstrap
- ứng dụng chỉ render đầy đủ sau khi bootstrap xong

### 4.2 Luồng đăng nhập

```text
LoginPage
-> AuthContext.login()
-> POST /api/v1/auth/login
-> AuthController
-> AdminDataRepository.Login()
-> trả access token + refresh token
-> frontend lưu session
-> frontend refresh bootstrap
```

Ý nghĩa:

- đăng nhập đi qua backend, không kiểm tra mật khẩu ở frontend
- session vẫn nằm trong `localStorage`
- thông tin người dùng hiển thị trên app vẫn lấy từ bootstrap backend

### 4.3 Luồng CRUD các màn quản trị

Tất cả các hàm `save*` trong `apps/admin-web/src/data/store.tsx` đều theo một mẫu chung:

```text
UI form
-> save*
-> gọi API CRUD backend
-> backend ghi SQL Server
-> frontend gọi lại refreshData()
-> GET /api/v1/bootstrap
-> state mới nhất được đồng bộ
```

Áp dụng cho:

- places
- users
- promotions
- reviews
- settings
- food items
- audio guides
- translations
- qr/routes

### 4.4 Luồng tải tệp thật

```text
Người dùng chọn tệp
-> POST /api/v1/storage/upload
-> StorageController
-> StorageService.SaveAsync()
-> lưu vào wwwroot/storage
-> trả URL /storage/...
-> frontend lưu URL vào entity qua API CRUD
```

Hiện tại đã có các nhóm upload chính:

- `images/food-items`
- `audio/guides`
- `images/qr-codes`

## 5. Cấu trúc source

```text
vinh-khanh-food/
|-- apps/
|   |-- admin-web/
|   `-- backend-api/
|-- docs/
|-- package.json
|-- Playground 2.sln
|-- NuGet.Config
`-- README.md
```

## 6. Source frontend

Thư mục chính:

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

### Các file frontend quan trọng

- `apps/admin-web/src/app/App.tsx`
  - bọc `AdminDataProvider` và `AuthProvider`
  - chờ bootstrap xong mới render app
- `apps/admin-web/src/app/router.tsx`
  - định nghĩa route quản trị
- `apps/admin-web/src/data/store.tsx`
  - state dùng chung của admin
  - gọi bootstrap và các API CRUD
- `apps/admin-web/src/data/types.ts`
  - khai báo shape dữ liệu frontend
- `apps/admin-web/src/lib/api.ts`
  - tầng HTTP client giao tiếp với backend
- `apps/admin-web/src/features/auth/AuthContext.tsx`
  - login, logout và restore session
- `apps/admin-web/src/features/places/PlacesPage.tsx`
  - CRUD địa điểm, translation, audio guide trong luồng places
- `apps/admin-web/src/features/places/OpenStreetMapPicker.tsx`
  - chọn tọa độ trên bản đồ
- `apps/admin-web/src/features/content/ContentPage.tsx`
  - CRUD món ăn và nội dung media liên quan
- `apps/admin-web/src/features/qr/QrRoutesPage.tsx`
  - CRUD QR và route
- `apps/admin-web/src/features/settings/SettingsPage.tsx`
  - cài đặt hệ thống
- `apps/admin-web/vite.config.ts`
  - proxy `/api` và `/storage` về backend

## 7. Source backend

Thư mục chính:

```text
apps/backend-api/
|-- Controllers/
|-- Contracts/
|-- Infrastructure/
|-- Models/
|-- Properties/
|-- Program.cs
|-- appsettings.json
`-- VinhKhanh.BackendApi.csproj
```

### Các file backend quan trọng

- `apps/backend-api/Program.cs`
  - đăng ký service
  - bật CORS
  - bật static files
  - gom xử lý lỗi API
- `apps/backend-api/Contracts/ApiResponse.cs`
  - khai báo `ApiResponse<T>` và các request/response contract
- `apps/backend-api/Controllers/BootstrapController.cs`
  - trả bootstrap state
- `apps/backend-api/Controllers/AuthController.cs`
  - login, refresh, logout
- `apps/backend-api/Controllers/PlacesController.cs`
  - CRUD địa điểm
- `apps/backend-api/Controllers/TranslationsController.cs`
  - CRUD nội dung thuyết minh
- `apps/backend-api/Controllers/AudioGuidesController.cs`
  - CRUD audio guide
- `apps/backend-api/Controllers/FoodItemsController.cs`
  - CRUD món ăn
- `apps/backend-api/Controllers/QrRoutesController.cs`
  - CRUD QR và route
- `apps/backend-api/Controllers/StorageController.cs`
  - upload file
- `apps/backend-api/Infrastructure/AdminDataRepository.cs`
  - repository chính cho bootstrap, auth và CRUD
- `apps/backend-api/Infrastructure/AdminDataRepository.Sql.cs`
  - kết nối SQL Server, xử lý seed/init database và helper SQL
- `apps/backend-api/Infrastructure/StorageService.cs`
  - lưu tệp upload và trả URL

## 8. API chính

Nhóm endpoint đang dùng:

- `GET /api/v1/bootstrap`
- `GET /api/v1/dashboard/summary`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `GET /api/v1/places`
- `GET /api/v1/translations`
- `GET /api/v1/audio-guides`
- `GET /api/v1/media-assets`
- `GET /api/v1/food-items`
- `GET /api/v1/qr-codes`
- `GET /api/v1/routes`
- `GET /api/v1/promotions`
- `GET /api/v1/reviews`
- `GET /api/v1/settings`
- `POST /api/v1/storage/upload`
- `GET /api/v1/activity/audit-logs`

## 9. Cách chạy dự án

### Yêu cầu

- `Node.js` và `npm`
- `.NET SDK 10`
- `SQL Server`

### Chạy frontend

```bash
npm run install:admin
npm run dev:admin
```

Frontend mặc định chạy tại:

- `http://localhost:5173`
- nếu chạy `npm run preview`, Docker hoặc deploy frontend tĩnh tách riêng backend, hãy tạo `apps/admin-web/.env` với `VITE_API_BASE_URL=http://localhost:5080/api/v1`

### Chạy backend

```bash
npm run dev:backend
```

Backend mặc định chạy theo launch profile:

- `http://localhost:5080`

### Build

```bash
npm run build:admin
npm run build:backend
```

## 10. Cấu hình SQL Server

Chuỗi kết nối nằm trong:

- `apps/backend-api/appsettings.json`

Ví dụ:

```json
"ConnectionStrings": {
  "AdminSqlServer": "Server=ENXUN\\SQLEXPRESS;Database=VinhKhanhFoodAdmin;User ID=sa;Password=1234567;TrustServerCertificate=True;Encrypt=False;"
}
```

Lưu ý:

- nên dùng `User ID` và `Password`, không dùng `username`
- backend có hỗ trợ chuẩn hóa `username` thành `User ID` để tránh lỗi parse
- nếu database chưa tồn tại, backend có thể dùng file seed SQL Server để khởi tạo

## 11. Tài khoản demo

Tài khoản mẫu trong seed hiện tại:

- `superadmin@vinhkhanh.vn / Admin@123`
- `bbq@vinhkhanh.vn / Admin@123`
- `oc@vinhkhanh.vn / Admin@123`

## 12. Nên đọc file nào đầu tiên nếu muốn hiểu nhanh hệ thống

Thứ tự gợi ý:

1. `apps/admin-web/src/app/App.tsx`
2. `apps/admin-web/src/data/store.tsx`
3. `apps/admin-web/src/lib/api.ts`
4. `apps/admin-web/src/features/auth/AuthContext.tsx`
5. `apps/backend-api/Program.cs`
6. `apps/backend-api/Contracts/ApiResponse.cs`
7. `apps/backend-api/Controllers/BootstrapController.cs`
8. `apps/backend-api/Controllers/AuthController.cs`
9. `apps/backend-api/Infrastructure/AdminDataRepository.cs`
10. `apps/backend-api/Infrastructure/AdminDataRepository.Sql.cs`
11. `apps/backend-api/Infrastructure/StorageService.cs`

## 13. Tóm tắt một câu

Đồ án hiện tại vận hành theo hướng: admin web khởi động bằng bootstrap từ backend, đăng nhập qua auth API, mọi thay đổi dữ liệu đi qua CRUD API, tệp upload đi qua backend storage, và backend cùng SQL Server là nguồn dữ liệu duy nhất cho hệ thống.
