using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Models.ViewModels.Inventory;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Trả nhà cung cấp 2.3.1 (DEC-44) cho kho đang hoạt động đầu tiên (KHO01).</summary>
public sealed class SupplierReturnService : ISupplierReturnService
{
    private const string CodePrefix = "TNCC";
    private const decimal MaxUnitCost = 1_000_000_000m;

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly StringComparer NameOrder = StringComparer.Create(Vietnamese, ignoreCase: true);

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly string _pharmacyName;

    public SupplierReturnService(PharmacyDbContext db, IAuditLogService audit, IOptions<PharmacyOptions> pharmacy)
    {
        _db = db;
        _audit = audit;
        _pharmacyName = pharmacy.Value.Name;
    }

    // ================================================================== Danh sách

    public async Task<SupplierReturnListViewModel> GetListAsync(SupplierReturnFilter filter, CancellationToken ct = default)
    {
        var query = _db.SupplierReturns.AsNoTracking().AsQueryable();
        byte? status = filter.Status switch
        {
            "draft" => (byte)SupplierReturnStatus.Draft,
            "issued" => (byte)SupplierReturnStatus.Issued,
            "settled" => (byte)SupplierReturnStatus.Settled,
            "cancelled" => (byte)SupplierReturnStatus.Cancelled,
            _ => null
        };
        if (status is null) filter.Status = null;
        else query = query.Where(r => r.Status == status);
        if (filter.SupplierId is not null) query = query.Where(r => r.SupplierId == filter.SupplierId);

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            var folded = SearchText.Fold(text);
            query = query.Where(r => r.SupplierReturnCode.Contains(text)
                                     || EF.Functions.Collate(r.Supplier.SupplierName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                     || EF.Functions.Collate(r.Reason.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                                     || r.SupplierReturnDetails.Any(d => d.Batch.BatchNumber.Contains(text)
                                         || EF.Functions.Collate(d.Batch.Medicine.MedicineName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)SupplierReturnFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await query
            .OrderByDescending(r => r.ReturnDate).ThenByDescending(r => r.SupplierReturnId)
            .Skip((filter.Page - 1) * SupplierReturnFilter.PageSize).Take(SupplierReturnFilter.PageSize)
            .Select(r => new SupplierReturnRow(r.SupplierReturnId, r.SupplierReturnCode, r.ReturnDate, r.Supplier.SupplierName,
                r.GoodsReceipt != null ? r.GoodsReceipt.GoodsReceiptCode : null, r.SupplierReturnDetails.Count(), r.TotalAmount,
                r.SupplierPayments.Where(p => p.PaymentType == (byte)SupplierPaymentType.RefundFromSupplier && p.Status == (byte)PaymentStatus.Succeeded).Sum(p => p.Amount),
                r.Status))
            .ToListAsync(ct);

        return new SupplierReturnListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await _db.SupplierReturns.CountAsync(ct),
            PageCount = pageCount,
            Suppliers = await _db.Suppliers.AsNoTracking().OrderBy(s => s.SupplierName)
                .Select(s => new LookupItem(s.SupplierId, s.SupplierName)).ToListAsync(ct)
        };
    }

    // ================================================================== Chi tiết

    public async Task<SupplierReturnDetailsViewModel?> GetDetailsAsync(int supplierReturnId, CancellationToken ct = default)
    {
        var r = await _db.SupplierReturns.AsNoTracking()
            .Where(x => x.SupplierReturnId == supplierReturnId)
            .Select(x => new
            {
                x.SupplierReturnId, x.SupplierReturnCode, x.Status, x.ReturnDate, x.SupplierId, x.Supplier.SupplierCode, x.Supplier.SupplierName,
                x.Supplier.Phone, x.Supplier.Address, x.GoodsReceiptId,
                ReceiptCode = x.GoodsReceipt != null ? x.GoodsReceipt.GoodsReceiptCode : null,
                CreatedBy = x.CreatedByEmployee.FullName, x.Reason, x.TotalAmount
            })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;

        var lines = await _db.SupplierReturnDetails.AsNoTracking()
            .Where(d => d.SupplierReturnId == supplierReturnId)
            .OrderBy(d => d.SupplierReturnDetailId)
            .Select(d => new
            {
                d.SupplierReturnDetailId, d.BatchId, d.Batch.Medicine.MedicineCode, d.Batch.Medicine.MedicineName, d.Batch.BatchNumber, d.Batch.ExpiryDate,
                d.Quantity, BaseUnit = d.Batch.Medicine.BaseUnit.UnitName, d.UnitCost, d.Reason, OnHand = d.Batch.QuantityOnHand, LotStatus = d.Batch.Status
            })
            .ToListAsync(ct);

        DateTime? issuedAt = null;
        if (r.Status is (byte)SupplierReturnStatus.Issued or (byte)SupplierReturnStatus.Settled && lines.Count > 0)
        {
            var refIds = lines.Select(l => (long)l.SupplierReturnDetailId).ToList();
            issuedAt = await _db.InventoryTransactions.AsNoTracking()
                .Where(t => t.ReferenceType == InventoryReferenceTypes.SupplierReturnDetail && refIds.Contains(t.ReferenceId))
                .MinAsync(t => (DateTime?)t.TransactionDate, ct);
        }

        var refunds = await SupplierPaymentService.PaymentLines(_db.SupplierPayments.AsNoTracking()
                .Where(p => p.SupplierReturnId == supplierReturnId && p.PaymentType == (byte)SupplierPaymentType.RefundFromSupplier))
            .ToListAsync(ct);

        return new SupplierReturnDetailsViewModel
        {
            SupplierReturnId = r.SupplierReturnId,
            Code = r.SupplierReturnCode,
            Status = r.Status,
            ReturnDate = r.ReturnDate,
            SupplierId = r.SupplierId,
            SupplierCode = r.SupplierCode,
            SupplierName = r.SupplierName,
            SupplierPhone = r.Phone,
            SupplierAddress = r.Address,
            GoodsReceiptId = r.GoodsReceiptId,
            GoodsReceiptCode = r.ReceiptCode,
            CreatedByName = r.CreatedBy,
            Reason = r.Reason,
            TotalAmount = r.TotalAmount,
            IssuedAt = issuedAt,
            Lines = lines.Select(l => new SupplierReturnLineView(l.BatchId, l.MedicineCode, l.MedicineName, l.BatchNumber, l.ExpiryDate,
                l.Quantity, l.BaseUnit, l.UnitCost, Math.Round(l.Quantity * l.UnitCost, 2, MidpointRounding.AwayFromZero), l.Reason, l.OnHand, l.LotStatus)).ToList(),
            Refunds = refunds,
            Refunded = refunds.Where(p => p.Status == (byte)PaymentStatus.Succeeded).Sum(p => p.Amount),
            PharmacyName = _pharmacyName
        };
    }

    // ================================================================== Form

    public async Task<SupplierReturnFormViewModel> NewFormAsync(int? supplierId, int? receiptId, int? batchId, CancellationToken ct = default)
    {
        var form = new SupplierReturnFormViewModel { SupplierId = supplierId, GoodsReceiptId = receiptId };
        var warehouseId = await DefaultWarehouseAsync(ct);
        if (batchId is not null)
        {
            var lot = await _db.MedicineBatches.AsNoTracking()
                .Where(b => b.BatchId == batchId && b.WarehouseId == warehouseId && b.QuantityOnHand > 0
                            && b.Status != (byte)BatchStatus.Recalled && b.GoodsReceipt != null)
                .Select(b => new { b.BatchId, b.QuantityOnHand, b.UnitCost, b.GoodsReceiptId, SupplierId = b.GoodsReceipt!.SupplierId })
                .FirstOrDefaultAsync(ct);
            if (lot is not null)
            {
                form.SupplierId = lot.SupplierId;
                form.GoodsReceiptId = lot.GoodsReceiptId;
                form.Lines.Add(new SupplierReturnLineForm { BatchId = lot.BatchId, Quantity = lot.QuantityOnHand, UnitCost = lot.UnitCost });
            }
        }
        else if (receiptId is not null)
        {
            var supplierOfReceipt = await _db.GoodsReceipts.AsNoTracking()
                .Where(r => r.GoodsReceiptId == receiptId && r.Status == (byte)GoodsReceiptStatus.Posted)
                .Select(r => (int?)r.SupplierId).FirstOrDefaultAsync(ct);
            if (supplierOfReceipt is null) form.GoodsReceiptId = null;
            else form.SupplierId = supplierOfReceipt;
        }
        if (form.Lines.Count == 0) form.Lines.Add(new SupplierReturnLineForm());
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task<SupplierReturnFormViewModel?> EditFormAsync(int supplierReturnId, CancellationToken ct = default)
    {
        var r = await _db.SupplierReturns.AsNoTracking().Where(x => x.SupplierReturnId == supplierReturnId)
            .Select(x => new { x.SupplierReturnId, x.SupplierReturnCode, x.SupplierId, x.GoodsReceiptId, x.Reason, x.Status })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;
        var form = new SupplierReturnFormViewModel
        {
            SupplierReturnId = r.SupplierReturnId,
            Code = r.SupplierReturnCode,
            SupplierId = r.SupplierId,
            GoodsReceiptId = r.GoodsReceiptId,
            Reason = r.Reason,
            IsDraft = r.Status == (byte)SupplierReturnStatus.Draft,
            Lines = await _db.SupplierReturnDetails.AsNoTracking().Where(d => d.SupplierReturnId == supplierReturnId)
                .OrderBy(d => d.SupplierReturnDetailId)
                .Select(d => new SupplierReturnLineForm { BatchId = d.BatchId, Quantity = d.Quantity, UnitCost = d.UnitCost, Reason = d.Reason })
                .ToListAsync(ct)
        };
        await PrepareFormAsync(form, ct);
        return form;
    }

    public async Task PrepareFormAsync(SupplierReturnFormViewModel form, CancellationToken ct = default)
    {
        if (form.IsEdit)
        {
            var current = await _db.SupplierReturns.AsNoTracking().Where(x => x.SupplierReturnId == form.SupplierReturnId)
                .Select(x => new { x.SupplierReturnCode, x.SupplierId, x.GoodsReceiptId }).FirstOrDefaultAsync(ct);
            if (current is not null)
            {
                // Nhà cung cấp và phiếu nhập gốc không đổi sau khi lập
                form.Code = current.SupplierReturnCode;
                form.SupplierId = current.SupplierId;
                form.GoodsReceiptId = current.GoodsReceiptId;
            }
        }
        else
        {
            form.Code = await NextCodeAsync(DateTime.Now, ct);
        }

        form.Suppliers = await _db.Suppliers.AsNoTracking().OrderBy(s => s.SupplierName)
            .Select(s => new LookupItem(s.SupplierId, s.SupplierName)).ToListAsync(ct);
        if (form.SupplierId is null)
        {
            form.Receipts = Array.Empty<LookupItem>();
            form.BatchOptions = Array.Empty<SupplierReturnBatchOption>();
            return;
        }

        var supplierId = form.SupplierId.Value;
        form.SupplierName = form.Suppliers.FirstOrDefault(s => s.Id == supplierId)?.Name;
        form.Receipts = await _db.GoodsReceipts.AsNoTracking()
            .Where(r => r.SupplierId == supplierId && r.Status == (byte)GoodsReceiptStatus.Posted)
            .OrderByDescending(r => r.ReceiptDate)
            .Select(r => new LookupItem(r.GoodsReceiptId, r.GoodsReceiptCode + (r.SupplierInvoiceNumber != null ? " · HĐ " + r.SupplierInvoiceNumber : "")))
            .ToListAsync(ct);
        if (form.GoodsReceiptId is not null && form.Receipts.All(r => r.Id != form.GoodsReceiptId)) form.GoodsReceiptId = null;
        form.GoodsReceiptCode = form.GoodsReceiptId is null ? null : form.Receipts.First(r => r.Id == form.GoodsReceiptId).Name;

        var warehouseId = await DefaultWarehouseAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var receiptId = form.GoodsReceiptId;
        var keep = form.Lines.Where(l => l.BatchId is not null).Select(l => l.BatchId!.Value).Distinct().ToList();
        var rows = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.WarehouseId == warehouseId && b.GoodsReceipt != null && b.GoodsReceipt.SupplierId == supplierId
                        && (receiptId == null || b.GoodsReceiptId == receiptId)
                        && ((b.QuantityOnHand > 0 && b.Status != (byte)BatchStatus.Recalled) || keep.Contains(b.BatchId)))
            .Select(b => new
            {
                b.BatchId, b.BatchNumber, b.ExpiryDate, b.QuantityOnHand, b.UnitCost, b.Status, b.Medicine.MedicineCode, b.Medicine.MedicineName,
                BaseUnit = b.Medicine.BaseUnit.UnitName, ReceiptCode = b.GoodsReceipt!.GoodsReceiptCode
            })
            .ToListAsync(ct);
        form.BatchOptions = rows
            .OrderBy(r => r.MedicineName, NameOrder).ThenBy(r => r.ExpiryDate).ThenBy(r => r.BatchId)
            .Select(r => new SupplierReturnBatchOption(r.BatchId,
                r.MedicineName + " · Lô " + r.BatchNumber + " · HSD " + r.ExpiryDate.ToString("dd/MM/yyyy", Vietnamese)
                + " · còn " + Format.Number(r.QuantityOnHand) + " " + r.BaseUnit + " · " + r.ReceiptCode
                + (r.ExpiryDate <= today ? " · đã hết hạn" : "") + (r.Status == (byte)BatchStatus.Recalled ? " · đang thu hồi" : ""),
                r.MedicineCode + " " + r.BatchNumber + " " + r.ReceiptCode, r.QuantityOnHand, r.UnitCost, r.BaseUnit))
            .ToList();
    }

    // ================================================================== Lập / sửa (Nháp)

    public async Task<OperationResult<int>> CreateAsync(SupplierReturnFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult<int>.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa lập phiếu trả được.");
        var clean = await ValidateAsync(form, ct);
        if (clean.Error is not null) return OperationResult<int>.Fail(clean.Error, clean.Field);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var now = DateTime.Now;
            var ret = new SupplierReturn
            {
                SupplierReturnCode = await NextCodeAsync(now, ct),
                SupplierId = clean.SupplierId,
                GoodsReceiptId = clean.GoodsReceiptId,
                CreatedByEmployeeId = employeeId.Value,
                ReturnDate = now,
                Reason = clean.Reason,
                Status = (byte)SupplierReturnStatus.Draft,
                TotalAmount = clean.Total
            };
            foreach (var line in clean.Lines) ret.SupplierReturnDetails.Add(ToDetail(line));
            _db.SupplierReturns.Add(ret);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("SUPPLIER_RETURN_CREATE", "SupplierReturns", ret.SupplierReturnId.ToString(CultureInfo.InvariantCulture), null,
                new { ret.SupplierReturnCode, ret.SupplierId, ret.GoodsReceiptId, ret.TotalAmount, Lines = clean.Lines.Select(l => new { l.BatchId, l.Quantity, l.UnitCost }).ToList() },
                actorUserId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(ret.SupplierReturnId,
                $"Đã lưu phiếu trả {ret.SupplierReturnCode} (nháp). Kiểm tra lại rồi bấm Xuất trả để trừ tồn kho.");
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult<int>.Fail("Không lưu được: mã phiếu trả vừa bị trùng do người khác lập cùng lúc. Bấm Lưu lại.");
        }
    }

    public async Task<OperationResult> UpdateAsync(SupplierReturnFormViewModel form, string actorUserId, CancellationToken ct = default)
    {
        var current = await _db.SupplierReturns.AsNoTracking().Where(r => r.SupplierReturnId == form.SupplierReturnId)
            .Select(r => new { r.SupplierReturnId, r.SupplierReturnCode, r.SupplierId, r.GoodsReceiptId, r.Status }).FirstOrDefaultAsync(ct);
        if (current is null) return OperationResult.Fail("Không tìm thấy phiếu trả nhà cung cấp.");
        if (current.Status != (byte)SupplierReturnStatus.Draft)
            return OperationResult.Fail($"Phiếu {current.SupplierReturnCode} {SupplierReturnStatusText.Of(current.Status).ToLowerInvariant()} nên không sửa được.");

        form.SupplierId = current.SupplierId;
        form.GoodsReceiptId = current.GoodsReceiptId;
        var clean = await ValidateAsync(form, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        var id = current.SupplierReturnId;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var claimed = await _db.SupplierReturns
            .Where(r => r.SupplierReturnId == id && r.Status == (byte)SupplierReturnStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Reason, clean.Reason).SetProperty(r => r.TotalAmount, clean.Total), ct);
        if (claimed == 0) return OperationResult.Fail("Phiếu vừa được xuất trả hoặc hủy ở nơi khác. Tải lại trang.");
        await _db.SupplierReturnDetails.Where(d => d.SupplierReturnId == id).ExecuteDeleteAsync(ct);
        foreach (var line in clean.Lines)
        {
            var detail = ToDetail(line);
            detail.SupplierReturnId = id;
            _db.SupplierReturnDetails.Add(detail);
        }
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("SUPPLIER_RETURN_UPDATE", "SupplierReturns", id.ToString(CultureInfo.InvariantCulture), null,
            new { current.SupplierReturnCode, TotalAmount = clean.Total, Lines = clean.Lines.Select(l => new { l.BatchId, l.Quantity, l.UnitCost }).ToList() },
            actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã lưu phiếu trả {current.SupplierReturnCode}.");
    }

    // ================================================================== Xuất trả (trừ tồn)

    public async Task<OperationResult> IssueAsync(int supplierReturnId, string actorUserId, CancellationToken ct = default)
    {
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        if (employeeId is null) return OperationResult.Fail("Tài khoản chưa gắn hồ sơ nhân viên nên chưa xuất trả được.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var claimed = await _db.SupplierReturns
            .Where(r => r.SupplierReturnId == supplierReturnId && r.Status == (byte)SupplierReturnStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, (byte)SupplierReturnStatus.Issued), ct);
        if (claimed == 0)
        {
            var status = await _db.SupplierReturns.AsNoTracking().Where(r => r.SupplierReturnId == supplierReturnId)
                .Select(r => (byte?)r.Status).FirstOrDefaultAsync(ct);
            return OperationResult.Fail(status is null
                ? "Không tìm thấy phiếu trả nhà cung cấp."
                : $"Phiếu {SupplierReturnStatusText.Of(status.Value).ToLowerInvariant()} nên không xuất trả lại được.");
        }

        var code = await _db.SupplierReturns.AsNoTracking().Where(r => r.SupplierReturnId == supplierReturnId).Select(r => r.SupplierReturnCode).FirstAsync(ct);
        var lines = await _db.SupplierReturnDetails.AsNoTracking()
            .Where(d => d.SupplierReturnId == supplierReturnId)
            .OrderBy(d => d.SupplierReturnDetailId)
            .Select(d => new
            {
                d.SupplierReturnDetailId, d.BatchId, d.Quantity, d.UnitCost, d.Batch.MedicineId, d.Batch.WarehouseId, d.Batch.BatchNumber,
                d.Batch.Medicine.MedicineName, BaseUnit = d.Batch.Medicine.BaseUnit.UnitName
            })
            .ToListAsync(ct);
        if (lines.Count == 0) return OperationResult.Fail("Phiếu chưa có lô nào. Sửa phiếu rồi xuất trả.");

        var recalled = (byte)BatchStatus.Recalled;
        var now = DateTime.Now;
        foreach (var line in lines)
        {
            var batchId = line.BatchId;
            var quantity = line.Quantity;
            var updated = await _db.MedicineBatches
                .Where(b => b.BatchId == batchId && b.Status != recalled && b.QuantityOnHand >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand - quantity), ct);
            if (updated == 0)
            {
                var lot = await _db.MedicineBatches.AsNoTracking().Where(b => b.BatchId == batchId)
                    .Select(b => new { b.QuantityOnHand, b.Status }).FirstAsync(ct);
                _db.ChangeTracker.Clear();
                var why = lot.Status == recalled
                    ? "đang thu hồi"
                    : $"chỉ còn {Format.Number(lot.QuantityOnHand)} {line.BaseUnit}, phiếu ghi trả {Format.Number(quantity)}";
                return OperationResult.Fail($"Chưa xuất trả được: {line.MedicineName} lô {line.BatchNumber} {why}. Sửa phiếu rồi xuất trả lại.");
            }
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                MedicineId = line.MedicineId,
                BatchId = batchId,
                WarehouseId = line.WarehouseId,
                TransactionType = (byte)InventoryTransactionType.SupplierReturn,
                QuantityChange = -quantity,
                ReferenceType = InventoryReferenceTypes.For(InventoryTransactionType.SupplierReturn),
                ReferenceId = line.SupplierReturnDetailId,
                TransactionDate = now,
                CreatedByEmployeeId = employeeId,
                Notes = "Trả nhà cung cấp — " + code
            });
        }

        var total = lines.Sum(l => Math.Round(l.Quantity * l.UnitCost, 2, MidpointRounding.AwayFromZero));
        // Không có tiền hoàn (trả miễn phí, đổi hàng) thì quyết toán ngay
        var newStatus = total == 0 ? (byte)SupplierReturnStatus.Settled : (byte)SupplierReturnStatus.Issued;
        try
        {
            await _db.SaveChangesAsync(ct);
            await _db.SupplierReturns.Where(r => r.SupplierReturnId == supplierReturnId)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.TotalAmount, total).SetProperty(r => r.Status, newStatus), ct);
            await _audit.WriteAsync("SUPPLIER_RETURN_ISSUE", "SupplierReturns", supplierReturnId.ToString(CultureInfo.InvariantCulture),
                new { Status = (byte)SupplierReturnStatus.Draft },
                new { Status = newStatus, SupplierReturnCode = code, TotalAmount = total, Lines = lines.Select(l => new { l.BatchId, l.BatchNumber, l.Quantity }).ToList() },
                actorUserId, ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail("Chưa xuất trả được do dữ liệu vừa thay đổi. Tải lại trang rồi thử lại.");
        }
        return OperationResult.Ok(total == 0
            ? $"Đã xuất trả phiếu {code}: trừ tồn {lines.Count} lô. Phiếu không có tiền hoàn nên đã quyết toán."
            : $"Đã xuất trả phiếu {code}: trừ tồn {lines.Count} lô. Khi nhà cung cấp hoàn tiền, lập phiếu thu trên phiếu này.");
    }

