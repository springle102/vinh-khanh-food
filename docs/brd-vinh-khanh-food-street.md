# BRD - Hệ thống thuyết minh tự động đa ngôn ngữ cho Phố Ẩm thực Vĩnh Khánh

## Thông tin tài liệu

| Thuộc tính | Giá trị |
|---|---|
| Loại tài liệu | Business Requirements Document |
| Phiên bản | 1.0 |
| Ngày biên soạn | 21/03/2026 |
| Frontend | React + Vite Admin Web + .NET MAUI Mobile App |
| Backend | ASP.NET Core Web API |
| Phạm vi áp dụng | Admin Web + Backend API + Mobile App + lớp dữ liệu phục vụ QR/audio/content/analytics |
| Căn cứ biên soạn | Hiện trạng source tại workspace `Playground 2` |

## Project Snapshot

### Phạm vi release hiện tại

Nền tảng quản trị và trải nghiệm guide đa kênh cho POI, món ăn, bản dịch, audio guide, QR, route, review, analytics và cấu hình vận hành, chạy trên `admin-web`, `backend-api` và `mobile-app`.

### Chỉ số nhanh

| Chỉ số | Giá trị |
|---|---|
| Khối ứng dụng chính | `3` |
| Namespace API chính | `2` |
| Ngôn ngữ cấu hình sẵn | `5` |
| Vai trò admin chính | `2` |
| Nhóm trải nghiệm chính | `admin + guide mobile/public` |

### Giá trị cốt lõi

Một nguồn dữ liệu chuẩn duy nhất để ban quản lý và chủ quán có thể cập nhật thông tin nhanh, giảm sai lệch nội dung và cải thiện trải nghiệm khách quét QR.

## 1. Tổng quan

### 1.1 Tóm tắt điều hành

Phố Ẩm thực Vĩnh Khánh là một điểm đến ẩm thực đặc trưng của Quận 4, thu hút cả khách nội địa lẫn khách quốc tế. Tuy nhiên, thông tin giới thiệu quán, món ăn, lộ trình trải nghiệm, khuyến mãi và nội dung thuyết minh hiện dễ bị phân tán, thiếu chuẩn hóa và khó cập nhật đồng bộ.

Dự án này nhằm xây dựng một hệ thống quản trị tập trung để:

- Chuẩn hóa dữ liệu địa điểm, món ăn và nội dung giới thiệu.
- Hỗ trợ nội dung đa ngôn ngữ và audio guide.
- Quản lý QR code, lộ trình tham quan và khuyến mãi.
- Kiểm soát tài khoản quản trị, cấu hình hệ thống và nhật ký hoạt động.
- Tạo nền tảng để nâng cao trải nghiệm du khách khi quét QR và nghe thuyết minh.

### 1.2 Bối cảnh

Khu phố ẩm thực có nhiều quán, món đặc trưng và nhu cầu tiếp cận khách trong nước lẫn quốc tế, nhưng dữ liệu giới thiệu hiện dễ phân tán và thiếu chuẩn hóa.

### 1.3 Vấn đề cần giải quyết

- Thông tin quán, món, ưu đãi và lộ trình chưa có nguồn dữ liệu thống nhất.
- Thiếu bản dịch và audio guide nhất quán cho khách quốc tế.
- Quản lý QR, review và media còn rời rạc, khó kiểm soát.
- Thiếu cơ chế phân quyền rõ giữa quản trị tổng và chủ quán.
- Thiếu nhật ký hoạt động để truy vết thay đổi nội dung và thao tác quản trị.

### 1.4 Cơ hội kinh doanh

- Tăng chất lượng trải nghiệm tại điểm đến bằng nội dung số có cấu trúc.
- Nâng cao khả năng quảng bá cho từng quán và từng món đặc trưng.
- Giảm thời gian cập nhật thông tin và hạn chế sai lệch dữ liệu.
- Tạo tiền đề cho các gói nội dung premium, analytics và mở rộng dịch vụ số.

