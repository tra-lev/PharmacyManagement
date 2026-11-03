using System.Text;

namespace PharmacyManagement.Helpers;

/// <summary>
/// Tìm kiếm không phân biệt dấu và hoa/thường (gõ "giam dau" ra "Giảm đau", "nguyen" ra "Nguyễn").
/// Cách làm trong truy vấn EF (không phụ thuộc collation mặc định của CSDL):
///   EF.Functions.Collate(cột.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(SearchText.Fold(từ khóa))
/// — collation *_CI_AI bỏ qua dấu thanh, dấu mũ, dấu móc (ả, ấ, ư, ơ…); riêng "đ" là chữ cái riêng nên phải đổi sang "d" ở cả hai phía.
/// Chỉ dùng cho cột nvarchar chứa tiếng Việt (tên thuốc, tên hoạt chất, tên NCC, họ tên khách…);
/// cột mã, số điện thoại (varchar, không dấu) so sánh bình thường.
/// </summary>
public static class SearchText
{
    /// <summary>Collation Windows bản 100, không phân biệt hoa/thường (CI) và dấu (AI); có sẵn trên mọi SQL Server từ 2008.</summary>
    public const string Collation = "Latin1_General_100_CI_AI";

    /// <summary>Chuẩn hóa từ khóa: dựng sẵn dấu (NFC) và đổi đ/Đ thành d/D cho khớp với cột đã đổi tương tự trong truy vấn.</summary>
    public static string Fold(string term)
        => term.Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
}
