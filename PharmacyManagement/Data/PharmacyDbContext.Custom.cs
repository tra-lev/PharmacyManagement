using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Models.Entities;

namespace PharmacyManagement.Data;

/// <summary>
/// Tùy biến mô hình EF ngoài file scaffold (scaffold --force không ghi đè file này — README mục Quy ước).
/// </summary>
public partial class PharmacyDbContext
{
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // Nhân viên – Ca là 1–NHIỀU (mỗi người nhiều ca theo thời gian). Scaffold hiểu nhầm thành 1–1 vì chỉ mục lọc
        // UX_Shifts_OpenPerEmployee (EmployeeId) WHERE ClosedAt IS NULL là UNIQUE — chỉ mục đó chỉ bảo đảm
        // "mỗi người tối đa 1 ca chưa chốt", vẫn giữ nguyên trong CSDL và trong mô hình.
        // Bỏ điều hướng Employee.Shift (một ca) và khai báo lại quan hệ; truy vấn ca của nhân viên dùng DbSet Shifts.
        modelBuilder.Entity<Employee>().Ignore(e => e.Shift);
        modelBuilder.Entity<Shift>()
            .HasOne(s => s.Employee)
            .WithMany()
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .HasConstraintName("FK_Shifts_Employees");
    }
}
