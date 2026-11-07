using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Reports;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

/// <summary>Trạng thái lần chạy liên thông gần nhất (dùng chung cho tác vụ chạy ngầm và trang quản lý).</summary>
public sealed class DqgSyncState
{
    private readonly object _lock = new();
    private DateTime? _lastRunAt;
    private bool? _online;

    public (DateTime? LastRunAt, bool? Online) Snapshot()
    {
        lock (_lock) return (_lastRunAt, _online);
    }

    public void Record(DateTime at, bool online)
    {
        lock (_lock)
        {
            _lastRunAt = at;
            _online = online;
        }
    }
}

/// <summary>Liên thông Dược quốc gia — mô phỏng (DEC-50).</summary>
public sealed class DqgSyncService : IDqgSyncService
{
    /// <summary>Máy chủ dùng để kiểm tra còn kết nối Internet (chỉ phân giải tên miền, không gửi dữ liệu).</summary>
    private const string ProbeHost = "dav.gov.vn";

    private const int MaxPerRun = 200;
    private const int MaxError = 2000;

    /// <summary>Một lần chạy tại một thời điểm (tác vụ chạy ngầm và nút trên trang không chạy chồng nhau).</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;
    private readonly DqgSyncState _state;
    private readonly string _pharmacyName;

    public DqgSyncService(PharmacyDbContext db, IAuditLogService audit, DqgSyncState state, IOptions<PharmacyOptions> pharmacy)
    {
        _db = db;
        _audit = audit;
        _state = state;
        _pharmacyName = pharmacy.Value.Name;
    }

    // ================================================================== Chạy định kỳ

    public async Task<DqgRunResult> RunAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var queued = await QueueNewAsync(ct);
            var online = await IsOnlineAsync(ct);
            _state.Record(DateTime.Now, online);
            if (!online)
            {
                var waiting = await PendingQuery().CountAsync(ct);
                return new DqgRunResult(false, queued, 0, 0, waiting);
            }

            var logs = await _db.IntegrationLogs
                .Where(l => DqgText.Types.Contains(l.IntegrationType) && l.Status == (byte)IntegrationStatus.Pending)
                .OrderBy(l => l.CreatedAt).ThenBy(l => l.IntegrationLogId)
                .Take(MaxPerRun)
                .ToListAsync(ct);
            int sent = 0, failed = 0;
            foreach (var log in logs)
            {
                if (await SendAsync(log, ct)) sent++;
                else failed++;
            }
            await _db.SaveChangesAsync(ct);
            return new DqgRunResult(true, queued, sent, failed, await PendingQuery().CountAsync(ct));
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>4.4.1: chứng từ có thuốc liên thông chưa có gói tin → tạo gói tin Chờ gửi (lưu tại máy chủ nhà thuốc).</summary>
    private async Task<int> QueueNewAsync(CancellationToken ct)
    {
        var receiptIds = await NotQueuedReceipts().OrderBy(id => id).Take(MaxPerRun).ToListAsync(ct);
        var invoiceIds = await NotQueuedInvoices().OrderBy(id => id).Take(MaxPerRun).ToListAsync(ct);
        var now = DateTime.Now;
        var count = 0;
        foreach (var id in receiptIds)
        {
            var packet = await BuildImportAsync(id, ct);
            if (packet is null) continue;
            _db.IntegrationLogs.Add(NewLog(DqgText.ImportType, DqgText.ImportRef, id, packet, now));
            count++;
        }
        foreach (var id in invoiceIds)
        {
            var packet = await BuildSaleAsync(id, ct);
            if (packet is null) continue;
            _db.IntegrationLogs.Add(NewLog(DqgText.SaleType, DqgText.SaleRef, id, packet, now));
            count++;
        }
        if (count > 0) await _db.SaveChangesAsync(ct);
        return count;
    }

