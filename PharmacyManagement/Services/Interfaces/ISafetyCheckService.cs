using PharmacyManagement.Models.ViewModels.Sales;

namespace PharmacyManagement.Services.Interfaces;

/// <summary>
/// Kiểm tra an toàn khi bán — BFD 3.1.3 (bán lẻ) và 3.2.2.3 (bán theo đơn), Hệ thống thực hiện, chỉ cảnh báo, dược sĩ quyết định:
/// dị ứng của khách (CustomerAllergies), tương tác giữa hoạt chất của các thuốc khác nhau (IngredientInteractions),
/// và — khi có <c>diagnosis</c> hoặc bật kiểm tra bệnh nền — chống chỉ định (MedicineContraindications, DEC-08).
/// </summary>
public interface ISafetyCheckService
{
    /// <param name="checkContraindications">true: so từ khóa chống chỉ định của thuốc với <paramref name="diagnosis"/> + bệnh nền của khách.</param>
    Task<List<SafetyWarning>> CheckAsync(int? customerId, IReadOnlyCollection<int> medicineIds, bool checkContraindications, string? diagnosis,
        CancellationToken ct = default);
}
