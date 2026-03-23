# BRD - Hệ thống thuyết minh tự động đa ngôn ngữ cho Phố Ẩm thực Vĩnh Khánh

## Thông tin tài liệu

| Thuộc tính | Giá trị |
|---|---|
| Loại tài liệu | Business Requirements Document |
| Phiên bản | 1.0 |
| Ngày biên soạn | 21/03/2026 |
| Frontend | React + Vite Admin Web |
| Backend | ASP.NET Core Web API |
| Phạm vi áp dụng | Admin Web + Backend API + lớp dữ liệu phục vụ QR/audio/content |
| Căn cứ biên soạn | Hiện trạng source tại workspace `Playground 2` |

## Project Snapshot

### Phạm vi release hiện tại

Nền tảng quản trị tập trung cho địa điểm, món ăn, bản dịch, audio guide, QR code, route, review, promotion, user và cấu hình vận hành.

### Chỉ số nhanh

| Chỉ số | Giá trị |
|---|---|
| Màn hình nghiệp vụ admin | `12+` |
| Ngôn ngữ cấu hình sẵn | `5` |
| Vai trò admin chính | `2` |
| Nhóm endpoint backend | `14+` |

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
- Hỗ trợ tối thiểu 5 ngôn ngữ: `vi`, `en`, `zh-CN`, `ko`, `ja`.

### 1.6 Chỉ số thành công đề xuất

| Chỉ số | Mục tiêu |
|---|---|
| Địa điểm trọng tâm có hồ sơ nội dung và QR hoạt động | `100%` |
| Thời gian cập nhật một nội dung cơ bản | `< 10 phút` |
| Thời gian xử lý review mới trong vận hành | `24 giờ` |
| Nguồn dữ liệu chuẩn cho admin state | `1 nguồn duy nhất` |

### 1.7 Ghi chú phạm vi giai đoạn

Trọng tâm của giai đoạn hiện tại là lớp quản trị và backend phục vụ trải nghiệm QR/audio. Phần ứng dụng public cho du khách có thể mở rộng ở giai đoạn tiếp theo.

## 2. Kiến trúc tổng quan

### 2.1 Cấu trúc hệ thống dựa trên đồ án hiện tại

Phần này mô tả trung thực kiến trúc source đang có: một `admin-web` React/Vite ở phía frontend và một `backend-api` ASP.NET Core ở phía server.

Lưu ý quan trọng:

- Đồ án hiện tại **không tách thành microservice**.
- Backend là **một API duy nhất**.
- Backend được chia thành **6 nhóm nghiệp vụ REST** rõ ràng.
- Toàn bộ nhóm nghiệp vụ dùng chung `AdminDataRepository` và `StorageService`.
- Dữ liệu vận hành hiện đang persist trực tiếp về SQL Server.
- File upload nằm dưới `/wwwroot/storage`.

Hai nhóm cuối trong bảng dưới đây là hướng mở rộng hợp lý cho giai đoạn sau.

### 2.2 Các nhóm kiến trúc chính

| Nhóm | Trạng thái | Vai trò chính |
|---|---|---|
| `content` | Active | Places, translations, food items, media assets, SEO, publish status |
| `media_audio` | Active | Audio guides, upload MP3/image, StorageService, static file URL dưới `/storage` |
| `admin_security` | Active | Đăng nhập, RBAC, admin user CRUD, session, `managedPlaceId` và kiểm soát quyền |
| `bootstrap_dashboard` | Active | `GET /bootstrap`, dashboard summary, hydrate state ban đầu và refresh toàn cục |
| `qr_routes` | Active | QR image/state, route CRUD, featured path và điểm vào cho trải nghiệm quét mã |
| `operations` | Active | Promotions, review moderation, settings, activity log và tham số vận hành |
| `data_platform` | Reserved | EF Core, SQL Server, migration, backup và tách repository dữ liệu khi mở rộng |
| `public_experience` | Reserved | Public landing/app cho du khách, scan flow hoàn chỉnh, favorites và premium unlock |

### 2.3 Luồng kiến trúc hiện tại

`admin-web (React + Vite) -> REST Controllers -> AdminDataRepository + StorageService -> SQL Server + /wwwroot/storage`

## 3. Phạm vi dự án

### 3.1 Trong phạm vi

- Admin web cho quản trị tập trung
- Đăng nhập và phân quyền quản trị
- Quản lý địa điểm, món ăn và bản dịch
- Quản lý audio guide và upload media
- Quản lý QR code và route tham quan
- Khuyến mãi, review moderation, audit log
- Cấu hình ngôn ngữ, premium, map, storage, TTS
- Dashboard tổng quan

