USE PharmacyManagementDB;

SELECT  u.UserName                              AS TenTaiKhoan,
        u.Email,
        r.Name                                  AS VaiTro,
        e.EmployeeCode                          AS MaNV,
        e.FullName                              AS HoTen,
        CASE WHEN u.EmailConfirmed = 1 THEN N'Đã xác minh' ELSE N'Chưa xác minh' END AS EmailTrangThai,
        CASE
            WHEN u.PasswordHash IS NULL                     THEN N'Chờ kích hoạt (chưa đặt mật khẩu)'
            WHEN u.LockoutEnd > '9000-01-01'                THEN N'Đã khóa (chủ nhà thuốc khóa)'
            WHEN u.LockoutEnd > SYSDATETIMEOFFSET()         THEN N'Tạm khóa do sai mật khẩu'
            ELSE N'Đang hoạt động'
        END                                     AS TrangThai,
        u.AccessFailedCount                     AS SoLanSaiMatKhau,
        CASE WHEN EXISTS (SELECT 1 FROM dbo.AspNetUserClaims c
                          WHERE c.UserId = u.Id AND c.ClaimType = N'pm:must_change_password')
             THEN N'Có' ELSE N'' END            AS BatDoiMatKhau
FROM dbo.AspNetUsers u
LEFT JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
LEFT JOIN dbo.AspNetRoles r      ON r.Id = ur.RoleId
LEFT JOIN dbo.Employees e        ON e.UserId = u.Id
ORDER BY CASE r.Name WHEN N'Owner' THEN 0 WHEN N'Manager' THEN 1 ELSE 2 END, u.UserName;