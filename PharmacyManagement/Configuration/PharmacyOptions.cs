namespace PharmacyManagement.Configuration;

/// <summary>
/// Thông tin chung của nhà thuốc (không bí mật) — đặt trong appsettings.json, mục "Pharmacy".
/// Tên hiện ở logo, tiêu đề tab trình duyệt, tên người gửi và nội dung email.
/// </summary>
public sealed class PharmacyOptions
{
    public const string SectionName = "Pharmacy";

    public string Name { get; set; } = "Nhà thuốc";
}