    private IQueryable<int> NotQueuedReceipts()
        => _db.GoodsReceipts.AsNoTracking()
            .Where(r => r.Status == (byte)GoodsReceiptStatus.Posted
                        && _db.GoodsReceiptDetails.Any(d => d.GoodsReceiptId == r.GoodsReceiptId && d.Medicine.IsInterconnected)
                        && !_db.IntegrationLogs.Any(l => l.IntegrationType == DqgText.ImportType && l.ReferenceType == DqgText.ImportRef
                                                          && l.ReferenceId == r.GoodsReceiptId))
            .Select(r => r.GoodsReceiptId);

    private IQueryable<int> NotQueuedInvoices()
        => _db.SalesInvoices.AsNoTracking()
            .Where(i => i.Status == (byte)InvoiceStatus.Paid
                        && _db.SalesInvoiceDetails.Any(d => d.InvoiceId == i.InvoiceId && d.Medicine.IsInterconnected)
                        && !_db.IntegrationLogs.Any(l => l.IntegrationType == DqgText.SaleType && l.ReferenceType == DqgText.SaleRef
                                                          && l.ReferenceId == i.InvoiceId))
            .Select(i => i.InvoiceId);

    private IQueryable<IntegrationLog> PendingQuery()
        => _db.IntegrationLogs.AsNoTracking()
            .Where(l => DqgText.Types.Contains(l.IntegrationType) && l.Status == (byte)IntegrationStatus.Pending);

    private static IntegrationLog NewLog(string type, string referenceType, int referenceId, DqgPacket packet, DateTime now) => new()
    {
        IntegrationType = type,
        ReferenceType = referenceType,
        ReferenceId = referenceId,
        RequestPayload = JsonSerializer.Serialize(packet, Json),
        Status = (byte)IntegrationStatus.Pending,
        RetryCount = 0,
        CreatedAt = now
    };

    /// <summary>4.4.2.1 / 4.4.2.2: gửi mô phỏng. Dữ liệu thiếu thì Dược quốc gia từ chối (lỗi), đủ thì tiếp nhận.</summary>
    private async Task<bool> SendAsync(IntegrationLog log, CancellationToken ct)
    {
        var packet = Read(log.RequestPayload);
        if (packet is null || string.IsNullOrEmpty(packet.SoChungTu))
        {
            // Gói tin cũ (dữ liệu mẫu) chỉ có mã chứng từ: dựng lại từ dữ liệu hiện tại
            packet = await BuildAsync(log, ct);
            if (packet is not null) log.RequestPayload = JsonSerializer.Serialize(packet, Json);
        }

        var now = DateTime.Now;
        log.LastAttemptAt = now;
        var errors = packet is null ? new List<string> { "Không tìm thấy chứng từ gốc." } : Validate(packet, log.IntegrationType);
        if (errors.Count > 0)
        {
            var message = "Dược quốc gia từ chối (mô phỏng): " + string.Join("; ", errors);
            log.Status = (byte)IntegrationStatus.Failed;
            log.RetryCount++;
            log.ErrorMessage = message.Length <= MaxError ? message : message[..MaxError];
            log.ResponsePayload = JsonSerializer.Serialize(new { ketQua = "LOI", loi = errors }, Json);
            return false;
        }

        log.Status = (byte)IntegrationStatus.Succeeded;
        log.ErrorMessage = null;
        log.ResponsePayload = JsonSerializer.Serialize(new
        {
            ketQua = "OK",
            maTiepNhan = "DQG-" + now.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + "-" + log.IntegrationLogId.ToString(CultureInfo.InvariantCulture)
        }, Json);
        return true;
    }

