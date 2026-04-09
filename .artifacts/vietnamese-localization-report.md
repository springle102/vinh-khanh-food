# Vietnamese Localization Normalization Report

## Phạm vi đã xử lý

- UI admin, mobile app, backend API response, log/audit action và seed SQL.
- Đồng bộ nội dung TTS dùng service chung giữa admin và app.
- Bổ sung script migrate cho dữ liệu legacy và cấu hình UTF-8 mặc định cho repo.

## Trước -> sau

| Khu vực | Trước | Sau |
| --- | --- | --- |
| Admin login | `Dang nhap he thong nha hang` | `Đăng nhập hệ thống nhà hàng` |
| Admin login | `Mat khau` | `Mật khẩu` |
| Admin login | `Dang dang nhap...` | `Đang đăng nhập...` |
| Admin preview TTS | `Dang phat ElevenLabs TTS.` | `Đang phát ElevenLabs TTS.` |
| Admin preview TTS | `Da phat xong TTS preview.` | `Đã phát xong bản xem thử TTS.` |
| Admin POI playback | `Da tam dung thuyet minh.` | `Đã tạm dừng bài thuyết minh.` |
| Admin POI playback | `Khong the phat audio guide da luu.` | `Không thể phát audio guide đã lưu.` |
| Admin settings | `Audio guide da upload van duoc uu tien phat truoc...` | `Audio guide đã upload vẫn được ưu tiên phát trước...` |
| Mobile scanner | `Quet QR` | `Quét QR` |
| Mobile scanner | `Nhap ma thu cong` | `Nhập mã thủ công` |
| Mobile services | `Khong tai duoc goi localization...` | `Không tải được gói localization...` |
| Backend validation | `Du lieu gui len khong hop le.` | `Dữ liệu gửi lên không hợp lệ.` |
| Backend auth | `Thong tin dang nhap khong hop le.` | `Thông tin đăng nhập không hợp lệ.` |
| Backend narration | `Tieng Viet` | `Tiếng Việt` |
| Backend narration | `Khong co ban dich luu san...` | `Chưa có bản dịch lưu sẵn...` |
| Backend SQL bootstrap | `Khong the ket noi SQL Server...` | `Không thể kết nối SQL Server...` |
| Audit log | `Dang nhap admin` | `Đăng nhập admin` |
| Audit log | `Cap nhat noi dung thuyet minh` | `Cập nhật nội dung thuyết minh` |
| Audit log | `Tao audio guide` | `Tạo audio guide` |
| Audit log | `Cap nhat trang thai danh gia` | `Cập nhật trạng thái đánh giá` |
| Route content | `Khoi dau 45 phut` | `Khởi đầu 45 phút` |
| Route content | `Tour buoi toi tap trung vao mon nuong, oc va khong khi pho am thuc ve dem.` | `Tour buổi tối tập trung vào món nướng, ốc và không khí phố ẩm thực về đêm.` |
| Legacy narration | `chao mung ban den voi pho am thuc vinh khanh` | `Chào mừng bạn đến với phố ẩm thực Vĩnh Khánh.` |

## File/script áp dụng tự động

- `d:\vinh-khanh-food\scripts\sql\normalize-vietnamese-content.sql`
  - Chuẩn hóa audit actions, route demo content và một số câu narration legacy đang không dấu.
- `d:\vinh-khanh-food\.editorconfig`
  - Ép charset UTF-8 cho toàn repo để tránh lỗi font/ký tự.

## Ghi chú

- Một số chuỗi không dấu vẫn được giữ lại trong code dưới dạng khóa legacy để nhận diện dữ liệu cũ và chuyển sang bản có dấu. Chúng không còn là text hiển thị cho người dùng cuối.
- Sau các thay đổi ở source, dữ liệu mới sinh ra từ admin/app/backend sẽ dùng tiếng Việt có dấu ngay từ đầu; script SQL ở trên dùng để dọn dữ liệu cũ đã tồn tại.
