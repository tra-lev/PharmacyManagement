namespace PharmacyManagement.Models.ViewModels.Admin;

/// <summary>Nhãn tiếng Việt cho nhật ký hoạt động (DEC-48). Mã thao tác chưa có nhãn hiện "Thao tác khác".</summary>
public static class AuditText
{
    public sealed record Group(string Key, string Title);

    public static readonly IReadOnlyList<Group> Groups = new[]
    {
        new Group("account", "Tài khoản, phân quyền"),
        new Group("catalog", "Danh mục"),
        new Group("sales", "Bán hàng, ca, đổi trả"),
        new Group("stock", "Kho"),
        new Group("debt", "Công nợ nhà cung cấp")
    };

    private static readonly Dictionary<string, (string Label, string Group)> Actions = new(StringComparer.Ordinal)
    {
        // Tài khoản, phân quyền
        ["ACCOUNT_INVITE"] = ("Mời nhân viên", "account"),
        ["ACCOUNT_INVITE_RESEND"] = ("Gửi lại lời mời", "account"),
        ["ACCOUNT_INVITE_CANCEL"] = ("Hủy lời mời", "account"),
        ["ACCOUNT_ACTIVATE"] = ("Kích hoạt tài khoản", "account"),
        ["ACCOUNT_LOCK"] = ("Khóa tài khoản", "account"),
        ["ACCOUNT_UNLOCK"] = ("Mở khóa tài khoản", "account"),
        ["ACCOUNT_EMAIL_CHANGE_REQUEST"] = ("Yêu cầu đổi email", "account"),
        ["ACCOUNT_EMAIL_CHANGED"] = ("Đổi email", "account"),
        ["EMAIL_VERIFY"] = ("Xác minh email", "account"),
        ["OWNER_BOOTSTRAP"] = ("Khởi tạo chủ nhà thuốc", "account"),
        ["OWNER_RECOVERY"] = ("Khôi phục tài khoản chủ nhà thuốc", "account"),
        ["PASSWORD_CHANGE"] = ("Đổi mật khẩu", "account"),
        ["PASSWORD_CHANGE_FIRST_LOGIN"] = ("Đặt mật khẩu lần đầu", "account"),
        ["PASSWORD_RESET"] = ("Đặt lại mật khẩu", "account"),
        ["PROFILE_FULLNAME_UPDATE"] = ("Cập nhật họ tên", "account"),
        ["USERNAME_SYNC"] = ("Cập nhật tên đăng nhập", "account"),
        ["ROLE_PERMISSION_UPDATE"] = ("Sửa phân quyền vai trò", "account"),

        // Danh mục
        ["MEDICINE_CREATE"] = ("Thêm thuốc", "catalog"),
        ["MEDICINE_UPDATE"] = ("Sửa thuốc", "catalog"),
        ["MEDICINE_ACTIVATE"] = ("Kinh doanh lại thuốc", "catalog"),
        ["MEDICINE_DEACTIVATE"] = ("Ngừng kinh doanh thuốc", "catalog"),
        ["SUPPLIER_CREATE"] = ("Thêm nhà cung cấp", "catalog"),
        ["SUPPLIER_UPDATE"] = ("Sửa nhà cung cấp", "catalog"),
        ["SUPPLIER_ACTIVATE"] = ("Giao dịch lại với nhà cung cấp", "catalog"),
        ["SUPPLIER_DEACTIVATE"] = ("Ngừng giao dịch nhà cung cấp", "catalog"),
        ["CUSTOMER_CREATE"] = ("Thêm khách hàng", "catalog"),
        ["CUSTOMER_UPDATE"] = ("Sửa khách hàng", "catalog"),
        ["CUSTOMER_ACTIVATE"] = ("Mở lại hồ sơ khách hàng", "catalog"),
        ["CUSTOMER_DEACTIVATE"] = ("Ẩn hồ sơ khách hàng", "catalog"),
        ["LOCATION_CREATE"] = ("Thêm tủ, kệ", "catalog"),
        ["LOCATION_UPDATE"] = ("Sửa tủ, kệ", "catalog"),
        ["LOCATION_DELETE"] = ("Xóa tủ, kệ", "catalog"),
        ["POLICY_UPDATE"] = ("Sửa định mức tồn, ngưỡng cận hạn thuốc", "catalog"),
        ["SETTING_UPDATE"] = ("Sửa ngưỡng cận hạn chung", "catalog"),

        // Bán hàng, ca, đổi trả
        ["INVOICE_CREATE"] = ("Lập hóa đơn", "sales"),
        ["INVOICE_UPDATE"] = ("Sửa hóa đơn", "sales"),
        ["INVOICE_PAY"] = ("Thu tiền hóa đơn", "sales"),
        ["INVOICE_CANCEL"] = ("Hủy hóa đơn", "sales"),
        ["INVOICE_DISCOUNT_OVER_LIMIT"] = ("Giảm giá vượt mức", "sales"),
        ["PRESCRIPTION_CREATE"] = ("Lưu đơn thuốc", "sales"),
        ["PRESCRIPTION_CANCEL"] = ("Hủy đơn thuốc", "sales"),
        ["RETURN_REQUEST"] = ("Gửi yêu cầu đổi trả", "sales"),
        ["RETURN_REQUEST_DONE"] = ("Xử lý xong yêu cầu đổi trả", "sales"),
        ["RETURN_REQUEST_REJECT"] = ("Từ chối yêu cầu đổi trả", "sales"),
        ["RETURN_CREATE"] = ("Hoàn tiền đổi trả", "sales"),
        ["RETURN_RESTOCK"] = ("Nhập lại hàng khách trả", "sales"),
        ["RETURN_REJECT"] = ("Không nhập lại hàng khách trả", "sales"),
        ["SHIFT_OPEN"] = ("Mở ca", "sales"),
        ["SHIFT_HANDOVER"] = ("Bàn giao ca", "sales"),
        ["SHIFT_CLOSE"] = ("Chốt ca", "sales"),

        // Kho
        ["PURCHASE_ORDER_CREATE"] = ("Lập đơn đặt hàng", "stock"),
        ["PURCHASE_ORDER_UPDATE"] = ("Sửa đơn đặt hàng", "stock"),
        ["PURCHASE_ORDER_SEND"] = ("Gửi đơn đặt hàng", "stock"),
        ["PURCHASE_ORDER_EMAIL"] = ("Gửi email đơn đặt hàng", "stock"),
        ["PURCHASE_ORDER_CANCEL"] = ("Hủy đơn đặt hàng", "stock"),
        ["GOODS_RECEIPT_POST"] = ("Nhập kho", "stock"),
        ["GOODS_RECEIPT_CANCEL"] = ("Hủy phiếu nhập", "stock"),
        ["BATCH_MOVE"] = ("Đổi vị trí lô", "stock"),
        ["DISPOSAL_CREATE"] = ("Lập biên bản hủy", "stock"),
        ["DISPOSAL_UPDATE"] = ("Sửa biên bản hủy", "stock"),
        ["DISPOSAL_POST"] = ("Ghi sổ hủy thuốc", "stock"),
        ["DISPOSAL_CANCEL"] = ("Hủy biên bản hủy", "stock"),
        ["STOCKTAKE_CREATE"] = ("Bắt đầu kiểm kê", "stock"),
        ["STOCKTAKE_SUBMIT"] = ("Nộp biên bản kiểm kê", "stock"),
        ["STOCKTAKE_APPROVE"] = ("Duyệt kiểm kê", "stock"),
        ["STOCKTAKE_REJECT"] = ("Từ chối kiểm kê", "stock"),
        ["STOCKTAKE_ADJUST"] = ("Điều chỉnh tồn theo kiểm kê", "stock"),
        ["STOCKTAKE_DELETE"] = ("Xóa phiếu kiểm kê", "stock"),
        ["RECALL_CREATE"] = ("Tiếp nhận thông báo thu hồi", "stock"),
        ["RECALL_RECOVER"] = ("Ghi thu hồi", "stock"),
        ["RECALL_COMPLETE"] = ("Hoàn tất thu hồi", "stock"),
        ["SUPPLIER_RETURN_CREATE"] = ("Lập phiếu trả nhà cung cấp", "stock"),
        ["SUPPLIER_RETURN_UPDATE"] = ("Sửa phiếu trả nhà cung cấp", "stock"),
        ["SUPPLIER_RETURN_ISSUE"] = ("Xuất trả nhà cung cấp", "stock"),
        ["SUPPLIER_RETURN_CANCEL"] = ("Hủy phiếu trả nhà cung cấp", "stock"),

        // Công nợ nhà cung cấp
        ["SUPPLIER_PAY"] = ("Lập phiếu chi", "debt"),
        ["SUPPLIER_REFUND"] = ("Lập phiếu thu tiền hoàn", "debt"),
        ["SUPPLIER_PAYMENT_VOID"] = ("Hủy phiếu chi, thu", "debt")
    };

