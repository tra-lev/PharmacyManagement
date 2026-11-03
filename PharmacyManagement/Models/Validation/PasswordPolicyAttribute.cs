using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace PharmacyManagement.Models.Validation;

/// <summary>
/// Quy tắc mật khẩu dùng chung: cấu hình Identity (Program.cs), kiểm tra ở server và kiểm tra ngay khi gõ (wwwroot/js/validation.js).
/// Chữ thường/chữ hoa/chữ số tính theo bảng ASCII giống PasswordValidator của Identity (chữ có dấu tiếng Việt không được tính).
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    public static IReadOnlyList<string> Missing(string? value)
    {
        value ??= string.Empty;
        var missing = new List<string>();
        if (value.Length < MinLength) missing.Add($"ít nhất {MinLength} ký tự");
        if (!value.Any(c => c is >= '0' and <= '9')) missing.Add("chữ số (0–9)");
        if (!value.Any(c => c is >= 'a' and <= 'z')) missing.Add("chữ thường (a–z)");
        if (!value.Any(c => c is >= 'A' and <= 'Z')) missing.Add("chữ hoa (A–Z)");
        return missing;
    }

    public static string Message(IReadOnlyList<string> missing) => "Mật khẩu còn thiếu: " + string.Join(", ", missing) + ".";
}

/// <summary>Gắn vào ô “mật khẩu mới”: báo thiếu điều kiện nào, cả ở server lẫn ngay khi người dùng gõ.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PasswordPolicyAttribute : ValidationAttribute, IClientModelValidator
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string text || text.Length == 0) return ValidationResult.Success; // để [Required] báo
        var missing = PasswordRules.Missing(text);
        if (missing.Count == 0) return ValidationResult.Success;
        return validationContext.MemberName is null
            ? new ValidationResult(PasswordRules.Message(missing))
            : new ValidationResult(PasswordRules.Message(missing), new[] { validationContext.MemberName });
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        Merge(context.Attributes, "data-val", "true");
        Merge(context.Attributes, "data-val-pmpassword", "Mật khẩu chưa đủ điều kiện.");
        Merge(context.Attributes, "data-val-pmpassword-min", PasswordRules.MinLength.ToString(CultureInfo.InvariantCulture));
    }

    private static void Merge(IDictionary<string, string> attributes, string key, string value)
    {
        if (!attributes.ContainsKey(key)) attributes.Add(key, value);
    }
}
