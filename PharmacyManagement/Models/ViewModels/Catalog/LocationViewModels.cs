using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PharmacyManagement.Models.ViewModels.Catalog;

/// <summary>Một kệ trên cây tủ – kệ. <see cref="Deletable"/>: chưa từng dùng (không lô, phiếu nhập, kiểm kê, sổ nhiệt độ nào gắn với kệ).</summary>
public sealed record ShelfNode(int LocationId, string Code, string Name, string? Description, int BatchCount, bool Deletable);

/// <summary>Một tủ và các kệ trực thuộc. <see cref="BatchCount"/>: số lô còn hàng trên các kệ của tủ.</summary>
public sealed record CabinetNode(int LocationId, string Code, string Name, string? Description, int BatchCount, bool Deletable, IReadOnlyList<ShelfNode> Shelves);

public sealed class LocationListViewModel
{
    public IReadOnlyList<CabinetNode> Cabinets { get; init; } = Array.Empty<CabinetNode>();
    public int ShelfCount => Cabinets.Sum(c => c.Shelves.Count);

    /// <summary>Có quyền xem Tồn kho — số lô trên kệ mở được danh sách lô.</summary>
    public bool CanViewStock { get; set; }
}

/// <summary>Form thêm / sửa tủ hoặc kệ (1.5.1). Loại (tủ / kệ) chọn khi thêm, không đổi khi sửa.</summary>
public sealed class LocationFormViewModel
{
    public const string CodePattern = @"^[A-Za-z0-9][A-Za-z0-9-]*$";

    public int? LocationId { get; set; }

    /// <summary>1 Tủ, 2 Kệ.</summary>
    public byte LocationType { get; set; } = (byte)Enums.LocationType.Shelf;

    [Required(ErrorMessage = "Nhập mã.")]
    [StringLength(30, ErrorMessage = "Mã tối đa 30 ký tự.")]
    [RegularExpression(CodePattern, ErrorMessage = "Mã chỉ gồm chữ không dấu, chữ số và dấu gạch ngang, ví dụ T01-K1.")]
    [Display(Name = "Mã")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Nhập tên.")]
    [StringLength(100, ErrorMessage = "Tên tối đa 100 ký tự.")]
    [Display(Name = "Tên")]
    public string? Name { get; set; }

    [StringLength(300, ErrorMessage = "Mô tả tối đa 300 ký tự.")]
    [Display(Name = "Mô tả")]
    public string? Description { get; set; }

    /// <summary>Tủ chứa kệ (bắt buộc với kệ, bỏ trống với tủ).</summary>
    [Display(Name = "Thuộc tủ")]
    public int? ParentLocationId { get; set; }

    // ---- Hiển thị (server điền) ----
    public bool IsEdit => LocationId is not null;
    public bool IsShelf => LocationType == (byte)Enums.LocationType.Shelf;
    public IReadOnlyList<LookupItem> CabinetOptions { get; set; } = Array.Empty<LookupItem>();

    /// <summary>Đang sửa và tủ / kệ chưa từng dùng — hiện nút Xóa.</summary>
    [BindNever]
    public bool Deletable { get; set; }
}
