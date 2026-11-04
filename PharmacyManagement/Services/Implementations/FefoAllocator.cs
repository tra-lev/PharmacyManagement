using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class FefoAllocator : IFefoAllocator
{
    private readonly PharmacyDbContext _db;

    public FefoAllocator(PharmacyDbContext db)
    {
        _db = db;
    }

    public async Task<OperationResult<int>> AllocateInvoiceAsync(int invoiceId, string invoiceCode, int employeeId, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var details = await _db.SalesInvoiceDetails.AsNoTracking()
            .Where(d => d.InvoiceId == invoiceId)
            .OrderBy(d => d.InvoiceDetailId)
            .Select(d => new
            {
                d.InvoiceDetailId, d.MedicineId, d.Quantity, d.ConversionToBase,
                d.Medicine.MedicineName, BaseUnitName = d.Medicine.BaseUnit.UnitName
            })
            .ToListAsync(ct);
        if (details.Count == 0) return OperationResult<int>.Fail("Hóa đơn chưa có dòng thuốc nào.");

        var allocations = new List<(SalesIssueAllocation Allocation, int MedicineId, int WarehouseId)>();
        var now = DateTime.Now;

        foreach (var d in details)
        {
            var need = Math.Round(d.Quantity * d.ConversionToBase, 3, MidpointRounding.AwayFromZero);
            var medicineId = d.MedicineId;

            // 2.3.4.1 FEFO: lô Khả dụng, còn hạn, còn hàng — hạn gần nhất trước, cùng hạn thì lô nhập trước
            var candidates = await _db.MedicineBatches.AsNoTracking()
                .Where(b => b.MedicineId == medicineId && b.Status == (byte)BatchStatus.Available
                            && b.ExpiryDate > today && b.QuantityOnHand > 0)
                .OrderBy(b => b.ExpiryDate).ThenBy(b => b.BatchId)
                .Select(b => new { b.BatchId, b.QuantityOnHand, b.UnitCost, b.WarehouseId })
                .ToListAsync(ct);

            foreach (var c in candidates)
            {
                if (need <= 0) break;
                var take = Math.Min(need, c.QuantityOnHand);
                var taken = await TryTakeAsync(c.BatchId, take, today, ct);
                if (!taken)
                {
                    // Quầy khác vừa bán bớt lô này: đọc lại số còn (đã bị khóa dòng trong transaction này) và thử một lần nữa
                    var fresh = await _db.MedicineBatches.AsNoTracking()
                        .Where(b => b.BatchId == c.BatchId && b.Status == (byte)BatchStatus.Available && b.ExpiryDate > today)
                        .Select(b => (decimal?)b.QuantityOnHand)
                        .FirstOrDefaultAsync(ct) ?? 0;
                    take = Math.Min(need, fresh);
                    if (take <= 0 || !await TryTakeAsync(c.BatchId, take, today, ct)) continue;
                }

                allocations.Add((new SalesIssueAllocation
                {
                    InvoiceDetailId = d.InvoiceDetailId,
                    BatchId = c.BatchId,
                    QuantityBase = take,
                    UnitCostAtSale = c.UnitCost,
                    AllocatedAt = now
                }, medicineId, c.WarehouseId));
                need -= take;
            }

            if (need > 0)
            {
                _db.ChangeTracker.Clear();
                return OperationResult<int>.Fail(
                    $"{d.MedicineName} không đủ hàng: còn thiếu {Format.Number(need)} {d.BaseUnitName}. Bấm “Sửa thuốc” để giảm số lượng.");
            }
        }

        // 2.3.4.2: phiếu xuất bán theo lô + sổ kho (cùng transaction với việc trừ tồn ở trên)
        foreach (var a in allocations) _db.SalesIssueAllocations.Add(a.Allocation);
        await _db.SaveChangesAsync(ct);

        foreach (var a in allocations)
        {
            _db.InventoryTransactions.Add(new InventoryTransaction
            {
                MedicineId = a.MedicineId,
                BatchId = a.Allocation.BatchId,
                WarehouseId = a.WarehouseId,
                TransactionType = (byte)InventoryTransactionType.SaleIssue,
                QuantityChange = -a.Allocation.QuantityBase,
                ReferenceType = InventoryReferenceTypes.For(InventoryTransactionType.SaleIssue),
                ReferenceId = a.Allocation.AllocationId,
                TransactionDate = now,
                CreatedByEmployeeId = employeeId,
                Notes = "Xuất bán — " + invoiceCode
            });
        }
        await _db.SaveChangesAsync(ct);

        return OperationResult<int>.Ok(allocations.Count);
    }

    /// <summary>DB-07: trừ tồn chỉ khi lô còn Khả dụng, còn hạn và còn đủ số lượng. false = không trừ được.</summary>
    private async Task<bool> TryTakeAsync(int batchId, decimal quantity, DateOnly today, CancellationToken ct)
    {
        var updated = await _db.MedicineBatches
            .Where(b => b.BatchId == batchId && b.Status == (byte)BatchStatus.Available
                        && b.ExpiryDate > today && b.QuantityOnHand >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.QuantityOnHand, b => b.QuantityOnHand - quantity), ct);
        return updated == 1;
    }
}