    private static readonly Dictionary<string, string> Entities = new(StringComparer.Ordinal)
    {
        ["AspNetUsers"] = "Tài khoản",
        ["Employees"] = "Nhân viên",
        ["RolePermissions"] = "Phân quyền",
        ["Medicines"] = "Thuốc",
        ["Suppliers"] = "Nhà cung cấp",
        ["Customers"] = "Khách hàng",
        ["StorageLocations"] = "Tủ, kệ",
        ["InventoryPolicies"] = "Định mức tồn",
        ["PharmacySettings"] = "Thiết lập",
        ["SalesInvoices"] = "Hóa đơn",
        ["Prescriptions"] = "Đơn thuốc",
        ["Returns"] = "Đổi trả",
        ["ReturnDetails"] = "Đổi trả",
        ["Shifts"] = "Ca làm việc",
        ["PurchaseOrders"] = "Đơn đặt hàng",
        ["GoodsReceipts"] = "Phiếu nhập",
        ["MedicineBatches"] = "Lô thuốc",
        ["StockDisposals"] = "Biên bản hủy",
        ["Stocktakes"] = "Phiếu kiểm kê",
        ["ProductRecalls"] = "Phiếu thu hồi",
        ["SupplierReturns"] = "Phiếu trả nhà cung cấp",
        ["SupplierPayments"] = "Phiếu chi, thu"
    };

