using PharmacyManagement.Models.ViewModels.Reports;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Báo cáo doanh thu 4.1.1, lợi nhuận 4.1.2, doanh số cá nhân (F4.1.1-OWN) — DEC-46.
/// Doanh thu: hóa đơn đã thanh toán, xếp theo ngày thanh toán đủ tiền. Hoàn trả khách: theo ngày đổi trả.
/// Lợi nhuận gộp = doanh thu thuần − giá vốn hàng bán (giá vốn lô lúc xuất) + giá vốn hàng khách trả đã nhập lại kho.
/// Tổn thất hàng tồn kho (hủy, thu hồi, kiểm kê, trả NCC hoàn thiếu) tính riêng → lợi nhuận sau tổn thất.
/// </summary>
public interface IRevenueReportService
{
    /// <param name="onlyEmployeeId">Chỉ hóa đơn của người bán này (trang "Doanh số của tôi" hoặc lọc người bán).</param>
    /// <param name="includeProfit">Tính giá vốn, lợi nhuận, tổn thất (F4.1.2).</param>
    Task<RevenueReportViewModel> BuildAsync(RevenueFilter filter, int? onlyEmployeeId, bool includeProfit, CancellationToken ct = default);

    /// <summary>Hồ sơ nhân viên của tài khoản (kể cả đã nghỉ); null nếu chưa gắn.</summary>
    Task<(int EmployeeId, string Name)?> EmployeeOfAsync(string userId, CancellationToken ct = default);
}