### 1.5 Mục tiêu dự án

- Xây dựng admin dashboard tập trung.
- Quản lý nội dung đa ngôn ngữ và audio.
- Chuẩn hóa dữ liệu backend làm nguồn chuẩn duy nhất.
- Cho phép quản lý địa điểm, bản dịch, audio, QR, lộ trình, món ăn, khuyến mãi, đánh giá và cấu hình từ một hệ thống thống nhất.
- Phục vụ trải nghiệm du khách qua mobile app với map, QR, POI detail và narration từ cùng nguồn dữ liệu.
- Hỗ trợ tối thiểu 5 ngôn ngữ: `vi`, `en`, `zh-CN`, `ko`, `ja`.

### 1.6 Chỉ số thành công đề xuất

| Chỉ số | Mục tiêu |
|---|---|
| Địa điểm trọng tâm có hồ sơ nội dung và QR hoạt động | `100%` |
| Thời gian cập nhật một nội dung cơ bản | `< 10 phút` |
| Thời gian xử lý review mới trong vận hành | `24 giờ` |
| Nguồn dữ liệu chuẩn cho admin state | `1 nguồn duy nhất` |

### 1.7 Ghi chú phạm vi giai đoạn

Giai đoạn hiện tại đã có đủ `admin-web`, `backend-api` và `mobile-app` để vận hành nội dung và phục vụ trải nghiệm du khách. Tuy vậy, hai nhánh API `api/v1/*` và `api/guide/v1/*` vẫn đang chạy song song, và một số hạng mục như duyệt nội dung, quản lý tour đầy đủ và hardening production vẫn cần hoàn thiện thêm.

## 2. Kiến trúc tổng quan

### 2.1 Cấu trúc hệ thống dựa trên đồ án hiện tại

Phần này mô tả trung thực kiến trúc source đang có: một `admin-web` React/Vite cho vận hành, một `backend-api` ASP.NET Core ở phía server và một `mobile-app` .NET MAUI cho trải nghiệm du khách.

Lưu ý quan trọng:

- Đồ án hiện tại **không tách thành microservice**.
- Backend là **một ứng dụng ASP.NET Core duy nhất**, nhưng đang phục vụ **2 namespace API**:
  - `api/v1/*` cho admin web hiện hữu
  - `api/guide/v1/*` cho mobile/public guide và guide admin API
- Nhánh admin hiện hữu chủ yếu đi qua `AdminDataRepository` và `StorageService`.
- Nhánh guide mới đi qua `DbContext`, repositories và services riêng, nhưng vẫn dùng cùng database `VinhKhanhFoodAdmin`.
- Dữ liệu nghiệp vụ và dữ liệu guide đang dùng chung `SQL Server`.
- File upload nằm dưới `/wwwroot/storage`.

### 2.2 Các nhóm kiến trúc chính

| Thành phần | Trạng thái | Vai trò chính |
|---|---|---|
| `admin-web` | Active | Giao diện vận hành cho `SUPER_ADMIN` và `PLACE_OWNER`, gồm dashboard, POI, content, users, promotions, reviews, activity, settings |
| `backend admin API` | Active | Phục vụ `api/v1/*`, bootstrap admin state, auth, CRUD và upload media cho admin web |
| `backend guide API` | Active | Phục vụ `api/guide/v1/*`, gồm mobile settings, POI list/detail, QR lookup, routes, analytics và guide admin API |
| `mobile-app` | Active | Ứng dụng `.NET MAUI` cho splash, chọn ngôn ngữ, home, map, list, POI detail, QR scanner, settings và auto narration |
| `sql-server + storage` | Active | Lưu dữ liệu dùng chung, phục vụ URL media/audio và persist analytics |

### 2.3 Luồng kiến trúc hiện tại