    /// <summary>Tên trường hay gặp trong dữ liệu trước / sau.</summary>
    private static readonly Dictionary<string, string> Fields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Status"] = "Trạng thái",
        ["Reason"] = "Lý do",
        ["Notes"] = "Ghi chú",
        ["Note"] = "Ghi chú",
        ["Amount"] = "Số tiền",
        ["TotalAmount"] = "Tổng tiền",
        ["Quantity"] = "Số lượng",
        ["UnitCost"] = "Đơn giá vốn",
        ["UnitPrice"] = "Đơn giá",
        ["SellingPrice"] = "Giá bán",
        ["Email"] = "Email",
        ["FullName"] = "Họ tên",
        ["UserName"] = "Tên đăng nhập",
        ["Role"] = "Vai trò",
        ["PaymentMethod"] = "Hình thức",
        ["TransactionReference"] = "Mã giao dịch",
        ["Lines"] = "Các dòng",
        ["Added"] = "Quyền được bật",
        ["Removed"] = "Quyền bị tắt",
        ["Location"] = "Vị trí",
        ["BatchNumber"] = "Số lô",
        ["MedicineId"] = "Thuốc (mã nội bộ)",
        ["BatchId"] = "Lô (mã nội bộ)"
    };

    public static string Action(string actionType)
        => Actions.TryGetValue(actionType, out var a) ? a.Label : "Thao tác khác";

    public static string? GroupOf(string actionType)
        => Actions.TryGetValue(actionType, out var a) ? a.Group : null;

    public static IReadOnlyList<string> ActionsOf(string groupKey)
        => Actions.Where(a => a.Value.Group == groupKey).Select(a => a.Key).ToList();

    public static string Entity(string entityName) => Entities.TryGetValue(entityName, out var e) ? e : entityName;

    public static string Field(string name) => Fields.TryGetValue(name, out var f) ? f : name;

    public static string Tone(string actionType) => actionType switch
    {
        _ when actionType.EndsWith("_CANCEL", StringComparison.Ordinal) || actionType.EndsWith("_VOID", StringComparison.Ordinal)
               || actionType.EndsWith("_DELETE", StringComparison.Ordinal) || actionType.EndsWith("_REJECT", StringComparison.Ordinal)
               || actionType == "ACCOUNT_LOCK" || actionType == "INVOICE_DISCOUNT_OVER_LIMIT" => "bad",
        "ROLE_PERMISSION_UPDATE" or "OWNER_RECOVERY" or "PASSWORD_RESET" => "warn",
        _ => "calm"
    };
}

public sealed class AuditLogFilter
{
    public const int PageSize = 50;

    public string? From { get; set; }
    public string? To { get; set; }

    /// <summary>Người thực hiện (AspNetUsers.Id).</summary>
    public string? User { get; set; }

    /// <summary>Nhóm thao tác (AuditText.Groups).</summary>
    public string? Group { get; set; }

    /// <summary>Tìm theo mã đối tượng hoặc nội dung (mã phiếu, mã hóa đơn…).</summary>
    public string? Q { get; set; }

    public int Page { get; set; } = 1;

    public bool IsFiltered => From is not null || To is not null || User is not null || Group is not null || Q is not null;
}

public sealed record AuditLogRow(long AuditLogId, DateTime CreatedAt, string ActionType, string EntityName, string? EntityId, string Actor);

public sealed record AuditUserOption(string UserId, string Name);

public sealed class AuditLogListViewModel
{
    public AuditLogFilter Filter { get; init; } = new();
    public IReadOnlyList<AuditLogRow> Rows { get; init; } = Array.Empty<AuditLogRow>();
    public int Total { get; init; }
    public int GrandTotal { get; init; }
    public int PageCount { get; init; }
    public IReadOnlyList<AuditUserOption> Users { get; init; } = Array.Empty<AuditUserOption>();
    public string? FilterError { get; init; }
}

/// <summary>Một trường trong dữ liệu trước / sau.</summary>
public sealed record AuditChange(string Field, string? Before, string? After)
{
    public bool Changed => Before != After;
}

public sealed class AuditLogDetailsViewModel
{
    public long AuditLogId { get; init; }
    public DateTime CreatedAt { get; init; }
    public string ActionType { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string? EntityId { get; init; }
    public string Actor { get; init; } = string.Empty;
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public IReadOnlyList<AuditChange> Changes { get; init; } = Array.Empty<AuditChange>();
    public bool HasBefore { get; init; }
    public bool HasAfter { get; init; }
}
