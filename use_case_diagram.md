# Sơ đồ Use Case Hệ thống Vinh Khanh Food

Dựa trên tài liệu hệ thống (`README.md`), đây là sơ đồ Use Case cho hệ thống, với các tác nhân (Actors) chính và các trường hợp sử dụng (Use Cases) tương ứng.

## Danh sách Tác nhân (Actors) và Use Cases

1. **SUPER_ADMIN** (Quản trị viên cấp cao)
   - Hoạt động trên: Admin web
   - Quyền hạn: Quản lý toàn bộ dữ liệu hệ thống (Quản trị nội dung, POI, tour, media, thuyết minh, người dùng, đánh giá, khuyến mãi, cấu hình...).

2. **PLACE_OWNER** (Chủ quán/Địa điểm)
   - Hoạt động trên: Admin web
   - Quyền hạn: Quản lý dữ liệu của POI/quán được phân công.

3. **Khách hàng** (Customer)
   - Hoạt động trên: Mobile app
   - Các chức năng chính:
     - Xem bản đồ.
     - Xem thông tin POI.
     - Nghe/xem thuyết minh về địa điểm.
     - Quản lý Tour cá nhân (My Tour).
     - Quét mã QR.
     - Quản lý tài khoản (Đăng ký, đăng nhập, cập nhật hồ sơ).
     - Đổi ngôn ngữ hiển thị.
     - Mua và sử dụng các tính năng Premium.

## Sơ đồ Use Case (Mermaid)

```mermaid
flowchart LR
    %% Định nghĩa các Actors
    SA((SUPER_ADMIN))
    PO((PLACE_OWNER))
    KH((Khách hàng))

    %% Các use case của hệ thống Admin Web
    subgraph Admin_Web [Hệ thống Admin Web]
        direction TB
        UC1([Quản lý toàn bộ dữ liệu hệ thống])
        UC2([Quản lý dữ liệu POI được phân công])
    end

    %% Các use case của hệ thống Mobile App
    subgraph Mobile_App [Hệ thống Mobile App]
        direction TB
        UC3([Quản lý tài khoản])
        UC4([Xem bản đồ & Thông tin POI])
        UC5([Nghe thuyết minh đa ngôn ngữ])
        UC6([Lưu và Quản lý Tour])
        UC7([Quét mã QR])
        UC8([Cài đặt ngôn ngữ])
        UC9([Mua và sử dụng Premium])
    end

    %% Liên kết Actors với Use Cases
    SA --> UC1
    PO --> UC2

    KH --> UC3
    KH --> UC4
    KH --> UC5
    KH --> UC6
    KH --> UC7
    KH --> UC8
    KH --> UC9
```