### 3.2 Ngoài phạm vi

- Ứng dụng mobile native riêng cho khách du lịch
- Cổng thanh toán online cho nội dung premium
- Tích hợp CRM, ERP hoặc POS của từng quán
- AI tự động sinh nội dung hoàn chỉnh trong production
- Kiến trúc DB quan hệ nâng cao hơn nếu cần mở rộng sâu hơn trong giai đoạn sau

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
| Khởi tạo và đăng nhập | Admin web tải bootstrap từ backend, người dùng đăng nhập bằng email/password, hệ thống trả session và nạp dữ liệu theo quyền truy cập |
| Quản lý nội dung điểm đến | Quản trị viên tạo hoặc cập nhật địa điểm, mô tả ngắn, mô tả dài, SEO, ngôn ngữ mặc định và trạng thái xuất bản |
| Upload media và audio | Ảnh, MP3 và ảnh QR được upload qua backend storage, sau đó URL được gắn vào entity tương ứng thay vì lưu dữ liệu nhị phân trong frontend |
| Vận hành, review và audit | Review được duyệt hoặc ẩn từ giao diện quản trị; thao tác quan trọng như đăng nhập và cập nhật dữ liệu được lưu vào audit log |

### 6.2 Luồng khởi tạo và đăng nhập quản trị

1. Người quản trị truy cập admin web.
2. Hệ thống tải bootstrap data từ backend.
3. Người dùng đăng nhập bằng email và mật khẩu.
4. Hệ thống xác thực, trả session/token và nạp lại dữ liệu phù hợp.

### 6.3 Luồng quản lý nội dung địa điểm

1. Quản trị viên chọn địa điểm hoặc tạo mới địa điểm.
2. Cập nhật thông tin cơ bản, danh mục, tọa độ, trạng thái và tag.
3. Nhập bản dịch theo từng ngôn ngữ.
4. Lưu nội dung và đồng bộ lại bootstrap state.

### 6.4 Luồng quản lý audio và media

1. Người dùng upload ảnh hoặc MP3 qua backend storage.
2. Hệ thống lưu file vào storage và trả URL.
3. URL được gắn vào thực thể nội dung/audio tương ứng.
4. Audio và media được dùng lại trong giao diện phục vụ khách.

### 6.5 Luồng QR và lộ trình tham quan

1. Hệ thống tạo hoặc cập nhật QR theo thực thể.
2. Quản trị viên bật/tắt QR và thay đổi ảnh QR nếu cần.
3. Quản trị viên cấu hình route gồm các điểm dừng, thời lượng và độ khó.
4. Du khách quét QR để truy cập nội dung tương ứng.

### 6.6 Luồng vận hành và kiểm soát chất lượng

1. Review mới được đưa về trạng thái chờ duyệt.
2. Quản trị viên duyệt hoặc ẩn review.
3. Audit log ghi lại các thay đổi quan trọng.
4. Dashboard tổng hợp số liệu để phục vụ theo dõi vận hành.

## 7. Các nhóm chức năng chính trong sản phẩm

### 7.1 Dashboard

- Tổng hợp số liệu vận hành, nội dung, QR, review và trạng thái dữ liệu.
- Tag: `Analytics`, `Summary`

### 7.2 Places & Content

- Quản lý địa điểm, bản dịch, mô tả SEO và cấu trúc nội dung đa ngôn ngữ.
- Tag: `Place CRUD`, `Translations`

### 7.3 Food & Promotion

- Quản lý món ăn nổi bật, giá tham khảo và chương trình ưu đãi theo quán.
- Tag: `Food Items`, `Promotions`

### 7.4 Audio & Media

- Upload MP3, quản lý audio guide, ảnh đại diện và tài nguyên truyền thông.
- Tag: `Uploaded`, `TTS-ready`

### 7.5 QR & Routes

- Cấu hình mã QR, ảnh QR, trạng thái hoạt động và lộ trình trải nghiệm ẩm thực.
- Tag: `QR State`, `Featured Routes`

### 7.6 Reviews

- Duyệt, ẩn hoặc theo dõi phản hồi khách hàng để duy trì chất lượng hiển thị.
- Tag: `Moderation`, `Guest Feedback`

### 7.7 Users & Roles

- Quản lý tài khoản admin, khóa mở, vai trò và điểm/quán được phân công.
- Tag: `Super Admin`, `Place Owner`

### 7.8 Settings & Audit

- Cấu hình ngôn ngữ, premium, provider vận hành và ghi nhận lịch sử thao tác.
- Tag: `System Config`, `Audit Trail`

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
