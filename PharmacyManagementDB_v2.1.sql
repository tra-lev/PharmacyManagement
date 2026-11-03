/* ============================================================================
   PharmacyManagementDB — THAY ĐỔI v2.1 (chạy SAU PharmacyManagementDB.sql v2)
   Nội dung: quy tắc tài khoản DEC-18 (02/11/2026)
     [A-01] Chỉ MỘT tài khoản mang vai trò Owner — CSDL tự chặn, kể cả khi gọi API
            hoặc sửa dữ liệu ngoài giao diện.
     [A-02] Mỗi email chỉ thuộc MỘT tài khoản.
   Không thêm bảng, không thêm/đổi/xóa cột, không đụng dữ liệu nghiệp vụ.
   Chạy được trên DB đã có dữ liệu mẫu. Chạy lại nhiều lần không lỗi (bỏ qua chỉ mục đã có).
   Cách chạy: SSMS → mở file → Execute (chạy cả file).
   ============================================================================ */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO
IF DB_ID(N'PharmacyManagementDB') IS NULL
BEGIN
    RAISERROR(N'Chưa có database PharmacyManagementDB — hãy chạy PharmacyManagementDB.sql (v2) trước.', 16, 1);
    SET NOEXEC ON;
END
GO
USE PharmacyManagementDB;
GO
IF OBJECT_ID(N'dbo.AspNetUserRoles', N'U') IS NULL OR OBJECT_ID(N'dbo.AspNetUsers', N'U') IS NULL
BEGIN
    RAISERROR(N'Không thấy bảng AspNetUserRoles/AspNetUsers — database chưa đúng v2.', 16, 1);
    SET NOEXEC ON;
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.AspNetRoles WHERE Id = N'7c1e5b0a-0001-4a6f-9b1e-000000000001' AND NormalizedName = N'OWNER')
BEGIN
    RAISERROR(N'Không thấy vai trò Owner với Id cố định của v2 — dừng lại.', 16, 1);
    SET NOEXEC ON;
END
GO
/* Kiểm tra dữ liệu hiện có không vi phạm trước khi tạo chỉ mục */
IF (SELECT COUNT(*) FROM dbo.AspNetUserRoles WHERE RoleId = N'7c1e5b0a-0001-4a6f-9b1e-000000000001') > 1
BEGIN
    RAISERROR(N'Đang có nhiều hơn 1 tài khoản Owner — xử lý tay trước khi chạy v2.1.', 16, 1);
    SET NOEXEC ON;
END
GO
IF EXISTS (SELECT NormalizedEmail FROM dbo.AspNetUsers WHERE NormalizedEmail IS NOT NULL
           GROUP BY NormalizedEmail HAVING COUNT(*) > 1)
BEGIN
    RAISERROR(N'Có email trùng giữa các tài khoản — xử lý tay trước khi chạy v2.1.', 16, 1);
    SET NOEXEC ON;
END
GO

/* [A-01] Owner duy nhất. Lọc theo Id vai trò Owner cố định (seed v2). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AspNetUserRoles_SingleOwner' AND object_id = OBJECT_ID(N'dbo.AspNetUserRoles'))
    CREATE UNIQUE INDEX UX_AspNetUserRoles_SingleOwner
        ON dbo.AspNetUserRoles(RoleId)
        WHERE RoleId = N'7c1e5b0a-0001-4a6f-9b1e-000000000001';
GO

/* [A-02] Email duy nhất. Giữ nguyên chỉ mục EmailIndex (không unique) mà Identity khai báo. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_AspNetUsers_NormalizedEmail' AND object_id = OBJECT_ID(N'dbo.AspNetUsers'))
    CREATE UNIQUE INDEX UX_AspNetUsers_NormalizedEmail
        ON dbo.AspNetUsers(NormalizedEmail)
        WHERE NormalizedEmail IS NOT NULL;
GO

/* Kết quả */
SELECT i.name AS ChiMuc, OBJECT_NAME(i.object_id) AS Bang, i.is_unique AS DuyNhat, i.filter_definition AS DieuKienLoc
FROM sys.indexes i
WHERE i.name IN (N'UX_AspNetUserRoles_SingleOwner', N'UX_AspNetUsers_NormalizedEmail');
GO
SET NOEXEC OFF;
GO