```text
Admin Web
-> /api/v1/*
-> Controllers + AdminDataRepository + StorageService
-> SQL Server + /wwwroot/storage

Mobile App
-> /api/guide/v1/*
-> Controllers + Services + Repositories + DbContext
-> SQL Server + /wwwroot/storage
```

Hai luồng trên dùng chung dữ liệu nghiệp vụ, nên thay đổi từ admin có thể được mobile/public app đọc lại ngay từ cùng một nguồn backend.

## 3. Phạm vi dự án

### 3.1 Trong phạm vi

- Admin web cho quản trị tập trung
- Đăng nhập và phân quyền quản trị
- Quản lý POI, món ăn, bản dịch và audio guide
- Quản lý audio guide và upload media
- Quản lý QR code và route tham quan
- Khuyến mãi, review moderation, audit log
- Cấu hình ngôn ngữ, premium, map, storage, TTS
- Dashboard tổng quan
- Mobile app cho khách du lịch với map, list, QR, POI detail, settings và narration
- Public/mobile guide API cho settings, POI, routes và analytics

### 3.2 Ngoài phạm vi

- Cổng thanh toán online cho nội dung premium
- Tích hợp CRM, ERP hoặc POS của từng quán
- AI tự động sinh nội dung hoàn chỉnh trong production
- Tách hệ thống thành microservice độc lập
- Hardening production hoàn chỉnh như certificate pinning, rate limit nâng cao và release store

## 4. Stakeholder và người dùng

| Nhóm | Vai trò | Nhu cầu chính | Giá trị nhận được |
|---|---|---|---|
| Ban quản lý | Chủ sở hữu nghiệp vụ | Chuẩn hóa dữ liệu, kiểm soát vận hành, theo dõi chất lượng nội dung | Có một hệ thống điều phối và báo cáo tập trung |
| Super Admin | Quản trị toàn cục | Quản lý tài khoản, cấu hình, QR, review, audit, dữ liệu hệ thống | Toàn quyền điều phối và kiểm soát rủi ro vận hành |
| Place Owner | Chủ quán hoặc điểm phụ trách | Cập nhật nội dung quán, món ăn, media, khuyến mãi trong phạm vi được giao | Chủ động làm mới thông tin và thu hút khách |
| Biên tập nội dung | Vận hành nội dung | Viết mô tả, kiểm duyệt bản dịch, audio và thông điệp giới thiệu | Tăng chất lượng trình bày và nhất quán thương hiệu |
| Du khách | Người dùng cuối | Truy cập thông tin nhanh qua QR, nghe thuyết minh đa ngôn ngữ | Trải nghiệm tham quan ẩm thực thuận tiện hơn |

## 5. Vai trò và phân quyền

| Vai trò | Phạm vi quyền chính |
|---|---|
| `SUPER_ADMIN` | Toàn quyền trên dữ liệu, cấu hình, tài khoản, audit log, review, QR, route |
| `PLACE_OWNER` | Quản lý điểm/quán được gán, cập nhật nội dung liên quan, món ăn, media, khuyến mãi trong phạm vi được phép |

## 6. Luồng nghiệp vụ chính

### 6.1 Các hành trình chính hệ thống phải hỗ trợ

| Luồng | Mô tả |
|---|---|
| Khởi tạo và đăng nhập quản trị | Admin web khởi tạo session, chuyển hướng theo vai trò và gọi backend để lấy dữ liệu quản trị |
| Quản trị nội dung theo vai trò | `SUPER_ADMIN` và `PLACE_OWNER` thao tác trên các màn hình khác nhau nhưng dùng cùng nguồn dữ liệu backend |
| Upload media và audio | Ảnh và audio được upload qua backend storage, trả URL để gắn lại vào entity |
| Khám phá trên mobile app | Mobile app tải settings, ngôn ngữ, danh sách POI, route và chi tiết nội dung từ `api/guide/v1/*` |
| QR, geofence và analytics | Du khách có thể mở nội dung bằng QR, chọn POI trên map hoặc auto trigger theo geofence; hệ thống ghi view/audio analytics |

