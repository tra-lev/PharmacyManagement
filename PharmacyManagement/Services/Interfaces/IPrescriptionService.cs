using PharmacyManagement.Helpers;
using PharmacyManagement.Models.ViewModels.Sales;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Đơn thuốc — BFD 3.2.1 tiếp nhận đơn (chưa ghi CSDL), 3.2.2.1 kiểm tra tính hợp lệ, 3.2.2.2 kiểm tra liều dùng,
/// 3.2.2.3 kiểm tra chống chỉ định/dị ứng/tương tác (Hệ thống, chỉ cảnh báo), 3.2.3 lưu đơn (Prescriptions + PrescriptionDetails).
/// Lập hóa đơn theo đơn (3.2.4): <see cref="ISalesInvoiceService"/>.
/// </summary>
public interface IPrescriptionService
{
    Task<PrescriptionFormViewModel> NewFormAsync(CancellationToken ct = default);

    /// <summary>Nạp danh sách chọn và cảnh báo (an toàn + liều) của các dòng hiện có.</summary>
    Task PrepareFormAsync(PrescriptionFormViewModel form, CancellationToken ct = default);

    /// <summary>"Kiểm tra đơn": chỉ kiểm tra hợp lệ (3.2.2.1), không lưu. Cảnh báo hiện qua PrepareFormAsync.</summary>
    Task<OperationResult> CheckAsync(PrescriptionFormViewModel form, CancellationToken ct = default);

    /// <summary>Kiểm tra lại toàn bộ; có cảnh báo thì phải xác nhận đã xem; lưu đơn trạng thái Chưa bán (3.2.3).</summary>
    Task<OperationResult<int>> CreateAsync(PrescriptionFormViewModel form, string userId, CancellationToken ct = default);

    Task<PrescriptionListViewModel> GetListAsync(PrescriptionFilter filter, CancellationToken ct = default);

    Task<PrescriptionDetailsViewModel?> GetDetailsAsync(int prescriptionId, CancellationToken ct = default);

    /// <summary>Hủy đơn chưa bán (bắt buộc lý do, không xóa); đơn đang có hóa đơn chờ thanh toán thì không hủy được.</summary>
    Task<OperationResult> CancelAsync(int prescriptionId, string? reason, string userId, CancellationToken ct = default);
}
