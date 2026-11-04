using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace PharmacyManagement.Infrastructure;

/// <summary>
/// Đánh dấu một thuộc tính decimal / decimal? là SỐ TIỀN (đồng, không có số lẻ).
/// - Ô nhập hiện dạng 200.000 (wwwroot/js/money-input.js tự thêm dấu chấm khi gõ, có chữ "đ" bên phải).
/// - Server đọc bằng <see cref="MoneyModelBinder"/>: bỏ dấu chấm, dấu phẩy, khoảng trắng, chữ đ → 200000.
/// - Kiểm tra: nguyên đồng, từ <see cref="Min"/> đến <see cref="Max"/>; trên trình duyệt dùng quy tắc "pmmoney" (wwwroot/js/validation.js).
/// Ô số khác (hệ số quy đổi, liều dùng…) vẫn dùng InvariantDecimalModelBinder.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class MoneyAttribute : ValidationAttribute, IClientModelValidator
{
    public MoneyAttribute() : base("{0} từ {1} đến {2}, tính theo đồng (không có số lẻ).") { }

    public decimal Min { get; set; }

    public decimal Max { get; set; } = 1_000_000_000m;

    public override bool IsValid(object? value)
        => value is null || (value is decimal d && d >= Min && d <= Max && d == decimal.Truncate(d));

    public override string FormatErrorMessage(string name)
        => string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name, Money(Min), Money(Max));

    public void AddValidation(ClientModelValidationContext context)
    {
        var name = context.ModelMetadata.GetDisplayName();
        Merge(context.Attributes, "data-val", "true");
        Merge(context.Attributes, "data-val-pmmoney", FormatErrorMessage(name));
        Merge(context.Attributes, "data-val-pmmoney-min", Min.ToString(CultureInfo.InvariantCulture));
        Merge(context.Attributes, "data-val-pmmoney-max", Max.ToString(CultureInfo.InvariantCulture));
    }

    private static string Money(decimal value) => value.ToString("#,##0", CultureInfo.GetCultureInfo("vi-VN")) + " đ";

    private static void Merge(IDictionary<string, string> attributes, string key, string value)
    {
        if (!attributes.ContainsKey(key)) attributes.Add(key, value);
    }
}

/// <summary>Chọn <see cref="MoneyModelBinder"/> cho thuộc tính có [Money]. Đăng ký TRƯỚC InvariantDecimalModelBinderProvider.</summary>
public sealed class MoneyModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.UnderlyingOrModelType != typeof(decimal)) return null;
        if (context.Metadata is DefaultModelMetadata metadata
            && (metadata.Attributes.PropertyAttributes?.OfType<MoneyAttribute>().Any() == true
                || metadata.Attributes.ParameterAttributes?.OfType<MoneyAttribute>().Any() == true))
            return new MoneyModelBinder();
        return null;
    }
}

/// <summary>Đọc số tiền gõ theo kiểu Việt Nam: "200.000", "200,000", "200 000", "200.000đ" → 200000.</summary>
public sealed class MoneyModelBinder : IModelBinder
{
    private const int MaxDigits = 15;

    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var raw = result.FirstValue ?? string.Empty;
        var cleaned = new string(raw.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('.' or ',' or 'đ' or 'Đ')).ToArray());

        if (cleaned.Length == 0)
        {
            if (bindingContext.ModelMetadata.IsReferenceOrNullableType)
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask; // decimal không null: để [Required] / kiểm tra nghiệp vụ báo
        }

        if (cleaned.Length <= MaxDigits && cleaned.All(char.IsAsciiDigit)
            && decimal.TryParse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            bindingContext.Result = ModelBindingResult.Success(value);
        }
        else
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                $"{bindingContext.ModelMetadata.GetDisplayName()}: chỉ nhập chữ số, ví dụ 200.000.");
        }
        return Task.CompletedTask;
    }
}