### 6.2 Luồng khởi tạo và đăng nhập quản trị

1. Người quản trị truy cập admin web.
2. `router.tsx` kiểm tra session và dùng `RootRedirect` để chuyển tới `/login`, `/admin/*` hoặc `/restaurant/*`.
3. Người dùng đăng nhập bằng email và mật khẩu.
4. Backend xác thực và trả session/token theo role.
5. Frontend điều hướng:
   - `SUPER_ADMIN` vào `/admin/dashboard`
   - `PLACE_OWNER` vào `/restaurant/dashboard`
6. Các màn hình sau đó gọi API để nạp dữ liệu theo quyền truy cập.

### 6.3 Luồng quản trị nội dung theo vai trò

1. Người dùng truy cập các màn hình `dashboard`, `pois`, `content`, `users`, `end-users`, `promotions`, `reviews`, `activity`, `settings`.
2. `SUPER_ADMIN` có thể thao tác toàn cục; `PLACE_OWNER` chỉ thao tác trong phạm vi POI được gán.
3. Người vận hành tạo hoặc cập nhật POI, món ăn, bản dịch, audio, khuyến mãi và các cấu hình liên quan.
4. Backend ghi dữ liệu vào SQL Server và áp dụng rule phân quyền ở controller/service.
5. Frontend đồng bộ lại state hoặc nạp lại danh sách theo response mới nhất từ backend.

### 6.4 Luồng quản lý audio và media

1. Người dùng upload ảnh hoặc MP3 qua backend storage.
2. Hệ thống lưu file vào storage và trả URL.
3. URL được gắn vào thực thể nội dung/audio tương ứng.
4. Mobile app và guide API đọc lại URL này để phát audio chuẩn bị sẵn hoặc hiển thị ảnh.
5. Nếu không có audio sẵn, mobile app có thể fallback sang TTS theo cấu hình hiện tại.

### 6.5 Luồng mobile app và public guide

1. Khi mở app, `SplashViewModel` tải `mobile settings` và ngôn ngữ đã lưu.
2. Người dùng chọn ngôn ngữ hoặc vào thẳng `HomePage` nếu đã có cấu hình trước đó.
3. App gọi `api/guide/v1/settings/mobile`, `api/guide/v1/pois`, `api/guide/v1/pois/routes`.
4. Người dùng khám phá bằng `map`, `list`, `QR scanner` hoặc `POI detail`.
5. Khi mở chi tiết POI, app gọi `TrackView` để ghi nhận analytics.

### 6.6 Luồng QR, geofence và route

1. Du khách quét QR hoặc nhập mã thủ công trong mobile app.
2. App gọi `GET /api/guide/v1/pois/qr/{qrCode}` để mở đúng nội dung POI.
3. Trên map, người dùng có thể chạm marker để mở detail và phát narration.
4. Nếu bật auto narration, `LocationTrackerService` theo dõi vị trí, kiểm tra bán kính geofence và tự phát nội dung khi đến gần POI.
5. App gọi `GET /api/guide/v1/pois/routes` để tải các route nổi bật và hiển thị danh sách điểm dừng.

### 6.7 Luồng analytics và kiểm soát vận hành

1. Khi người dùng xem POI, app gọi `POST /api/guide/v1/pois/{id}/events/view`.
2. Khi phát narration, app gọi `POST /api/guide/v1/pois/{id}/events/audio`.
3. Backend lưu `ViewLogs`, `AudioListenLogs` và cung cấp `GET /api/guide/v1/admin/analytics/overview`.
4. Song song đó, admin web vẫn duy trì `review moderation`, `activity log` và các cấu hình vận hành ở lớp quản trị.

## 7. Các nhóm chức năng chính trong sản phẩm

### 7.1 Admin Dashboard

