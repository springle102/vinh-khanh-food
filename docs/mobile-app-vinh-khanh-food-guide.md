# Thiết kế app mobile người dùng cuối cho Vĩnh Khánh Food Guide

## 0. Cơ sở bám theo hệ thống hiện có

Thiết kế này được dựng trực tiếp từ backend/admin hiện có, không tách ra bộ thực thể mới. App mobile chỉ hiển thị, tương tác và đồng bộ dựa trên các thực thể đã có trong hệ thống quản trị:

- `Place`
- `Translation`
- `AudioGuide`
- `MediaAsset`
- `FoodItem`
- `QRCodeRecord`
- `TourRoute`
- `Promotion`
- `Review`
- `CustomerUser`
- `SystemSetting`

### 0.1 Những gì hiện có trong source

- Backend đã có sẵn các nhóm endpoint: `bootstrap`, `auth`, `places`, `translations`, `audio-guides`, `media-assets`, `food-items`, `qr-codes`, `routes`, `promotions`, `reviews`, `settings`, `storage`.
- Các entity gốc nằm trong [Entities.cs](C:\Users\ADMIN\OneDrive\Tài liệu\vinh-khanh-food\apps\backend-api\Models\Entities.cs).
- `SystemSetting` đã có sẵn các field rất quan trọng cho mobile: `DefaultLanguage`, `FallbackLanguage`, `FreeLanguages`, `PremiumLanguages`, `GeofenceRadiusMeters`, `QrAutoPlay`, `GuestReviewEnabled` tại [Entities.cs](C:\Users\ADMIN\OneDrive\Tài liệu\vinh-khanh-food\apps\backend-api\Models\Entities.cs#L197).
- Backend hiện trả bootstrap kiểu admin tại [BootstrapController.cs](C:\Users\ADMIN\OneDrive\Tài liệu\vinh-khanh-food\apps\backend-api\Controllers\BootstrapController.cs#L12).

### 0.2 Những điểm lệch giữa yêu cầu mobile và code hiện tại

Đây là phần rất quan trọng nếu muốn app phát triển thật:

1. Yêu cầu nói tự phát audio khi khách vào bán kính `10m`, nhưng dữ liệu mẫu SQL Server hiện tại ngày `March 23, 2026` đang để `geofenceRadiusMeters = 60` trong `SystemSettings`.
2. `CustomerUser` đã có `preferredLanguage`, `isPremium`, `favoritePlaceIds`, nhưng chưa có endpoint customer auth riêng cho app.
3. `Review` hiện mới có `GET` và `PATCH status`, chưa có `POST` để khách gửi đánh giá.
4. `ViewLog` và `AudioListenLog` hiện chưa gắn với `CustomerUser`, nên chưa đủ dữ liệu để hiện “lịch sử cá nhân” trong hồ sơ người dùng.
5. `AudioGuide` chưa có cờ `IsPremium`; logic premium hiện nên suy ra từ `SystemSetting.PremiumLanguages` và `Translation.IsPremium`.

### 0.3 Kết luận thiết kế

App mobile sẽ:

- dùng lại toàn bộ thực thể hiện có làm source of truth
- thêm một lớp “mobile view model” ở client để ghép dữ liệu từ nhiều entity
- đề xuất mở rộng backend ở mức tối thiểu, vẫn nằm trên các entity hiện hữu, không tạo domain mới ngoài hệ admin

## 1. Ý tưởng tổng thể và định vị sản phẩm

### 1.1 Tên sản phẩm

`Vĩnh Khánh Food Guide`

### 1.2 Ý tưởng cốt lõi

Đây là một app du lịch ẩm thực thông minh cho khách tham quan Phố Ẩm thực Vĩnh Khánh. Trọng tâm trải nghiệm không phải là “đọc danh bạ quán ăn”, mà là:

- quét QR để vào đúng nội dung tại đúng điểm
- tự động nghe thuyết minh khi đi tới gần quán
- khám phá món ăn, ưu đãi và tuyến tham quan theo ngữ cảnh thật ngoài đường phố

### 1.3 Định vị sản phẩm

App được định vị là:

- một “audio food guide” cho khách du lịch
- một “companion app” khi đi bộ khám phá khu phố
- một sản phẩm mobile-first, thao tác nhanh, ít bước, dễ dùng khi đang đứng trước quán

### 1.4 Giá trị khác biệt

So với app du lịch chung chung, Vĩnh Khánh Food Guide có 4 điểm khác biệt:

1. Nội dung được quản lý tập trung từ admin web, nên thông tin đồng bộ và kiểm soát được.
2. QR scan là entry point chính, rất hợp với hành vi tại chỗ.
3. Audio guide đa ngôn ngữ là lõi sản phẩm, không phải tính năng phụ.
4. Nội dung miễn phí và premium được điều khiển từ `SystemSetting` và nội dung admin, không hard-code ở app.

## 2. Chân dung người dùng mục tiêu

### 2.1 Persona 1: Khách du lịch quốc tế

- Độ tuổi: 22-45
- Nhu cầu: tìm quán nổi bật, nghe giới thiệu ngắn gọn, dễ hiểu bằng tiếng mẹ đẻ hoặc tiếng Anh
- Hành vi: quét QR, xem map, xem route, lưu địa điểm muốn quay lại
- Pain point: khó hiểu quán nào thật sự nổi bật, rào cản ngôn ngữ, khó chọn món

### 2.2 Persona 2: Khách nội địa đi ăn trải nghiệm

- Độ tuổi: 18-35
- Nhu cầu: xem món signature, khuyến mãi, review, quán hot
- Hành vi: khám phá nhanh theo danh sách, lọc theo giá, xem quán gần mình
- Pain point: quá nhiều lựa chọn, thiếu thông tin cô đọng

### 2.3 Persona 3: Người đi theo nhóm hoặc gia đình

- Độ tuổi: 25-50
- Nhu cầu: route ngắn, dễ theo, biết thời lượng tham quan, phù hợp trẻ em hoặc người lớn tuổi
- Hành vi: xem route, chọn quán ít di chuyển, xem giá trước
- Pain point: khó sắp lịch trình khi chưa quen khu phố

### 2.4 Persona 4: Khách dùng thử trước khi mua premium

- Nhu cầu: dùng free trước, sau đó mở khóa thêm ngôn ngữ/nội dung
- Hành vi: nghe tiếng Việt/Anh miễn phí, thấy khóa ở tiếng Trung/Hàn/Nhật
- Pain point: không muốn bị ép mua ngay, cần hiểu premium mang lại gì

## 3. Kiến trúc thông tin của app

### 3.1 Cấu trúc cấp cao

App được tổ chức thành 5 khu chính:

1. `Home`
2. `Discover`
3. `Scan`
4. `Routes`
5. `Profile`

### 3.2 Cấu trúc nội dung bên trong

```text
Home
- Banner khu phố
- Featured places
- Active promotions
- Featured routes
- Trending food items
- Continue listening

Discover
- Search
- Filters
- Place list
- Map view
- Food spotlight

Scan
- QR scanner
- Scan result
- Deep link to place detail

Routes
- Route list
- Route detail
- Route navigator / progress

Profile
- Guest or signed-in profile
- Favorites
- Scan history
- Audio history
- Premium
- Settings
```

### 3.3 Quy tắc tổ chức dữ liệu hiển thị

- `Home` ưu tiên dữ liệu nhẹ, có thể preload.
- `Place Detail` là màn hình ghép dữ liệu nhiều thực thể.
- `Routes` và `Promotions` là danh sách riêng nhưng luôn có CTA quay về `Place Detail`.
- `Profile` đọc từ `CustomerUser` cộng thêm log lịch sử.

## 4. Danh sách toàn bộ màn hình cần có

### 4.1 Nhóm khởi động và truy cập

1. `SplashScreen`
2. `LanguagePickerModal`
3. `OnboardingScreen`
4. `PermissionPrimerScreen`
5. `AuthChoiceScreen`
6. `LoginScreen`
7. `RegisterScreen`
8. `ContinueAsGuestSheet`

### 4.2 Nhóm chính

9. `HomeScreen`
10. `ExploreListScreen`
11. `ExploreMapScreen`
12. `PlaceDetailScreen`
13. `FoodCollectionScreen`
14. `PromotionListScreen`
15. `PromotionDetailSheet`
16. `RouteListScreen`
17. `RouteDetailScreen`
18. `RouteNavigatorScreen`
19. `QrScannerModal`
20. `QrResultScreen`
21. `AudioPlayerBottomSheet`
22. `AudioPlayerFullScreen`
23. `FavoritesScreen`
24. `ReviewListSection` trong detail
25. `WriteReviewModal`
26. `ProfileScreen`
27. `EditProfileScreen`
28. `PremiumUnlockScreen`
29. `SettingsScreen`
30. `PermissionStatusScreen`
31. `OfflineFallbackScreen`
32. `EmptyStateScreen` theo từng module
33. `ErrorStateScreen`

### 4.3 Màn hình hỗ trợ

34. `SharedPlaceDeepLinkScreen`
35. `NotFoundScanScreen`
36. `AudioUnavailableSheet`

## 5. Navigation structure đề xuất

### 5.1 Root navigation

```text
RootStack
- Splash
- Onboarding
- MainTabs
- PlaceDetail
- QrScanner (modal)
- WriteReview (modal)
- PremiumUnlock
- AudioPlayerFullScreen
- NotFoundScan
```

### 5.2 Bottom tabs

```text
MainTabs
- HomeTab
- DiscoverTab
- ScanActionButton
- RoutesTab
- ProfileTab
```

### 5.3 Stack theo tab

```text
HomeTab
- HomeScreen
- PlaceDetailScreen
- PromotionDetailSheet

DiscoverTab
- ExploreListScreen
- ExploreMapScreen
- FoodCollectionScreen
- PlaceDetailScreen

RoutesTab
- RouteListScreen
- RouteDetailScreen
- RouteNavigatorScreen
- PlaceDetailScreen

ProfileTab
- ProfileScreen
- FavoritesScreen
- EditProfileScreen
- SettingsScreen
- PermissionStatusScreen
- PremiumUnlockScreen
```

### 5.4 Modal

- `QrScannerModal`
- `WriteReviewModal`
- `LanguagePickerModal`
- `PromotionDetailSheet`
- `AudioPlayerBottomSheet`
- `ContinueAsGuestSheet`

### 5.5 Quy tắc điều hướng

- QR scan luôn mở modal, không ép rời flow hiện tại.
- Audio player luôn có mini-player nổi ở đáy app.
- Từ map marker, promotion badge, route stop hay favorite đều đổ về chung `PlaceDetailScreen`.

## 6. Thiết kế UI/UX tổng thể

### 6.1 Visual direction

App cần có cảm giác:

- ấm
- có nhịp sống phố đêm Sài Gòn
- thân thiện với khách quốc tế
- không enterprise

### 6.2 Bảng màu đề xuất

- `Chili Red`: `#D94C2F`
- `Tamarind Orange`: `#F28C28`
- `Night Charcoal`: `#1D1A17`
- `Warm Cream`: `#FFF7EE`
- `Leaf Green`: `#3D7A4C`
- `Soft Gold`: `#E7B75F`

### 6.3 Typography

- Font chính: `Be Vietnam Pro`
- Font fallback quốc tế: `Noto Sans`
- Heading đậm, bo tròn nhẹ, dễ đọc khi ngoài trời
- Kích thước CTA lớn hơn tiêu chuẩn web, vì người dùng đang đi bộ

### 6.4 Thành phần UI trọng tâm

- `Place Card`: ảnh lớn, title, category chip, distance, price, CTA nghe audio
- `Audio Mini Player`: thumbnail quán, tên audio, progress, play/pause
- `Promotion Badge`: màu cam/đỏ nổi, hiển thị ngắn gọn “Đang áp dụng”
- `Route Progress`: stepper ngang hoặc vertical timeline
- `Review Card`: avatar chữ cái, số sao, ngôn ngữ, trạng thái chỉ hiện review approved
- `Premium Lock Card`: icon khóa, preview ngắn, CTA mở khóa

### 6.5 Dark mode / light mode

- Light mode là mặc định vì khách đi ngoài trời
- Dark mode dùng nền charcoal + ảnh sáng + CTA cam
- Audio player nên tối hơn phần còn lại để tạo cảm giác tập trung

## 7. UI/UX chi tiết theo từng màn hình

### 7.1 SplashScreen

#### UI

- nền gradient cam đỏ sang vàng
- logo `Vĩnh Khánh Food Guide`
- tagline: “Scan, nghe, khám phá”
- loading indicator ngắn

#### Hành vi

- load local config
- đọc ngôn ngữ lưu cục bộ
- kiểm tra bootstrap cache
- điều hướng sang onboarding hoặc home

### 7.2 OnboardingScreen

#### UI

- 3 slide lớn, ảnh full-width
- slide 1: quét QR để mở đúng quán
- slide 2: nghe audio thuyết minh đa ngôn ngữ
- slide 3: theo route và nhận ưu đãi
- nút `Chọn ngôn ngữ`
- CTA `Bắt đầu`

#### Hành vi

- mở `LanguagePickerModal`
- sau cùng sang màn permission primer

### 7.3 PermissionPrimerScreen

#### UI

- card giải thích từng quyền:
  - `Camera`: quét QR
  - `Location`: tự phát thuyết minh khi ở gần quán
  - `Notification`: nhắc ưu đãi/tour
  - `Audio`: phát nền
- nút cấp quyền từng bước
- có lựa chọn `Để sau`

#### Hành vi

- xin quyền tuần tự, không xin dồn cùng lúc
- nếu từ chối, app vẫn dùng được nhưng hiện trạng thái hạn chế

### 7.4 AuthChoiceScreen

#### UI

- 2 card lớn:
  - `Dùng như khách`
  - `Đăng nhập / Tạo tài khoản`
- mô tả ngắn lợi ích khi đăng nhập: lưu yêu thích, premium, lịch sử

#### Hành vi

- guest vào app ngay
- sign-in mở auth flow

### 7.5 LoginScreen / RegisterScreen

#### UI

- đơn giản, 1 cột
- email, password, tên hiển thị
- social login chưa cần ở MVP
- link đổi giữa login và register

#### Hành vi

- login/register bằng `CustomerUser`
- lưu access token và refresh token riêng cho mobile

### 7.6 HomeScreen

#### UI

- hero banner về Phố Ẩm thực Vĩnh Khánh
- search nhỏ ở trên
- QR floating button rất nổi
- section:
  - `Địa điểm nổi bật`
  - `Ưu đãi đang diễn ra`
  - `Tuyến tham quan nổi bật`
  - `Món ăn được quan tâm`
  - `Nghe tiếp`
- mini-player sticky khi có audio

#### Hành vi

- chạm banner vào discover
- chạm card vào detail
- chạm QR mở scanner modal
- `Nghe tiếp` mở lại audio gần nhất

### 7.7 ExploreListScreen

#### UI

- search bar lớn
- chip filter:
  - danh mục
  - giá
  - nổi bật
  - có khuyến mãi
  - ngôn ngữ hỗ trợ
- segmented control:
  - `Danh sách`
  - `Bản đồ`
- card danh sách có distance nếu có location

#### Hành vi

- tìm theo tên quán, tag, món ăn, khu vực
- đổi tab map không mất filter

### 7.8 ExploreMapScreen

#### UI

- map toàn màn hình
- bottom sheet danh sách gần đây
- marker màu theo category
- marker nổi bật lớn hơn

#### Hành vi

- tap marker hiện quick card
- swipe quick card chuyển quán gần đó
- CTA `Xem chi tiết` vào detail

### 7.9 QrScannerModal

#### UI

- camera full screen
- khung scan nổi bật
- gợi ý “Hướng camera vào mã QR tại quán”
- nút bật flash
- nút nhập tay mã nếu cần

#### Hành vi

- scan xong resolve `QRCodeRecord`
- nếu `isActive = true` mở `QrResultScreen` rồi vào `PlaceDetailScreen`
- nếu `SystemSetting.QrAutoPlay = true` và có audio hợp lệ thì tự play
- nếu QR inactive hoặc không tồn tại thì mở `NotFoundScanScreen`

### 7.10 QrResultScreen

#### UI

- thumbnail quán
- tên quán
- trạng thái “QR hợp lệ”
- nút `Mở chi tiết`
- nút `Nghe ngay`

#### Hành vi

- tự nhảy sau 1 giây nếu không có lỗi
- dùng như screen chuyển tiếp để người dùng không bị giật

### 7.11 PlaceDetailScreen

#### UI

- hero image / gallery
- top actions:
  - back
  - favorite
  - share
- block thông tin:
  - tên địa điểm
  - category
  - địa chỉ
  - quận/phường
  - giá
  - thời lượng tham quan
  - khoảng cách
- CTA lớn:
  - `Nghe audio`
  - `Chỉ đường`
  - `Quét QR khác`
- section:
  - mô tả ngắn
  - mô tả đầy đủ
  - audio theo ngôn ngữ
  - gallery
  - món ăn liên quan
  - khuyến mãi liên quan
  - review

#### Hành vi

- nếu translation ngôn ngữ hiện tại không có, fallback sang `SystemSetting.FallbackLanguage`
- nếu audio không có, hiện `AudioUnavailableSheet`
- nếu ngôn ngữ là premium và user chưa unlock, show lock card thay vì nội dung đầy đủ

### 7.12 AudioPlayerBottomSheet / FullScreen

#### UI

- ảnh đại diện quán
- title audio
- chip ngôn ngữ
- progress slider
- play/pause
- seek 10s
- tốc độ 1x, 1.25x, 1.5x
- chuyển ngôn ngữ audio nếu có

#### Hành vi

- phát audio upload hoặc TTS URL từ backend
- không cho chọn giọng Bắc/Trung/Nam ở app
- nếu audio URL rỗng hoặc status khác `ready`, hiện fallback:
  - “Nội dung audio của ngôn ngữ này đang được cập nhật”
  - cho đọc text thay thế

### 7.13 RouteListScreen

#### UI

- list route card
- mỗi card có:
  - tên route
  - mô tả ngắn
  - thời lượng
  - độ khó
  - số điểm dừng
  - badge featured

#### Hành vi

- vào `RouteDetailScreen`

### 7.14 RouteDetailScreen

#### UI

- header ảnh/gradient
- tên route
- mô tả
- duration
- difficulty
- danh sách stop với preview place
- CTA `Bắt đầu tuyến`

#### Hành vi

- show toàn bộ place stop theo thứ tự `StopPlaceIds`
- có thể favorite route local, nhưng route vẫn là `TourRoute`

### 7.15 RouteNavigatorScreen

#### UI

- stepper tiến độ
- current stop card
- next stop preview
- nút:
  - `Nghe điểm này`
  - `Chỉ đường`
  - `Đến điểm tiếp theo`

#### Hành vi

- đánh dấu stop đã đi qua bằng local state
- nếu tới gần stop tiếp theo có thể auto suggest phát audio

### 7.16 PromotionListScreen

#### UI

- filter:
  - đang active
  - sắp diễn ra
- promotion card:
  - title
  - thời gian áp dụng
  - quán áp dụng
  - badge trạng thái
  - CTA `Đến địa điểm`

#### Hành vi

- chỉ hiển thị promotion có `status = active` ở home
- upcoming có thể hiện trong tab promotions

### 7.17 FoodCollectionScreen

#### UI

- food grid / list
- ảnh món ăn
- tên món
- giá
- spicy level
- label `Signature` nếu được chọn từ ranking hoặc logic nổi bật

#### Hành vi

- tap món sẽ mở bottom sheet hoặc đưa về place detail của quán

### 7.18 WriteReviewModal

#### UI

- chọn số sao
- textarea bình luận
- hiển thị ngôn ngữ hiện tại
- note “Đánh giá sẽ hiển thị sau khi được duyệt”

#### Hành vi

- guest được gửi nếu `GuestReviewEnabled = true`
- review mới luôn lên `pending`

### 7.19 ProfileScreen

#### UI

- avatar chữ cái
- tên / guest badge
- premium badge
- block:
  - ngôn ngữ ưa thích
  - địa điểm yêu thích
  - lịch sử quét QR
  - lịch sử nghe audio
- CTA:
  - chỉnh sửa hồ sơ
  - mở khóa premium
  - cài đặt

#### Hành vi

- guest thấy CTA tạo tài khoản để đồng bộ dữ liệu

### 7.20 SettingsScreen

#### UI

- ngôn ngữ app
- default/fallback info
- autoplay audio toggle
- quyền vị trí / camera / thông báo
- hỗ trợ email
- privacy policy
- thông tin phiên bản

#### Hành vi

- `autoplay` là setting local của user
- `QrAutoPlay` là setting hệ thống từ backend
- app chỉ tự phát khi cả 2 cùng bật

### 7.21 PremiumUnlockScreen

#### UI

- card so sánh Free vs Premium
- các ngôn ngữ premium
- nội dung mẫu bị khóa
- CTA `Mở khóa`
- mode demo có toggle `Demo unlock`

#### Hành vi

- trong demo có thể bật local premium
- khi production sẽ map với `CustomerUser.IsPremium`

## 8. Luồng người dùng chính

### 8.1 Hành trình khách mới

```text
Splash
-> Onboarding
-> Chọn ngôn ngữ
-> Gợi ý quyền
-> Chọn dùng như khách hoặc đăng nhập
-> Home
-> Xem quán nổi bật / quét QR / khám phá tuyến
```

### 8.2 Hành trình khách quét QR

```text
Home / bất kỳ màn hình nào
-> Mở scanner
-> Scan QR
-> Resolve QRCodeRecord
-> Kiểm tra IsActive
-> Mở Place Detail
-> Nếu QrAutoPlay = true và audio sẵn sàng
-> Tự phát audio
```

### 8.3 Hành trình khách nghe audio

```text
Place Detail
-> Chọn ngôn ngữ
-> Kiểm tra premium
-> Play audio
-> Mini player nổi xuyên app
-> Ghi AudioListenLog
-> Gợi ý địa điểm tiếp theo hoặc route liên quan
```

### 8.4 Hành trình khám phá địa điểm

```text
Discover
-> Search / filter / map
-> Xem card quán
-> Vào Place Detail
-> Xem món ăn, ưu đãi, review
-> Favorite hoặc chỉ đường
```

### 8.5 Hành trình xem khuyến mãi

```text
Home hoặc Promotions
-> Xem promotion active
-> Mở chi tiết ưu đãi
-> CTA đến quán
-> Mở Place Detail
-> Quét QR hoặc nghe audio tại quán
```

### 8.6 Hành trình gửi đánh giá

```text
Place Detail
-> Write Review
-> Chọn sao + nhập bình luận
-> Submit
-> Backend lưu status = pending
-> App báo "đang chờ duyệt"
```

## 9. Wireframe text-based

### 9.1 Home

```text
+--------------------------------------------------+
| Vĩnh Khánh Food Guide          [Lang] [Profile] |
| Khám phá phố ẩm thực theo cách sống động hơn    |
| [ Tìm địa điểm, món ăn, route...              ] |
|                                                  |
| [ QR Scan ]  [ Map ]  [ Nghe tiếp ]             |
|                                                  |
| Địa điểm nổi bật                                 |
| [Ảnh lớn] BBQ Night            120k-350k         |
| [Nghe audio] [Chi tiết]                          |
|                                                  |
| Khuyến mãi đang diễn ra                          |
| [Badge ACTIVE] Combo BBQ nhóm 4 người            |
|                                                  |
| Tuyến tham quan nổi bật                          |
| [Route 45 phút] [Hải sản buổi tối]               |
+--------------------------------------------------+
| Home | Discover |  Scan  | Routes | Profile     |
+--------------------------------------------------+
```

### 9.2 Place Detail

```text
+--------------------------------------------------+
| < Back                          [Fav] [Share]    |
| [Hero Image / Gallery]                            |
| Quảng trường Ẩm thực BBQ Night                    |
| Hải sản nướng • Quận 4 • 120k-350k                |
| 8 phút đi bộ • 50 phút tham quan                  |
|                                                  |
| [ Nghe audio ] [ Chỉ đường ] [ Quét QR ]         |
|                                                  |
| Mô tả ngắn                                        |
| Điểm tụ họp sôi động với món nướng hải sản...     |
|                                                  |
| Audio guide                                       |
| [VI miễn phí] [EN miễn phí] [ZH khóa]            |
|                                                  |
| Món nổi bật                                       |
| [Hàu nướng mỡ hành]                               |
|                                                  |
| Khuyến mãi                                        |
| [Combo BBQ nhóm 4 người]                          |
|                                                  |
| Review                                            |
| [4.8 sao] [Viết đánh giá]                         |
+--------------------------------------------------+
```

### 9.3 Route Navigator

```text
+--------------------------------------------------+
| Route: Hải sản buổi tối                           |
| Progress: 1/2                                     |
| [Stop 1] Quán Ốc Vĩnh Khánh Signature             |
|                                                  |
| [ Nghe điểm này ]                                 |
| [ Chỉ đường ]                                     |
| [ Đến điểm tiếp theo ]                            |
|                                                  |
| Tiếp theo: BBQ Night                              |
+--------------------------------------------------+
```

## 10. Cấu trúc dữ liệu app dựa trên backend/admin hiện có

### 10.1 Model lưu trữ gốc trong app

Vì framework bắt buộc là `.NET MAUI`, model trong app nên được định nghĩa bằng C# và map 1-1 với JSON từ backend:

```csharp
public sealed class PlaceDto { /* map từ /places */ }
public sealed class TranslationDto { /* map từ /translations */ }
public sealed class AudioGuideDto { /* map từ /audio-guides */ }
public sealed class MediaAssetDto { /* map từ /media-assets */ }
public sealed class FoodItemDto { /* map từ /food-items */ }
public sealed class QrCodeRecordDto { /* map từ /qr-codes */ }
public sealed class TourRouteDto { /* map từ /routes */ }
public sealed class PromotionDto { /* map từ /promotions */ }
public sealed class ReviewDto { /* map từ /reviews */ }
public sealed class CustomerUserDto { /* map từ /app/me */ }
public sealed class SystemSettingDto { /* map từ /settings */ }
```

### 10.2 View model do app tự tổng hợp

Đây không phải thực thể mới trên backend, chỉ là model render:

```csharp
public sealed class PlaceDetailViewModel : ObservableObject
{
    public PlaceDto Place { get; init; } = default!;
    public PlaceCategoryDto? Category { get; init; }
    public TranslationDto? ActiveTranslation { get; init; }
    public TranslationDto? FallbackTranslation { get; init; }
    public IReadOnlyList<AudioGuideDto> AudioGuides { get; init; } = [];
    public IReadOnlyList<MediaAssetDto> MediaAssets { get; init; } = [];
    public IReadOnlyList<FoodItemDto> FoodItems { get; init; } = [];
    public IReadOnlyList<PromotionDto> Promotions { get; init; } = [];
    public IReadOnlyList<ReviewDto> Reviews { get; init; } = [];
    public QrCodeRecordDto? QrCode { get; init; }
    public bool IsFavorite { get; set; }
    public double? DistanceMeters { get; set; }
    public bool HasFreeText { get; init; }
    public bool HasPlayableAudio { get; init; }
    public bool IsPremiumLocked { get; init; }
}
```

### 10.3 Mapping cụ thể giữa backend và app

#### Place

- Nguồn: `GET /places`
- Dùng cho list, map, detail, route stop
- Chỉ lấy place có `status = published` cho khách cuối

#### Translation

- Nguồn: `GET /translations`
- Ghép theo:
  - `entityType = place`
  - `entityId = place.id`
  - `languageCode = selectedLanguage`
- Nếu không có thì fallback sang `SystemSetting.FallbackLanguage`

#### AudioGuide

- Nguồn: `GET /audio-guides`
- Chỉ phát nếu:
  - `status = ready`
  - `audioUrl` không rỗng

#### MediaAsset

- Nguồn: `GET /media-assets`
- Ghép theo place để tạo gallery

#### FoodItem

- Nguồn: `GET /food-items?placeId=...`
- Hiện trong place detail và food collection

#### QRCodeRecord

- Nguồn: `GET /qr-codes`
- Dùng resolve QR local ở MVP hoặc resolve server khi scale lớn hơn

#### TourRoute

- Nguồn: `GET /routes`
- `stopPlaceIds` map sang danh sách `Place`

#### Promotion

- Nguồn: `GET /promotions`
- Home lấy `status = active`
- Promotions tab có thể hiện `active` + `upcoming`

#### Review

- Nguồn: `GET /reviews`
- App chỉ hiển thị review `status = approved`
- Review mới gửi từ app phải mặc định `pending`

#### CustomerUser

- Nguồn: customer auth + `GET /me`
- Dùng cho:
  - profile
  - favoritePlaceIds
  - preferredLanguage
  - premium status

#### SystemSetting

- Nguồn: `GET /settings`
- Dùng cho:
  - default/fallback language
  - free/premium languages
  - geofence radius
  - qr autoplay
  - guest review enabled
  - map provider

### 10.4 Logic premium đề xuất

Do `AudioGuide` chưa có `isPremium`, app dùng quy tắc:

```text
audioLocked =
  audio.languageCode thuộc SystemSetting.PremiumLanguages
  hoặc translation cùng entity + language có IsPremium = true
```

Nghĩa là:

- text premium dựa vào `Translation.IsPremium`
- audio premium suy ra từ ngôn ngữ premium hoặc translation premium cùng ngôn ngữ

Đây là cách bám sát hệ hiện có mà chưa cần thêm bảng mới.

## 11. Endpoint nào app cần gọi ở trang nào

| Màn hình / flow | Endpoint | Mục đích | Cách tải | Cache |
|---|---|---|---|---|
| Splash / app start | `GET /api/v1/settings` | lấy cấu hình hệ thống tối thiểu | preload | cache 24h |
| Splash / home bootstrap | `GET /api/v1/bootstrap?scope=mobile` hoặc endpoint mobile riêng | nạp dữ liệu đầu trang | preload | cache 15-30 phút |
| Home featured places | `GET /api/v1/places?status=published&featured=true` | nếu không dùng bootstrap mobile | preload | cache 15 phút |
| Home promotions | `GET /api/v1/promotions?status=active` | section khuyến mãi | preload | cache 10 phút |
| Home routes | `GET /api/v1/routes` | featured route | preload | cache 1 ngày |
| Home continue listening | local store + `GET /me/history` | nghe tiếp | lazy | cache local |
| Explore search | `GET /api/v1/places?...` | danh sách quán | lazy theo filter | cache 5 phút |
| Explore text detail | `GET /api/v1/translations?entityType=place&entityId=...` | mô tả đa ngôn ngữ | lazy | cache 1 ngày |
| Place audio | `GET /api/v1/audio-guides?entityType=place&entityId=...&status=ready` | danh sách audio | lazy | cache 1 ngày |
| Place media | `GET /api/v1/media-assets?entityType=place&entityId=...` | gallery | lazy | cache 1 ngày |
| Place foods | `GET /api/v1/food-items?placeId=...` | món ăn | lazy | cache 1 ngày |
| Place promotions | `GET /api/v1/promotions?placeId=...` | ưu đãi theo quán | lazy | cache 10 phút |
| Place reviews | `GET /api/v1/reviews?placeId=...&status=approved` | review public | lazy | cache 5 phút |
| QR scan | `GET /api/v1/qr-codes?isActive=true` hoặc `GET /api/v1/qr-codes/resolve?value=...` | resolve mã QR | preload nhỏ hoặc lazy | cache 1 ngày cho active QR |
| Routes list | `GET /api/v1/routes` | danh sách route | preload/lazy | cache 1 ngày |
| Profile | `GET /api/v1/app/me` | hồ sơ người dùng | lazy sau login | cache session |
| Favorites | `PATCH /api/v1/app/me/favorites` | cập nhật yêu thích | realtime | local optimistic |
| Review submit | `POST /api/v1/reviews` | gửi đánh giá mới | mutation | không cache |
| Upload avatar/review media sau này | `POST /api/v1/storage/upload` | upload file | lazy | không cache |

## 12. Dữ liệu nào nên preload, lazy load, cache local

### 12.1 Nên preload từ bootstrap mobile

Vì đây là app đi ngoài đường, dữ liệu đầu vào phải nhanh và nhẹ. Bootstrap mobile nên chứa:

- `settings`
- `categories`
- `featured places` đã publish
- `active promotions`
- `featured routes`
- `minimal qr code map`
- `supported languages`
- `top food items` hoặc food spotlight cho home

Không nên nhồi toàn bộ dữ liệu admin vào app bootstrap vì:

- nặng
- lộ dữ liệu không cần thiết như admin user, audit log
- không phù hợp mobile bandwidth

### 12.2 Nên lazy load

- translations đầy đủ của từng place
- audio guides theo place
- media gallery theo place
- food items theo place
- reviews theo place
- chi tiết route có stop details đã ghép
- lịch sử cá nhân

### 12.3 Nên cache local

- settings
- categories
- featured places
- routes
- promotions đang active
- active QR map
- place detail đã mở gần đây
- audio playback state
- favorites local để optimistic UI
- preferred language
- autoplay local setting

### 12.4 Chính sách cache đề xuất

- `settings`: 24h
- `categories`: 7 ngày
- `featured/home bootstrap`: 15-30 phút
- `place detail`: 24h
- `reviews`: 5 phút
- `active promotions`: 10 phút
- `route`: 1 ngày

## 13. Tính năng ưu tiên theo MVP và giai đoạn mở rộng

### 13.1 MVP bắt buộc

1. Splash + onboarding + chọn ngôn ngữ
2. Guest mode + đăng nhập cơ bản
3. Home có featured place, promotion, route, QR CTA
4. QR scan mở đúng place
5. Place detail đầy đủ dữ liệu chính
6. Audio player đa ngôn ngữ
7. Explore list + search + filter cơ bản
8. Explore map
9. Routes list + route detail
10. Favorites
11. Review xem và gửi mới
12. Settings cơ bản
13. Premium UI demo
14. Cache local + offline read cho dữ liệu đã xem

### 13.2 MVP nên có nhưng có thể triển khai tối giản

- geofence auto play khi app foreground
- continue listening
- share place
- deep link từ QR/share link

### 13.3 Phase 2

1. Background geofencing ổn định hơn
2. Push notification theo promotion hoặc route
3. Offline audio download theo route
4. Premium payment thật
5. Review kèm ảnh/video
6. Gợi ý route cá nhân hóa theo lịch sử nghe/quét
7. Recommendation theo ngôn ngữ và sở thích
8. Check-in theo quãng đường hoặc huy hiệu khám phá

## 14. Đề xuất công nghệ phát triển app

### 14.1 Framework bắt buộc: .NET MAUI

Vì ràng buộc của bài toán là bắt buộc dùng `.NET MAUI`, mình thiết kế lại phần kỹ thuật theo hướng native cross-platform bằng `C# + XAML`, ưu tiên Android và iOS.

Lý do chọn kiến trúc này:

1. Cùng hệ sinh thái .NET với backend ASP.NET Core, nên DTO, validation rule và cách tổ chức service dễ đồng bộ hơn.
2. Phù hợp với một đồ án phát triển thật khi nhóm muốn dùng chung ngôn ngữ C# từ API đến mobile.
3. .NET MAUI có sẵn nền tảng tốt cho Shell navigation, DI, Preferences, SecureStorage, Geolocation và tích hợp native khi cần.

#### Stack .NET MAUI đề xuất

- Framework UI: `.NET MAUI`
- Pattern: `MVVM`
- MVVM toolkit: `CommunityToolkit.Mvvm`
- Navigation: `MAUI Shell`
- Dependency injection: `MauiAppBuilder.Services`
- API client: `HttpClient` + typed service, có thể thêm `Refit` nếu muốn giảm code lặp
- JSON serialization: `System.Text.Json`
- Local settings: `Preferences`
- Secure token storage: `SecureStorage`
- Cache/offline database: `SQL Server`
- File cache / downloaded audio: `FileSystem.AppDataDirectory`
- Maps: `Microsoft.Maui.Controls.Maps`
- QR scanning: `ZXing.Net.Maui`
- Audio playback: `CommunityToolkit.Maui MediaElement` hoặc service audio native bọc riêng
- Popup / bottom sheet: `CommunityToolkit.Maui`
- Logging: `ILogger<T>`

#### Cấu trúc project MAUI đề xuất

```text
VinhKhanh.MobileApp
- App.xaml
- AppShell.xaml
- Views/
- ViewModels/
- Models/
- Services/
- Repositories/
- Converters/
- Resources/
- Platforms/
  - Android/
  - iOS/
```

### 14.2 State management đề xuất cho .NET MAUI

Trong MAUI, mình không dùng kiểu state store như React nữa mà chuyển sang mô hình rõ ràng hơn:

- `MVVM` cho UI state và binding
- `Service + Repository` cho server state và cache
- `ObservableObject` + `RelayCommand` cho tương tác màn hình
- `SQLite + in-memory cache` cho dữ liệu đã đồng bộ

#### Phân lớp state

- Server state:
  - `PlaceDto`
  - `TranslationDto`
  - `AudioGuideDto`
  - `PromotionDto`
  - `TourRouteDto`
  - `ReviewDto`
  - `SystemSettingDto`
- Session state:
  - current customer user
  - guest session id
  - premium demo flag
  - preferred language
- UI state:
  - current tab
  - scanner page/modal state
  - current audio queue
  - selected filters
  - map/list mode

#### Service layer nên chia như sau

- `BootstrapService`
- `PlaceService`
- `AudioGuideService`
- `PromotionService`
- `RouteService`
- `ReviewService`
- `CustomerSessionService`
- `SettingsService`
- `LocationService`
- `QrScannerService`
- `AudioPlayerService`
- `SyncService`

### 14.3 Navigation trong .NET MAUI

Navigation nên map trực tiếp với thiết kế màn hình đã nêu:

- `AppShell` cho bottom tabs
- `ShellContent` cho `Home`, `Discover`, `Routes`, `Profile`
- nút `Scan` là action ở giữa hoặc tab đặc biệt để mở scanner page dạng modal
- `GoToAsync` cho route-based navigation
- `CommunityToolkit.Maui Popup` cho language picker, review modal, promotion detail, audio mini sheet

### 14.4 Xử lý dữ liệu và caching trong .NET MAUI

#### Dữ liệu nhẹ

- `Preferences` cho:
  - preferred language
  - autoplay local setting
  - map/list mode
  - onboarding completed

#### Dữ liệu nhạy cảm

- `SecureStorage` cho:
  - access token
  - refresh token
  - customer session id

#### Dữ liệu cache/offline

- `SQL Server` cho:
  - places
  - categories
  - routes
  - promotions
  - qr codes active
  - place detail đã xem
  - review cache ngắn hạn

#### File media

- audio tải tạm và ảnh cache qua `FileSystem.AppDataDirectory`

### 14.5 Tích hợp tính năng lõi trong .NET MAUI

#### QR scan

- dùng `ZXing.Net.Maui`
- nếu camera hoặc decode không ổn định trên thiết bị cụ thể, luôn có fallback nhập tay QR value

#### Bản đồ

- dùng `Microsoft.Maui.Controls.Maps`
- marker chạm để mở preview card
- điều hướng ngoài app qua map URL nếu người dùng chọn chỉ đường

#### Audio guide

- dùng `MediaElement` khi playback từ HTTP URL
- nếu cần kiểm soát sâu hơn cho background audio hoặc session interruption, tách `IAudioPlayerService` theo platform

#### Geofence

- phần tính khoảng cách cơ bản có thể làm bằng `Geolocation` + timer polling khi app foreground
- nếu muốn geofencing nền ổn định hơn ở Phase 2, nên multi-targeting thêm native Android/iOS service trong thư mục `Platforms/`

### 14.6 Xử lý loading, empty, error

#### Loading

- skeleton card ở home và list
- spinner nhỏ ở QR resolve
- shimmer trên place detail hero

#### Empty

- không có khuyến mãi: “Hiện chưa có ưu đãi, bạn vẫn có thể khám phá quán nổi bật”
- không có audio: “Audio của ngôn ngữ này đang được cập nhật”
- không có route: “Bạn có thể tự khám phá theo bản đồ”

#### Error

- lỗi mạng: hiện cached data + banner “Dữ liệu đang là bản lưu gần nhất”
- lỗi QR: cho nhập tay hoặc quay lại scanner
- lỗi location: hướng dẫn mở quyền trong settings

## 15. Quy tắc đồng bộ dữ liệu giữa admin web, backend và app mobile

### 15.1 Nguyên tắc chung

1. Admin web là nơi quản trị nội dung.
2. Backend là source of truth.
3. App mobile chỉ đọc và gửi tương tác người dùng cuối.
4. Mọi nội dung hiển thị ở mobile phải truy được về entity quản trị đang có.

### 15.2 Quy tắc hiển thị cho mobile

- `Place`: chỉ hiển thị `published`
- `Review`: chỉ hiển thị `approved`
- `Promotion`: home ưu tiên `active`
- `QRCodeRecord`: chỉ resolve nếu `isActive = true`
- `AudioGuide`: chỉ phát nếu `status = ready` và có `audioUrl`

### 15.3 Quy tắc fallback ngôn ngữ

```text
selected language
-> place.defaultLanguageCode
-> settings.fallbackLanguage
-> vi
```

### 15.4 Quy tắc autoplay audio

App chỉ tự phát khi thỏa đủ:

1. user đã cấp quyền location
2. user bật local setting `autoplay`
3. backend `SystemSetting.QrAutoPlay = true` với flow QR, hoặc `geofenceRadiusMeters > 0` với flow geofence
4. place có audio `ready`
5. user không đang nghe một audio khác

#### Lưu ý thực tế

- Requirement sản phẩm có thể đặt bán kính mặc định là `10m`
- Nhưng app vẫn phải đọc số thực tế từ `SystemSetting.GeofenceRadiusMeters`
- Trước khi go-live nên đổi giá trị từ `60` xuống `10` trong admin settings nếu muốn bám đúng yêu cầu

### 15.5 Quy tắc sync sau khi admin cập nhật nội dung

- Home bootstrap: revalidate theo TTL ngắn
- Place detail: refetch khi mở màn hình hoặc pull-to-refresh
- Nếu admin sửa translation/audio, place detail phải lấy dữ liệu mới ở lần mở sau
- Nếu QR bị inactive ở admin, app phải chặn mở nội dung dù QR cũ còn trong cache

### 15.6 Quy tắc đồng bộ favorite và lịch sử

- favorite: optimistic update local, sau đó sync backend
- scan history/audio history:
  - guest: lưu local trước
  - signed-in: sync backend rồi merge local khi login

### 15.7 Quy tắc review moderation

- app gửi review mới với `status = pending`
- admin duyệt trên web
- mobile chỉ thấy review `approved`

## 16. Phần backend cần mở rộng tối thiểu để app chạy thật

Thiết kế này vẫn bám entity cũ, nhưng nên bổ sung một số endpoint mobile:

### 16.1 Customer auth

- `POST /api/v1/app/auth/register`
- `POST /api/v1/app/auth/login`
- `POST /api/v1/app/auth/guest`
- `POST /api/v1/app/auth/logout`
- `GET /api/v1/app/me`
- `PATCH /api/v1/app/me`

Vẫn dùng entity `CustomerUser`, không tạo user type mới.

### 16.2 Favorites

- `PATCH /api/v1/app/me/favorites`

Ghi thẳng vào `CustomerUser.FavoritePlaceIds`.

### 16.3 Review creation

- `POST /api/v1/reviews`

Server tự set:

- `status = pending`
- `createdAt = now`

### 16.4 Activity logs cho mobile

Nên mở rộng chính `ViewLog` và `AudioListenLog` bằng field tùy chọn:

- `customerUserId?: string`
- `deviceId?: string`
- `source?: "qr" | "geofence" | "manual" | "route"`

Endpoint đề xuất:

- `POST /api/v1/activity/view-logs`
- `POST /api/v1/activity/audio-listen-logs`
- `GET /api/v1/app/me/view-logs`
- `GET /api/v1/app/me/audio-listen-logs`

### 16.5 Mobile bootstrap

Do `GET /api/v1/bootstrap` hiện là bootstrap admin, nên nên thêm:

- `GET /api/v1/bootstrap/mobile`

Hoặc:

- `GET /api/v1/bootstrap?scope=mobile`

Mục tiêu là chỉ trả dữ liệu thật sự cần cho mobile.

### 16.6 QR resolve

MVP có thể preload `qr-codes` active và resolve local.

Khi scale lớn hơn, nên có:

- `GET /api/v1/qr-codes/resolve?value=...`

## 17. Hành vi geofence auto play đề xuất

Đây là tính năng lõi nên cần viết rõ:

1. App tải danh sách `Place` đã publish có tọa độ hợp lệ.
2. Dùng `SystemSetting.GeofenceRadiusMeters` làm bán kính.
3. Khi user vào vùng gần quán:
   - kiểm tra cooldown chưa phát gần đây
   - kiểm tra app đang foreground hay không
   - kiểm tra audio ngôn ngữ hiện tại có sẵn
4. Nếu đủ điều kiện thì hiện toast nhỏ:
   - “Bạn đang ở gần Quán Ốc Vĩnh Khánh Signature”
   - sau 2 giây tự phát, hoặc cho người dùng chạm để phát

### Quy tắc chống làm phiền

- mỗi place chỉ auto play 1 lần trong 30 phút
- nếu user bấm tắt, không auto play lại trong cùng phiên
- nếu đang nghe audio khác, chỉ hiện suggestion chứ không cắt ngang

## 18. Kết luận đề xuất

Nếu bám đúng hệ admin hiện có, app mobile phù hợp nhất nên được xây như sau:

- `QR scan + audio guide` là lõi
- `Place Detail` là màn hình trung tâm
- `Discover + Map + Routes` là 3 trục khám phá
- `CustomerUser + SystemSetting` điều khiển trải nghiệm cá nhân, ngôn ngữ và premium
- backend chỉ cần mở rộng tối thiểu vài endpoint cho customer auth, review submit, favorite và activity log là có thể phát triển thật

Nói ngắn gọn theo kiểu sinh viên năm 3:

App này không nên làm như một “danh sách quán ăn đẹp mắt”, mà phải làm như một “người hướng dẫn du lịch ẩm thực biết nói”, nơi mọi nội dung đều đi ra từ admin web đã có sẵn, còn mobile chỉ là lớp trải nghiệm thật cho khách khi đứng ngoài phố Vĩnh Khánh.