    // ================================================================== Hủy phiếu nháp

    public async Task<OperationResult> CancelAsync(int supplierReturnId, string? reason, string actorUserId, CancellationToken ct = default)
    {
        var why = AccountHelpers.NormalizeFullName(reason);
        if (why.Length == 0) return OperationResult.Fail("Nhập lý do hủy phiếu.");
        if (why.Length > 300) return OperationResult.Fail("Lý do hủy phiếu tối đa 300 ký tự.");

        var current = await _db.SupplierReturns.AsNoTracking().Where(r => r.SupplierReturnId == supplierReturnId)
            .Select(r => new { r.SupplierReturnCode, r.Status, r.Reason }).FirstOrDefaultAsync(ct);
        if (current is null) return OperationResult.Fail("Không tìm thấy phiếu trả nhà cung cấp.");
        if (current.Status != (byte)SupplierReturnStatus.Draft)
            return OperationResult.Fail($"Phiếu {current.SupplierReturnCode} {SupplierReturnStatusText.Of(current.Status).ToLowerInvariant()} nên không hủy được.");

        // Bảng phiếu trả không có cột ghi chú: lý do hủy nối vào lý do phiếu (tối đa 500 ký tự) và ghi đủ trong nhật ký
        var text = current.Reason + "\nHủy phiếu: " + why;
        if (text.Length > 500) text = text[..500];

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var updated = await _db.SupplierReturns
            .Where(r => r.SupplierReturnId == supplierReturnId && r.Status == (byte)SupplierReturnStatus.Draft)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, (byte)SupplierReturnStatus.Cancelled).SetProperty(r => r.Reason, text), ct);
        if (updated == 0) return OperationResult.Fail("Phiếu vừa được xuất trả hoặc hủy ở nơi khác. Tải lại trang.");
        await _audit.WriteAsync("SUPPLIER_RETURN_CANCEL", "SupplierReturns", supplierReturnId.ToString(CultureInfo.InvariantCulture),
            new { Status = (byte)SupplierReturnStatus.Draft }, new { Status = (byte)SupplierReturnStatus.Cancelled, Reason = why }, actorUserId, ct);
        await tx.CommitAsync(ct);
        return OperationResult.Ok($"Đã hủy phiếu trả {current.SupplierReturnCode}.");
    }

    // ================================================================== Kiểm tra form

    private sealed record CleanLine(int BatchId, decimal Quantity, decimal UnitCost, string? Reason);

    private sealed record Clean(int SupplierId, int? GoodsReceiptId, string Reason, List<CleanLine> Lines, decimal Total, string? Error = null, string? Field = null)
    {
        public static Clean Fail(string error, string? field = null) => new(0, null, string.Empty, new List<CleanLine>(), 0m, error, field);
    }

    private async Task<Clean> ValidateAsync(SupplierReturnFormViewModel form, CancellationToken ct)
    {
        if (form.SupplierId is null) return Clean.Fail("Chọn nhà cung cấp.", nameof(SupplierReturnFormViewModel.SupplierId));
        var supplierId = form.SupplierId.Value;
        if (!await _db.Suppliers.AnyAsync(s => s.SupplierId == supplierId, ct))
            return Clean.Fail("Chọn một nhà cung cấp trong danh sách.", nameof(SupplierReturnFormViewModel.SupplierId));
        var receiptId = form.GoodsReceiptId;
        if (receiptId is not null && !await _db.GoodsReceipts.AnyAsync(r => r.GoodsReceiptId == receiptId && r.SupplierId == supplierId
                && r.Status == (byte)GoodsReceiptStatus.Posted, ct))
            return Clean.Fail("Phiếu nhập gốc phải là phiếu đã nhập kho của nhà cung cấp này.", nameof(SupplierReturnFormViewModel.GoodsReceiptId));

        var reason = AccountHelpers.NormalizeFullName(form.Reason);
        if (reason.Length == 0) return Clean.Fail("Nhập lý do trả hàng.", nameof(SupplierReturnFormViewModel.Reason));
        if (reason.Length > 500) return Clean.Fail("Lý do trả hàng tối đa 500 ký tự.", nameof(SupplierReturnFormViewModel.Reason));

        var raw = form.Lines.Where(l => l.BatchId is not null || l.Quantity is not null || l.UnitCost is not null || !string.IsNullOrWhiteSpace(l.Reason)).ToList();
        if (raw.Count == 0) return Clean.Fail("Thêm ít nhất một lô cần trả.", nameof(SupplierReturnFormViewModel.Lines));
        if (raw.Count > SupplierReturnFormViewModel.MaxLines)
            return Clean.Fail($"Một phiếu tối đa {SupplierReturnFormViewModel.MaxLines} lô.", nameof(SupplierReturnFormViewModel.Lines));

        var warehouseId = await DefaultWarehouseAsync(ct);
        var ids = raw.Where(l => l.BatchId is not null).Select(l => l.BatchId!.Value).Distinct().ToList();
        var lots = await _db.MedicineBatches.AsNoTracking()
            .Where(b => ids.Contains(b.BatchId))
            .Select(b => new
            {
                b.BatchId, b.BatchNumber, b.WarehouseId, b.QuantityOnHand, b.UnitCost, b.Status, b.GoodsReceiptId,
                SupplierId = b.GoodsReceipt != null ? (int?)b.GoodsReceipt.SupplierId : null,
                b.Medicine.MedicineName, BaseUnit = b.Medicine.BaseUnit.UnitName
            })
            .ToDictionaryAsync(b => b.BatchId, ct);

        var seen = new HashSet<int>();
        var lines = new List<CleanLine>();
        for (var i = 0; i < raw.Count; i++)
        {
            var l = raw[i];
            var n = i + 1;
            const string field = nameof(SupplierReturnFormViewModel.Lines);
            if (l.BatchId is null) return Clean.Fail($"Dòng {n}: chọn lô cần trả.", field);
            if (!lots.TryGetValue(l.BatchId.Value, out var lot)) return Clean.Fail($"Dòng {n}: không tìm thấy lô đã chọn.", field);
            var label = $"Dòng {n} ({lot.MedicineName}, lô {lot.BatchNumber})";
            if (!seen.Add(lot.BatchId)) return Clean.Fail($"{label}: lô này đã có ở dòng khác.", field);
            if (lot.WarehouseId != warehouseId) return Clean.Fail($"{label}: lô thuộc kho khác.", field);
            if (lot.SupplierId != supplierId) return Clean.Fail($"{label}: lô không nhập từ nhà cung cấp này.", field);
            if (receiptId is not null && lot.GoodsReceiptId != receiptId) return Clean.Fail($"{label}: lô không thuộc phiếu nhập gốc đã chọn.", field);
            if (lot.Status == (byte)BatchStatus.Recalled) return Clean.Fail($"{label}: lô đang thu hồi, xử lý ở phần Thu hồi.", field);

            if (l.Quantity is null || l.Quantity <= 0) return Clean.Fail($"{label}: nhập số lượng trả lớn hơn 0.", field);
            var quantity = l.Quantity.Value;
            if (decimal.Round(quantity, 3) != quantity) return Clean.Fail($"{label}: số lượng tối đa 3 chữ số sau dấu thập phân.", field);
            if (quantity > lot.QuantityOnHand) return Clean.Fail($"{label}: lô chỉ còn {Format.Number(lot.QuantityOnHand)} {lot.BaseUnit}.", field);

            var cost = l.UnitCost ?? lot.UnitCost;
            if (cost < 0) return Clean.Fail($"{label}: đơn giá trả không được âm.", field);
            if (cost > MaxUnitCost) return Clean.Fail($"{label}: đơn giá trả quá lớn.", field);
            if (decimal.Round(cost, 2) != cost) return Clean.Fail($"{label}: đơn giá trả tối đa 2 chữ số sau dấu thập phân.", field);

            var lineReason = string.IsNullOrWhiteSpace(l.Reason) ? null : l.Reason.Trim();
            if (lineReason is not null && lineReason.Length > 500) return Clean.Fail($"{label}: ghi chú tối đa 500 ký tự.", field);
            lines.Add(new CleanLine(lot.BatchId, quantity, cost, lineReason));
        }
        var total = lines.Sum(l => Math.Round(l.Quantity * l.UnitCost, 2, MidpointRounding.AwayFromZero));
        return new Clean(supplierId, receiptId, reason, lines, total);
    }

    // ================================================================== Dùng chung

    private static SupplierReturnDetail ToDetail(CleanLine line) => new()
    {
        BatchId = line.BatchId,
        Quantity = line.Quantity,
        UnitCost = line.UnitCost,
        Reason = line.Reason
    };

    private Task<int?> DefaultWarehouseAsync(CancellationToken ct)
        => _db.Warehouses.AsNoTracking().Where(w => w.IsActive).OrderBy(w => w.WarehouseId)
            .Select(w => (int?)w.WarehouseId).FirstOrDefaultAsync(ct);

    private Task<int?> EmployeeIdOfAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId && e.IsActive).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    /// <summary>TNCC + yyMMdd + 3 số nối tiếp trong ngày (giống dữ liệu mẫu TNCC261020001).</summary>
    private async Task<string> NextCodeAsync(DateTime at, CancellationToken ct)
    {
        var prefix = CodePrefix + at.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var codes = await _db.SupplierReturns.AsNoTracking().Where(r => r.SupplierReturnCode.StartsWith(prefix))
            .Select(r => r.SupplierReturnCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return prefix + (max + 1).ToString("D3", CultureInfo.InvariantCulture);
    }
}