- Tổng hợp số liệu vận hành, nội dung, review, activity và trạng thái dữ liệu.
- Tag: `Analytics`, `Summary`

### 7.2 POI & Content Management

- Quản lý POI, bản dịch, mô tả SEO, tag, QR code, giờ mở cửa và cấu trúc nội dung đa ngôn ngữ.
- Tag: `POI CRUD`, `Translations`

### 7.3 Food, Promotion & Reviews

- Quản lý món ăn nổi bật, chương trình ưu đãi và phản hồi khách hàng.
- Tag: `Food Items`, `Moderation`

### 7.4 Audio, Media & Storage

- Upload MP3, quản lý audio guide, media asset và phục vụ file qua `/storage/...`.
- Tag: `Uploaded`, `Narration`

### 7.5 Users, Roles & Admin Routing

- Quản lý tài khoản admin, phân quyền `SUPER_ADMIN` và `PLACE_OWNER`, điều hướng `/admin/*` và `/restaurant/*`.
- Tag: `RBAC`, `Managed POI`

### 7.6 Guide Public API

- Phục vụ mobile settings, danh sách POI, nearby, detail, QR lookup, routes và tracking analytics.
- Tag: `api/guide/v1`, `Public Experience`

### 7.7 Mobile Experience

- Cung cấp `Splash`, chọn ngôn ngữ, `Home`, `Map`, `List`, `POI detail`, `QR scanner`, `Settings` và offline cache.
- Tag: `MAUI`, `QR + Geofence`

### 7.8 Settings, Audit & Analytics

- Cấu hình ngôn ngữ, premium, map/TTS provider, geofence và ghi nhận activity log cùng view/audio analytics.
- Tag: `System Config`, `Tracking`

## 8. Danh mục yêu cầu chức năng

| ID | Yêu cầu | Tác nhân | Mức ưu tiên |
|---|---|---|---|
| FR-01 | Cho phép admin đăng nhập bằng email và mật khẩu. | Super Admin, Place Owner | Must |
| FR-02 | Phân quyền tối thiểu giữa `SUPER_ADMIN` và `PLACE_OWNER`. | Hệ thống | Must |
| FR-03 | Cung cấp dashboard tổng quan cho quản trị viên. | Super Admin | Must |
| FR-04 | Cho phép tạo, cập nhật và quản lý địa điểm/quán ăn. | Super Admin, Place Owner | Must |
| FR-05 | Quản lý bản dịch đa ngôn ngữ cho từng thực thể nội dung. | Super Admin, Place Owner | Must |
| FR-06 | Quản lý món ăn gắn với địa điểm, mô tả, hình ảnh, mức giá và độ cay. | Super Admin, Place Owner | Must |
| FR-07 | Quản lý audio guide, nguồn audio và trạng thái audio. | Super Admin, Place Owner | Must |
| FR-08 | Hỗ trợ upload ảnh, audio và tài nguyên QR qua backend storage. | Super Admin, Place Owner | Must |
| FR-09 | Quản lý QR code gồm giá trị mã, ảnh QR, trạng thái kích hoạt và thời điểm quét gần nhất. | Super Admin | Must |
| FR-10 | Cấu hình route tham quan gồm điểm dừng, thời lượng và mức độ trải nghiệm. | Super Admin | Should |
| FR-11 | Quản lý chương trình khuyến mãi theo quán và thời gian hiệu lực. | Super Admin, Place Owner | Should |
| FR-12 | Duyệt, ẩn hoặc theo dõi review của khách hàng. | Super Admin | Must |
| FR-13 | Quản lý tài khoản admin, khóa hoặc mở và gán phạm vi phụ trách. | Super Admin | Must |
| FR-14 | Cấu hình ngôn ngữ, premium, map provider, storage provider, TTS provider và tham số vận hành. | Super Admin | Must |
| FR-15 | Lưu audit log cho thao tác đăng nhập và cập nhật dữ liệu quan trọng. | Hệ thống | Must |
| FR-16 | Cung cấp bootstrap API để frontend nạp trạng thái ban đầu từ backend. | Hệ thống | Must |
| FR-17 | Sau mỗi thao tác CRUD thành công, frontend có thể đồng bộ lại state từ backend. | Hệ thống | Must |
| FR-18 | Cung cấp dữ liệu tổng hợp phục vụ analytics và dashboard. | Super Admin | Should |
| FR-19 | Phục vụ file đã upload qua URL tĩnh dưới `/storage/...`. | Hệ thống | Must |
| FR-20 | Hỗ trợ tối thiểu 5 ngôn ngữ cấu hình sẵn: `vi`, `en`, `zh-CN`, `ko`, `ja`. | Tất cả | Must |

