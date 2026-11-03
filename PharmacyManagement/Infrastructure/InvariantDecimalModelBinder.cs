using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PharmacyManagement.Infrastructure;

/// <summary>
/// Ô &lt;input type="number"&gt; luôn gửi số theo dạng 1234.5 (dấu chấm thập phân), không phụ thuộc ngôn ngữ.
/// Ứng dụng chạy văn hóa vi-VN (dấu chấm là phân cách hàng nghìn) nên binder mặc định sẽ đọc "1.5" thành 15.
/// Binder này đọc số theo InvariantCulture cho mọi thuộc tính decimal / decimal?.
/// </summary>
public sealed class InvariantDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
        => context.Metadata.UnderlyingOrModelType == typeof(decimal) ? new InvariantDecimalModelBinder() : null;
}

public sealed class InvariantDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var result = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (result == ValueProviderResult.None) return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, result);
        var raw = result.FirstValue?.Trim().Replace(" ", string.Empty);
        if (string.IsNullOrEmpty(raw))
        {
            if (bindingContext.ModelMetadata.IsReferenceOrNullableType)
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask; // decimal không null: để [Required]/kiểm tra nghiệp vụ báo
        }

        if (decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            bindingContext.Result = ModelBindingResult.Success(value);
        else
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName,
                $"{bindingContext.ModelMetadata.GetDisplayName()}: số không hợp lệ (dùng dấu chấm cho phần thập phân, không gõ dấu phân cách hàng nghìn).");
        return Task.CompletedTask;
    }
}
