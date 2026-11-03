namespace PharmacyManagement.Helpers;

/// <summary>Kết quả nghiệp vụ trả về controller: thành công/thất bại + thông báo tiếng Việt.</summary>
public sealed class OperationResult
{
    private OperationResult(bool succeeded, string? message, string? field, string? warning)
    {
        Succeeded = succeeded;
        Message = message;
        Field = field;
        Warning = warning;
    }

    public bool Succeeded { get; }

    /// <summary>Thông báo thành công hoặc lỗi.</summary>
    public string? Message { get; }

    /// <summary>Tên trường gây lỗi (để hiện lỗi ngay dưới ô nhập), null nếu lỗi chung.</summary>
    public string? Field { get; }

    /// <summary>Thành công nhưng có việc cần chú ý, ví dụ đã tạo lời mời nhưng chưa gửi được email.</summary>
    public string? Warning { get; }

    public static OperationResult Ok(string? message = null, string? warning = null) => new(true, message, null, warning);

    public static OperationResult Fail(string message, string? field = null) => new(false, message, field, null);
}

/// <summary>Kết quả có kèm giá trị.</summary>
public sealed class OperationResult<T>
{
    private OperationResult(bool succeeded, T? value, string? message, string? field)
    {
        Succeeded = succeeded;
        Value = value;
        Message = message;
        Field = field;
    }

    public bool Succeeded { get; }
    public T? Value { get; }
    public string? Message { get; }
    public string? Field { get; }

    public static OperationResult<T> Ok(T value, string? message = null) => new(true, value, message, null);

    public static OperationResult<T> Fail(string message, string? field = null) => new(false, default, message, field);
}