## 9. Quy tắc nghiệp vụ và dữ liệu

| ID | Quy tắc |
|---|---|
| BR-01 | Backend là nguồn dữ liệu chuẩn duy nhất; frontend chỉ bootstrap và refresh lại state. |
| BR-02 | `localStorage` chỉ dùng để giữ session đăng nhập admin, không lưu state vận hành dài hạn. |
| BR-03 | Mỗi place phải có `defaultLanguageCode`; nhóm ngôn ngữ free và premium được cấu hình tại settings. |
| BR-04 | Mỗi QR code phải gắn với một thực thể xác định như `place`, `food_item` hoặc `route`. |
| BR-05 | Review không đi thẳng vào hiển thị ổn định; cần cơ chế duyệt hoặc ẩn để kiểm soát chất lượng. |
| BR-06 | File phải đi qua backend storage, lưu dưới thư mục chuẩn hóa và chỉ trả URL về entity nghiệp vụ. |
| BR-07 | Các thao tác đăng nhập và cập nhật dữ liệu quan trọng cần được ghi vào audit log để truy vết. |
| BR-08 | Place Owner chỉ được thao tác trong phạm vi địa điểm được gán nếu không có quyền toàn cục. |
| BR-09 | Ngôn ngữ premium và ngôn ngữ miễn phí phải được cấu hình ở cấp hệ thống. |
| BR-10 | Trạng thái nội dung tối thiểu gồm `draft`, `published`, `archived`. |

## 10. Yêu cầu phi chức năng

| ID | Nhóm | Yêu cầu |
|---|---|---|
| NFR-01 | Hiệu năng | Màn hình quản trị phải tải được dữ liệu khởi tạo trong thời gian chấp nhận được với bộ dữ liệu demo hiện tại. |
| NFR-02 | Bảo mật | Khu vực admin phải yêu cầu xác thực và phân quyền theo vai trò. |
| NFR-03 | Toàn vẹn dữ liệu | Frontend phải đồng bộ lại state từ backend sau thao tác cập nhật thành công. |
| NFR-04 | Truy vết | Các hành vi quan trọng phải có audit log để đối soát lịch sử. |
| NFR-05 | Khả dụng | Hệ thống phải vận hành ổn định trong môi trường demo và nội bộ. |
| NFR-06 | Mở rộng | Kiến trúc API cần cho phép mở rộng schema SQL Server, migration va tach lop du lieu ro hon o giai doan sau. |
| NFR-07 | Khả dụng giao diện | Giao diện phù hợp desktop và tablet, dễ thao tác cho nhóm vận hành nội dung. |
| NFR-08 | Tương thích dữ liệu | Hỗ trợ UTF-8 và nhập liệu đa ngôn ngữ. |
| NFR-09 | Bảo trì | Contract giữa frontend và backend phải rõ ràng, dễ kiểm thử và dễ mở rộng. |
| NFR-10 | Quản lý media | Đường dẫn lưu trữ file phải nhất quán để dễ backup, phục vụ và thay thế storage provider. |

## 11. Rủi ro và phụ thuộc