    private static List<string> Validate(DqgPacket packet, string type)
    {
        var errors = new List<string>();
        if (packet.Thuoc.Count == 0) errors.Add("chứng từ không còn dòng thuốc liên thông");
        foreach (var line in packet.Thuoc)
        {
            if (string.IsNullOrWhiteSpace(line.SoDangKy)) errors.Add(line.TenThuoc + " chưa có số đăng ký");
            if (string.IsNullOrWhiteSpace(line.SoLo)) errors.Add(line.TenThuoc + " thiếu số lô");
            if (string.IsNullOrWhiteSpace(line.HanDung)) errors.Add(line.TenThuoc + " thiếu hạn dùng");
            if (line.SoLuong <= 0) errors.Add(line.TenThuoc + " có số lượng không hợp lệ");
        }
        if (type == DqgText.SaleType)
        {
            if (packet.DonThuoc is null && packet.Thuoc.Any(l => l.KeDon)) errors.Add("hóa đơn có thuốc kê đơn nhưng không gắn đơn thuốc");
            if (packet.DonThuoc is not null && string.IsNullOrWhiteSpace(packet.DonThuoc.BacSi)) errors.Add("đơn thuốc thiếu tên bác sĩ");
        }
        return errors.Distinct().ToList();
    }

    private async Task<bool> IsOnlineAsync(CancellationToken ct)
    {
        if (!NetworkInterface.GetIsNetworkAvailable()) return false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync(ProbeHost, timeout.Token);
            return addresses.Length > 0;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or OperationCanceledException or ArgumentException)
        {
            return false;
        }
    }

    // ================================================================== Dựng gói tin

    private Task<DqgPacket?> BuildAsync(IntegrationLog log, CancellationToken ct)
        => log.IntegrationType == DqgText.ImportType
            ? BuildImportAsync((int)log.ReferenceId, ct)
            : BuildSaleAsync((int)log.ReferenceId, ct);

    private async Task<DqgPacket?> BuildImportAsync(int receiptId, CancellationToken ct)
    {
        var r = await _db.GoodsReceipts.AsNoTracking().Where(x => x.GoodsReceiptId == receiptId)
            .Select(x => new { x.GoodsReceiptCode, x.ReceiptDate, x.SupplierInvoiceNumber, x.Supplier.SupplierName, x.Supplier.TaxCode })
            .FirstOrDefaultAsync(ct);
        if (r is null) return null;

        var lines = await _db.GoodsReceiptDetails.AsNoTracking()
            .Where(d => d.GoodsReceiptId == receiptId && d.Medicine.IsInterconnected && d.QuantityReceived > d.DamagedQuantity)
            .OrderBy(d => d.GoodsReceiptDetailId)
            .Select(d => new
            {
                d.Medicine.MedicineCode, d.Medicine.MedicineName, d.Medicine.RegistrationNumber, d.Medicine.IsPrescription,
                d.BatchNumber, d.ExpiryDate, Quantity = (d.QuantityReceived - d.DamagedQuantity) * d.ConversionToBase,
                BaseUnit = d.Medicine.BaseUnit.UnitName, d.UnitCost, d.ConversionToBase
            })
            .ToListAsync(ct);

        return new DqgPacket
        {
            Loai = DqgText.ImportType,
            CoSo = _pharmacyName,
            SoChungTu = r.GoodsReceiptCode,
            Ngay = r.ReceiptDate.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            DoiTac = r.SupplierName,
            MaSoThue = r.TaxCode,
            SoHoaDonNcc = r.SupplierInvoiceNumber,
            Thuoc = lines.Select(l => new DqgLine
            {
                MaThuoc = l.MedicineCode,
                TenThuoc = l.MedicineName,
                SoDangKy = l.RegistrationNumber,
                SoLo = l.BatchNumber,
                HanDung = l.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SoLuong = l.Quantity,
                DonViTinh = l.BaseUnit,
                DonGia = l.ConversionToBase == 0 ? l.UnitCost : Math.Round(l.UnitCost / l.ConversionToBase, 2),
                KeDon = l.IsPrescription
            }).ToList()
        };
    }

    private async Task<DqgPacket?> BuildSaleAsync(int invoiceId, CancellationToken ct)
    {
        var okPayment = (byte)PaymentStatus.Succeeded;
        var i = await _db.SalesInvoices.AsNoTracking().Where(x => x.InvoiceId == invoiceId)
            .Select(x => new
            {
                x.InvoiceCode,
                PaidAt = x.Payments.Where(p => p.Status == okPayment).Max(p => (DateTime?)p.PaidAt) ?? x.InvoiceDate,
                Customer = x.Customer != null ? x.Customer.FullName : null,
                Prescription = x.Prescription == null ? null : new
                {
                    x.Prescription.PrescriptionCode, x.Prescription.DoctorName, x.Prescription.ClinicName, x.Prescription.PrescriptionDate
                }
            })
            .FirstOrDefaultAsync(ct);
        if (i is null) return null;

        var lines = await _db.SalesIssueAllocations.AsNoTracking()
            .Where(a => a.InvoiceDetail.InvoiceId == invoiceId && a.InvoiceDetail.Medicine.IsInterconnected)
            .OrderBy(a => a.AllocationId)
            .Select(a => new
            {
                a.InvoiceDetail.Medicine.MedicineCode, a.InvoiceDetail.Medicine.MedicineName, a.InvoiceDetail.Medicine.RegistrationNumber,
                a.InvoiceDetail.Medicine.IsPrescription, a.Batch.BatchNumber, a.Batch.ExpiryDate, a.QuantityBase,
                BaseUnit = a.InvoiceDetail.Medicine.BaseUnit.UnitName, a.InvoiceDetail.UnitPrice, a.InvoiceDetail.ConversionToBase
            })
            .ToListAsync(ct);

        return new DqgPacket
        {
            Loai = DqgText.SaleType,
            CoSo = _pharmacyName,
            SoChungTu = i.InvoiceCode,
            Ngay = i.PaidAt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            DoiTac = i.Customer,
            DonThuoc = i.Prescription is null ? null : new DqgPrescription
            {
                MaDon = i.Prescription.PrescriptionCode,
                BacSi = i.Prescription.DoctorName,
                CoSoKham = i.Prescription.ClinicName,
                NgayKe = i.Prescription.PrescriptionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            },
            Thuoc = lines.Select(l => new DqgLine
            {
                MaThuoc = l.MedicineCode,
                TenThuoc = l.MedicineName,
                SoDangKy = l.RegistrationNumber,
                SoLo = l.BatchNumber,
                HanDung = l.ExpiryDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SoLuong = l.QuantityBase,
                DonViTinh = l.BaseUnit,
                DonGia = l.ConversionToBase == 0 ? l.UnitPrice : Math.Round(l.UnitPrice / l.ConversionToBase, 2),
                KeDon = l.IsPrescription
            }).ToList()
        };
    }

    private static DqgPacket? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<DqgPacket>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ================================================================== 4.4.2.3 Xử lý lỗi gửi

    public async Task<OperationResult> ResendAsync(long logId, string actorUserId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var log = await _db.IntegrationLogs.FirstOrDefaultAsync(l => l.IntegrationLogId == logId && DqgText.Types.Contains(l.IntegrationType), ct);
            if (log is null) return OperationResult.Fail("Không tìm thấy gói tin liên thông.");
            if (log.Status != (byte)IntegrationStatus.Failed) return OperationResult.Fail("Chỉ gửi lại được gói tin bị lỗi.");

            var code = await DocumentCodeAsync(log, ct);
            var online = await IsOnlineAsync(ct);
            _state.Record(DateTime.Now, online);
            var outcome = await ResendOneAsync(log, online, ct);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("INTEGRATION_RESEND", "IntegrationLogs", logId.ToString(CultureInfo.InvariantCulture),
                new { Status = (byte)IntegrationStatus.Failed },
                new { DocumentCode = code, log.Status, log.ErrorMessage }, actorUserId, ct);

            return outcome switch
            {
                (byte)IntegrationStatus.Succeeded => OperationResult.Ok($"Đã gửi lại {code}: Dược quốc gia đã tiếp nhận."),
                (byte)IntegrationStatus.Pending => OperationResult.Ok($"Đang mất kết nối mạng. Gói tin {code} đã vào hàng chờ, sẽ tự gửi khi có mạng."),
                _ => OperationResult.Fail($"{code} vẫn bị từ chối: {log.ErrorMessage}")
            };
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<OperationResult> ResendAllFailedAsync(string actorUserId, CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var logs = await _db.IntegrationLogs
                .Where(l => DqgText.Types.Contains(l.IntegrationType) && l.Status == (byte)IntegrationStatus.Failed)
                .OrderBy(l => l.IntegrationLogId).Take(MaxPerRun)
                .ToListAsync(ct);
            if (logs.Count == 0) return OperationResult.Ok("Không có gói tin lỗi nào.");

            var online = await IsOnlineAsync(ct);
            _state.Record(DateTime.Now, online);
            int ok = 0, waiting = 0, failed = 0;
            foreach (var log in logs)
            {
                switch (await ResendOneAsync(log, online, ct))
                {
                    case (byte)IntegrationStatus.Succeeded: ok++; break;
                    case (byte)IntegrationStatus.Pending: waiting++; break;
                    default: failed++; break;
                }
            }
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("INTEGRATION_RESEND", "IntegrationLogs", null, null,
                new { Count = logs.Count, Succeeded = ok, Waiting = waiting, Failed = failed }, actorUserId, ct);

            var message = $"Đã gửi lại {logs.Count} gói tin: {ok} được tiếp nhận"
                          + (waiting > 0 ? $", {waiting} chờ có mạng" : "")
                          + (failed > 0 ? $", {failed} vẫn lỗi — mở từng gói tin để xem cần sửa gì" : "") + ".";
            return failed > 0 ? OperationResult.Fail(message) : OperationResult.Ok(message);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Dựng lại gói tin từ dữ liệu hiện tại; có mạng thì gửi ngay, không thì để Chờ gửi.</summary>
    private async Task<byte> ResendOneAsync(IntegrationLog log, bool online, CancellationToken ct)
    {
        var packet = await BuildAsync(log, ct);
        if (packet is not null) log.RequestPayload = JsonSerializer.Serialize(packet, Json);
        log.Status = (byte)IntegrationStatus.Pending;
        log.ErrorMessage = null;
        if (online) await SendAsync(log, ct);
        return log.Status;
    }

    // ================================================================== Trang quản lý

    public async Task<IntegrationListViewModel> GetListAsync(IntegrationFilter filter, CancellationToken ct = default)
    {
        var all = _db.IntegrationLogs.AsNoTracking().Where(l => DqgText.Types.Contains(l.IntegrationType));
        var query = all;
        switch (filter.Type)
        {
            case "import": query = query.Where(l => l.IntegrationType == DqgText.ImportType); break;
            case "sale": query = query.Where(l => l.IntegrationType == DqgText.SaleType); break;
            default: filter.Type = null; break;
        }
        switch (filter.Status)
        {
            case "pending": query = query.Where(l => l.Status == (byte)IntegrationStatus.Pending); break;
            case "ok": query = query.Where(l => l.Status == (byte)IntegrationStatus.Succeeded); break;
            case "failed": query = query.Where(l => l.Status == (byte)IntegrationStatus.Failed); break;
            default: filter.Status = null; break;
        }
        var q = filter.Q?.Trim();
        filter.Q = string.IsNullOrEmpty(q) ? null : q;
        if (filter.Q is not null)
        {
            var text = filter.Q;
            query = query.Where(l =>
                (l.ReferenceType == DqgText.ImportRef && _db.GoodsReceipts.Any(r => r.GoodsReceiptId == l.ReferenceId && r.GoodsReceiptCode.Contains(text)))
                || (l.ReferenceType == DqgText.SaleRef && _db.SalesInvoices.Any(i => i.InvoiceId == l.ReferenceId && i.InvoiceCode.Contains(text))));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)IntegrationFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await query
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.IntegrationLogId)
            .Skip((filter.Page - 1) * IntegrationFilter.PageSize).Take(IntegrationFilter.PageSize)
            .Select(l => new IntegrationRow(l.IntegrationLogId, l.IntegrationType,
                (l.ReferenceType == DqgText.ImportRef
                    ? _db.GoodsReceipts.Where(r => r.GoodsReceiptId == l.ReferenceId).Select(r => r.GoodsReceiptCode).FirstOrDefault()
                    : _db.SalesInvoices.Where(i => i.InvoiceId == l.ReferenceId).Select(i => i.InvoiceCode).FirstOrDefault()) ?? "—",
                l.ReferenceId, l.CreatedAt, l.LastAttemptAt, l.Status, l.RetryCount, l.ErrorMessage))
            .ToListAsync(ct);

        var today = DateTime.Today;
        var (lastRun, online) = _state.Snapshot();
        return new IntegrationListViewModel
        {
            Filter = filter,
            Rows = rows,
            Total = total,
            GrandTotal = await all.CountAsync(ct),
            PageCount = pageCount,
            Pending = await all.CountAsync(l => l.Status == (byte)IntegrationStatus.Pending, ct),
            Failed = await all.CountAsync(l => l.Status == (byte)IntegrationStatus.Failed, ct),
            SucceededToday = await all.CountAsync(l => l.Status == (byte)IntegrationStatus.Succeeded && l.LastAttemptAt >= today, ct),
            NotQueued = await NotQueuedReceipts().CountAsync(ct) + await NotQueuedInvoices().CountAsync(ct),
            LastRunAt = lastRun,
            Online = online
        };
    }

    public async Task<IntegrationDetailsViewModel?> GetDetailsAsync(long logId, CancellationToken ct = default)
    {
        var log = await _db.IntegrationLogs.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IntegrationLogId == logId && DqgText.Types.Contains(l.IntegrationType), ct);
        if (log is null) return null;

        var packet = Read(log.RequestPayload);
        if (packet is not null && string.IsNullOrEmpty(packet.SoChungTu)) packet = null;
        string? receipt = null;
        if (!string.IsNullOrWhiteSpace(log.ResponsePayload))
        {
            try
            {
                using var doc = JsonDocument.Parse(log.ResponsePayload);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("maTiepNhan", out var m))
                    receipt = m.GetString();
            }
            catch (JsonException)
            {
                receipt = null;
            }
        }

        return new IntegrationDetailsViewModel
        {
            LogId = log.IntegrationLogId,
            Type = log.IntegrationType,
            DocumentCode = await DocumentCodeAsync(log, ct),
            ReferenceId = log.ReferenceId,
            Status = log.Status,
            RetryCount = log.RetryCount,
            CreatedAt = log.CreatedAt,
            LastAttemptAt = log.LastAttemptAt,
            Error = log.ErrorMessage,
            ReceiptCode = receipt,
            Packet = packet
        };
    }

    public Task<int> FailedCountAsync(CancellationToken ct = default)
        => _db.IntegrationLogs.CountAsync(l => DqgText.Types.Contains(l.IntegrationType) && l.Status == (byte)IntegrationStatus.Failed, ct);

    private async Task<string> DocumentCodeAsync(IntegrationLog log, CancellationToken ct)
    {
        var id = (int)log.ReferenceId;
        var code = log.ReferenceType == DqgText.ImportRef
            ? await _db.GoodsReceipts.AsNoTracking().Where(r => r.GoodsReceiptId == id).Select(r => r.GoodsReceiptCode).FirstOrDefaultAsync(ct)
            : await _db.SalesInvoices.AsNoTracking().Where(i => i.InvoiceId == id).Select(i => i.InvoiceCode).FirstOrDefaultAsync(ct);
        return code ?? "—";
    }
}
