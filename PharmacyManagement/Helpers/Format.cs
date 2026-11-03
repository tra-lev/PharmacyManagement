using System.Globalization;

namespace PharmacyManagement.Helpers;

/// <summary>Định dạng hiển thị dùng chung (tiền, số lượng) theo kiểu Việt Nam: 1.234.567 đ.</summary>
public static class Format
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public static string Money(decimal value) => value.ToString("#,##0.##", Vietnamese) + " đ";

    public static string Money(decimal? value) => value is null ? "—" : Money(value.Value);

    public static string Number(decimal value) => value.ToString("#,##0.###", Vietnamese);

    /// <summary>Giá trị cho thuộc tính value của &lt;input type="number"&gt; (luôn dùng dấu chấm thập phân).</summary>
    public static string Input(decimal? value) => value?.ToString("0.######", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string When(System.DateTime value) => value.ToString("HH:mm dd/MM/yyyy", Vietnamese);
}