### 11.1 Giả định

- Ban quản lý có sẵn bộ dữ liệu địa điểm, món ăn và nội dung mô tả ban đầu.
- Có đội ngũ vận hành nội dung để nhập bản dịch và audio.
- Hạ tầng backend và storage luôn sẵn sàng cho admin web truy cập.

### 11.2 Phụ thuộc

- Backend API ASP.NET Core
- Frontend React + Vite
- Dịch vụ storage phục vụ upload ảnh, audio và QR
- Bộ dữ liệu seed và cấu hình ngôn ngữ

### 11.3 Rủi ro chính

| Rủi ro | Tác động | Hướng xử lý đề xuất |
|---|---|---|
| Backend phụ thuộc vào cấu hình kết nối SQL Server và chất lượng dữ liệu seed ban đầu | Có thể lỗi bootstrap nếu instance, quyền hoặc bảng dữ liệu chưa đúng | Chuẩn hóa connection string, import seed SQL Server và bổ sung kiểm tra kết nối sớm |
| Bản dịch, audio và mô tả điểm đến cần được vận hành nhất quán | Ảnh hưởng trải nghiệm du khách | Áp dụng quy trình kiểm duyệt nội dung trước publish |
| Upload media dung lượng lớn | Ảnh hưởng hiệu năng | Chuẩn hóa giới hạn file và chiến lược lưu trữ |
| Nếu role và phạm vi `managedPlaceId` không được kiểm soát chặt ở backend | Người dùng có thể thao tác vượt quyền | Kiểm tra role và `managedPlaceId` ở backend |
| Admin web phụ thuộc vào backend API, storage và dữ liệu mẫu ban đầu | Rủi ro khi trình diễn hoặc vận hành demo | Chuẩn bị trước dữ liệu SQL Server, storage và checklist vận hành |

## 12. Tiêu chí nghiệm thu và roadmap

### 12.1 Tiêu chí nghiệm thu chính

- Super Admin đăng nhập được và xem dashboard.
- CRUD địa điểm, bản dịch, món ăn, audio, khuyến mãi, review, settings hoạt động.
- Upload ảnh, MP3 và QR trả về URL hợp lệ.
- Audit log ghi nhận thao tác trọng yếu.
- Place Owner chỉ thao tác trong phạm vi được phân quyền.
- Có thể duyệt hoặc ẩn review từ giao diện quản trị.
- Dữ liệu sau khi lưu vẫn còn sau khi tải lại ứng dụng theo cơ chế persist hiện tại của backend.

### 12.2 Kết quả đầu ra mong đợi

- Một bộ dữ liệu quản trị tập trung cho khu phố ẩm thực.
- Nội dung đa ngôn ngữ và audio có thể vận hành thực tế.
- Nền tảng sẵn sàng mở rộng sang public app hoặc dịch vụ premium.

### 12.3 Lộ trình triển khai

#### Giai đoạn 1

MVP quản trị: auth, places, translations, food items, audio, QR, reviews, settings, upload media.

#### Giai đoạn 2

Tối ưu vận hành: analytics sâu hơn, quy trình kiểm duyệt nội dung, báo cáo trạng thái dữ liệu.

#### Giai đoạn 3

Mở rộng nền tảng: chuyển sang DB quan hệ, tích hợp premium/payment và kênh public nếu cần.

## 13. Kết luận

BRD này xác định rõ mục tiêu kinh doanh và phạm vi nghiệp vụ cho hệ thống thuyết minh tự động đa ngôn ngữ Phố Ẩm thực Vĩnh Khánh. Trọng tâm của giai đoạn hiện tại là xây dựng một nền tảng quản trị tập trung, có khả năng quản lý nội dung số, audio, QR, lộ trình và vận hành dữ liệu một cách nhất quán, đồng thời tạo nền móng để mở rộng sang các tính năng thương mại và phân tích sâu hơn trong tương lai.
