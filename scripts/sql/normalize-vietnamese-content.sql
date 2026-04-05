SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.AuditLogs', N'U') IS NOT NULL
    BEGIN
        UPDATE dbo.AuditLogs
        SET [Action] = CASE [Action]
            WHEN N'Dang nhap admin' THEN N'Đăng nhập admin'
            WHEN N'Lam moi phien dang nhap' THEN N'Làm mới phiên đăng nhập'
            WHEN N'Tao tai khoan admin' THEN N'Tạo tài khoản admin'
            WHEN N'Cap nhat tai khoan admin' THEN N'Cập nhật tài khoản admin'
            WHEN N'Gui duyet POI moi' THEN N'Gửi duyệt POI mới'
            WHEN N'Cap nhat POI cho duyet' THEN N'Cập nhật POI chờ duyệt'
            WHEN N'Duyet POI' THEN N'Duyệt POI'
            WHEN N'Tao POI' THEN N'Tạo POI'
            WHEN N'Cap nhat POI' THEN N'Cập nhật POI'
            WHEN N'Xoa POI' THEN N'Xóa POI'
            WHEN N'Tao noi dung thuyet minh' THEN N'Tạo nội dung thuyết minh'
            WHEN N'Cap nhat noi dung thuyet minh' THEN N'Cập nhật nội dung thuyết minh'
            WHEN N'Xoa noi dung thuyet minh' THEN N'Xóa nội dung thuyết minh'
            WHEN N'Tao audio guide' THEN N'Tạo audio guide'
            WHEN N'Cap nhat audio guide' THEN N'Cập nhật audio guide'
            WHEN N'Xoa audio guide' THEN N'Xóa audio guide'
            WHEN N'Tao media asset' THEN N'Tạo media asset'
            WHEN N'Cap nhat media asset' THEN N'Cập nhật media asset'
            WHEN N'Xoa media asset' THEN N'Xóa media asset'
            WHEN N'Tao mon an' THEN N'Tạo món ăn'
            WHEN N'Cap nhat mon an' THEN N'Cập nhật món ăn'
            WHEN N'Xoa mon an' THEN N'Xóa món ăn'
            WHEN N'Tao uu dai' THEN N'Tạo ưu đãi'
            WHEN N'Cap nhat uu dai' THEN N'Cập nhật ưu đãi'
            WHEN N'Xoa uu dai' THEN N'Xóa ưu đãi'
            WHEN N'Tao danh gia' THEN N'Tạo đánh giá'
            WHEN N'Cap nhat trang thai danh gia' THEN N'Cập nhật trạng thái đánh giá'
            WHEN N'Khoa nguoi dung cuoi' THEN N'Khóa người dùng cuối'
            WHEN N'Mo khoa nguoi dung cuoi' THEN N'Mở khóa người dùng cuối'
            WHEN N'Cap nhat cai dat he thong' THEN N'Cập nhật cài đặt hệ thống'
            ELSE [Action]
        END
        WHERE [Action] IN (
            N'Dang nhap admin',
            N'Lam moi phien dang nhap',
            N'Tao tai khoan admin',
            N'Cap nhat tai khoan admin',
            N'Gui duyet POI moi',
            N'Cap nhat POI cho duyet',
            N'Duyet POI',
            N'Tao POI',
            N'Cap nhat POI',
            N'Xoa POI',
            N'Tao noi dung thuyet minh',
            N'Cap nhat noi dung thuyet minh',
            N'Xoa noi dung thuyet minh',
            N'Tao audio guide',
            N'Cap nhat audio guide',
            N'Xoa audio guide',
            N'Tao media asset',
            N'Cap nhat media asset',
            N'Xoa media asset',
            N'Tao mon an',
            N'Cap nhat mon an',
            N'Xoa mon an',
            N'Tao uu dai',
            N'Cap nhat uu dai',
            N'Xoa uu dai',
            N'Tao danh gia',
            N'Cap nhat trang thai danh gia',
            N'Khoa nguoi dung cuoi',
            N'Mo khoa nguoi dung cuoi',
            N'Cap nhat cai dat he thong'
        );
    END;

    IF OBJECT_ID(N'dbo.Routes', N'U') IS NOT NULL
    BEGIN
        UPDATE dbo.Routes
        SET Name = CASE NULLIF(LTRIM(RTRIM(Name)), N'')
                WHEN N'Khoi dau 45 phut' THEN N'Khởi đầu 45 phút'
                WHEN N'Hai san buoi toi' THEN N'Hải sản buổi tối'
                ELSE Name
            END,
            Theme = CASE NULLIF(LTRIM(RTRIM(Theme)), N'')
                WHEN N'An vat' THEN N'Ăn vặt'
                WHEN N'Hai san' THEN N'Hải sản'
                WHEN N'Buoi toi' THEN N'Buổi tối'
                WHEN N'Khach quoc te' THEN N'Khách quốc tế'
                WHEN N'Gia dinh' THEN N'Gia đình'
                WHEN N'Tong hop' THEN N'Tổng hợp'
                ELSE Theme
            END,
            [Description] = CASE NULLIF(LTRIM(RTRIM([Description])), N'')
                WHEN N'Tour ngan cho khach moi den, uu tien cac POI noi bat va nhung mon de tiep can.'
                    THEN N'Tour ngắn cho khách mới đến, ưu tiên các POI nổi bật và những món dễ tiếp cận.'
                WHEN N'Tour buoi toi tap trung vao mon nuong, oc va khong khi pho am thuc ve dem.'
                    THEN N'Tour buổi tối tập trung vào món nướng, ốc và không khí phố ẩm thực về đêm.'
                ELSE [Description]
            END,
            UpdatedBy = CASE NULLIF(LTRIM(RTRIM(UpdatedBy)), N'')
                WHEN N'Minh Anh' THEN N'Minh Ánh'
                ELSE UpdatedBy
            END
        WHERE Name IN (N'Khoi dau 45 phut', N'Hai san buoi toi')
            OR Theme IN (N'An vat', N'Hai san', N'Buoi toi', N'Khach quoc te', N'Gia dinh', N'Tong hop')
            OR [Description] IN (
                N'Tour ngan cho khach moi den, uu tien cac POI noi bat va nhung mon de tiep can.',
                N'Tour buoi toi tap trung vao mon nuong, oc va khong khi pho am thuc ve dem.')
            OR UpdatedBy = N'Minh Anh';
    END;

    IF OBJECT_ID(N'dbo.PoiTranslations', N'U') IS NOT NULL
    BEGIN
        UPDATE dbo.PoiTranslations
        SET Title = CASE LOWER(LTRIM(RTRIM(COALESCE(Title, N''))))
                WHEN N'chao mung ban den voi pho am thuc vinh khanh' THEN N'Chào mừng bạn đến với phố ẩm thực Vĩnh Khánh'
                ELSE Title
            END,
            ShortText = CASE LOWER(LTRIM(RTRIM(COALESCE(ShortText, N''))))
                WHEN N'chao mung ban den voi pho am thuc vinh khanh' THEN N'Chào mừng bạn đến với phố ẩm thực Vĩnh Khánh.'
                ELSE ShortText
            END,
            FullText = CASE LOWER(LTRIM(RTRIM(COALESCE(FullText, N''))))
                WHEN N'chao mung ban den voi pho am thuc vinh khanh' THEN N'Chào mừng bạn đến với phố ẩm thực Vĩnh Khánh.'
                WHEN N'tour ngan cho khach moi den, uu tien cac poi noi bat va nhung mon de tiep can.' THEN N'Tour ngắn cho khách mới đến, ưu tiên các POI nổi bật và những món dễ tiếp cận.'
                WHEN N'tour buoi toi tap trung vao mon nuong, oc va khong khi pho am thuc ve dem.' THEN N'Tour buổi tối tập trung vào món nướng, ốc và không khí phố ẩm thực về đêm.'
                ELSE FullText
            END
        WHERE LOWER(LTRIM(RTRIM(COALESCE(Title, N'')))) = N'chao mung ban den voi pho am thuc vinh khanh'
            OR LOWER(LTRIM(RTRIM(COALESCE(ShortText, N'')))) = N'chao mung ban den voi pho am thuc vinh khanh'
            OR LOWER(LTRIM(RTRIM(COALESCE(FullText, N'')))) IN (
                N'chao mung ban den voi pho am thuc vinh khanh',
                N'tour ngan cho khach moi den, uu tien cac poi noi bat va nhung mon de tiep can.',
                N'tour buoi toi tap trung vao mon nuong, oc va khong khi pho am thuc ve dem.');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
