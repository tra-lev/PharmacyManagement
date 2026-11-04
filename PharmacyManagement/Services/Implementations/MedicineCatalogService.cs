using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Helpers;
using PharmacyManagement.Models.Entities;
using PharmacyManagement.Models.ViewModels.Catalog;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class MedicineCatalogService : IMedicineCatalogService
{
    private const string CodePrefix = "MT";
    private const int MaxKeywords = 50;
    private const decimal MaxConversion = 1_000_000m;
    private const decimal MaxMoney = 1_000_000_000m;

    private readonly PharmacyDbContext _db;
    private readonly IAuditLogService _audit;

    public MedicineCatalogService(PharmacyDbContext db, IAuditLogService audit)
    {
        _db = db;
        _audit = audit;
    }

    // ================================================================== Danh sách

    public async Task<MedicineListViewModel> GetListAsync(MedicineFilter filter, CancellationToken ct = default)
    {
        var query = _db.Medicines.AsNoTracking().AsQueryable();

        filter.Status = filter.Status is "inactive" or "all" ? filter.Status : "active";
        query = filter.Status switch
        {
            "inactive" => query.Where(m => !m.IsActive),
            "all" => query,
            _ => query.Where(m => m.IsActive)
        };
        if (filter.CategoryId is not null) query = query.Where(m => m.CategoryId == filter.CategoryId);
        if (filter.Type == "rx") query = query.Where(m => m.IsPrescription);
        else if (filter.Type == "otc") query = query.Where(m => !m.IsPrescription);
        else filter.Type = null;

        var q = AccountHelpers.NormalizeFullName(filter.Q);
        filter.Q = q.Length == 0 ? null : q;
        var folded = string.Empty;
        if (filter.Q is not null)
        {
            // Tên thuốc, hoạt chất: không phân biệt dấu (SearchText); mã thuốc, mã vạch: so bình thường
            var s = filter.Q;
            folded = SearchText.Fold(s);
            query = query.Where(m =>
                EF.Functions.Collate(m.MedicineName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)
                || m.MedicineCode.Contains(s) || m.Barcode == s
                || m.MedicineIngredients.Any(i =>
                    EF.Functions.Collate(i.Ingredient.IngredientName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).Contains(folded)));
        }

        var total = await query.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)MedicineFilter.PageSize));
        filter.Page = Math.Clamp(filter.Page, 1, pageCount);

        // Đang tìm: tên BẮT ĐẦU bằng từ khóa lên trước, rồi mới đến tên chứa từ khóa / khớp mã, hoạt chất
        var ordered = folded.Length == 0
            ? query.OrderBy(m => m.MedicineName)
            : query.OrderBy(m => EF.Functions.Collate(m.MedicineName.Replace("đ", "d").Replace("Đ", "D"), SearchText.Collation).StartsWith(folded) ? 0 : 1)
                .ThenBy(m => m.MedicineName);
        var page = await ordered.ThenBy(m => m.MedicineCode)
            .Skip((filter.Page - 1) * MedicineFilter.PageSize).Take(MedicineFilter.PageSize)
            .Select(m => new
            {
                m.MedicineId, m.MedicineCode, m.MedicineName, m.Strength, m.DosageForm,
                CategoryName = m.Category.CategoryName, BaseUnitName = m.BaseUnit.UnitName,
                m.DefaultSellingPrice, m.IsPrescription, m.IsControlledDrug, m.IsActive
            })
            .ToListAsync(ct);

        var ids = page.Select(p => p.MedicineId).ToList();
        var ingredients = await _db.MedicineIngredients.AsNoTracking()
            .Where(mi => ids.Contains(mi.MedicineId))
            .Select(mi => new { mi.MedicineId, mi.Ingredient.IngredientName })
            .ToListAsync(ct);
        var ingredientText = ingredients.GroupBy(i => i.MedicineId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(i => i.IngredientName).OrderBy(n => n)));

        return new MedicineListViewModel
        {
            Filter = filter,
            Total = total,
            GrandTotal = await _db.Medicines.CountAsync(ct),
            PageCount = pageCount,
            Rows = page.Select(p => new MedicineRow(p.MedicineId, p.MedicineCode, p.MedicineName, p.Strength, p.DosageForm,
                p.CategoryName, p.BaseUnitName, p.DefaultSellingPrice, p.IsPrescription, p.IsControlledDrug, p.IsActive,
                ingredientText.GetValueOrDefault(p.MedicineId, string.Empty))).ToList(),
            Categories = await _db.Categories.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.CategoryName)
                .Select(c => new LookupItem(c.CategoryId, c.CategoryName)).ToListAsync(ct)
        };
    }

    // ================================================================== Chi tiết

    public async Task<MedicineDetailsViewModel?> GetDetailsAsync(int medicineId, CancellationToken ct = default)
    {
        var m = await _db.Medicines.AsNoTracking()
            .Where(x => x.MedicineId == medicineId)
            .Select(x => new
            {
                Medicine = x,
                CategoryName = x.Category.CategoryName,
                ManufacturerName = x.Manufacturer != null ? x.Manufacturer.ManufacturerName : null,
                BaseUnitName = x.BaseUnit.UnitName
            })
            .FirstOrDefaultAsync(ct);
        if (m is null) return null;
        var med = m.Medicine;

        var conversions = await _db.MedicineUnitConversions.AsNoTracking()
            .Where(c => c.MedicineId == medicineId)
            .OrderBy(c => c.ConversionToBase)
            .Select(c => new { c.Unit.UnitName, c.ConversionToBase, c.SellingPrice, c.IsDefaultSaleUnit })
            .ToListAsync(ct);
        var ingredients = await _db.MedicineIngredients.AsNoTracking()
            .Where(i => i.MedicineId == medicineId)
            .OrderBy(i => i.Ingredient.IngredientName)
            .Select(i => new { i.Ingredient.IngredientName, i.IngredientStrength })
            .ToListAsync(ct);
        var keywords = await _db.MedicineContraindications.AsNoTracking()
            .Where(k => k.MedicineId == medicineId).OrderBy(k => k.ConditionKeyword)
            .Select(k => k.ConditionKeyword).ToListAsync(ct);
        var history = await _db.MedicinePriceHistories.AsNoTracking()
            .Where(h => h.MedicineId == medicineId)
            .OrderByDescending(h => h.EffectiveFrom).ThenByDescending(h => h.PriceHistoryId)
            .Take(30)
            .Select(h => new PriceHistoryRow(h.EffectiveFrom, h.Unit.UnitName, h.OldSellingPrice, h.SellingPrice, h.PurchasePrice,
                h.ChangedByEmployee != null ? h.ChangedByEmployee.FullName : null, h.ChangeReason))
            .ToListAsync(ct);
        var batches = _db.MedicineBatches.AsNoTracking().Where(b => b.MedicineId == medicineId && b.QuantityOnHand > 0);
        var stockQuantity = await batches.SumAsync(b => (decimal?)b.QuantityOnHand, ct) ?? 0;
        var stockBatches = await batches.CountAsync(ct);

        return new MedicineDetailsViewModel
        {
            MedicineId = med.MedicineId,
            Code = med.MedicineCode,
            Name = med.MedicineName,
            Barcode = med.Barcode,
            CategoryName = m.CategoryName,
            ManufacturerName = m.ManufacturerName,
            DosageForm = med.DosageForm,
            Strength = med.Strength,
            RegistrationNumber = med.RegistrationNumber,
            CountryOfOrigin = med.CountryOfOrigin,
            IsPrescription = med.IsPrescription,
            IsControlledDrug = med.IsControlledDrug,
            IsInterconnected = med.IsInterconnected,
            IsActive = med.IsActive,
            CreatedAt = med.CreatedAt,
            BaseUnitName = m.BaseUnitName,
            DefaultPurchasePrice = med.DefaultPurchasePrice,
            DefaultSellingPrice = med.DefaultSellingPrice,
            Conversions = conversions.Select(c => new ConversionInfo(c.UnitName, c.ConversionToBase,
                c.SellingPrice ?? Math.Round(med.DefaultSellingPrice * c.ConversionToBase, 2), c.SellingPrice is null, c.IsDefaultSaleUnit)).ToList(),
            Ingredients = ingredients.Select(i => (i.IngredientName, i.IngredientStrength)).ToList(),
            ContraindicationKeywords = keywords,
            Contraindications = med.Contraindications,
            Interactions = med.Interactions,
            UsageInstructions = med.UsageInstructions,
            MaxSingleDose = med.MaxSingleDose,
            MaxDailyDose = med.MaxDailyDose,
            MinPatientAgeYears = med.MinPatientAgeYears,
            QuantityOnHand = stockQuantity,
            BatchCount = stockBatches,
            PriceHistory = history
        };
    }

    // ================================================================== Form

    public async Task<MedicineFormViewModel> NewFormAsync(MedicineEditRights rights, CancellationToken ct = default)
    {
        var form = new MedicineFormViewModel
        {
            MedicineCode = await NextCodeAsync(ct),
            BaseUnitId = await _db.Units.AsNoTracking().Where(u => u.UnitCode == "VIEN").Select(u => (int?)u.UnitId).FirstOrDefaultAsync(ct),
            Ingredients = new List<IngredientLine> { new() }
        };
        await PrepareFormAsync(form, rights, ct);
        return form;
    }

    public async Task<MedicineFormViewModel?> EditFormAsync(int medicineId, MedicineEditRights rights, CancellationToken ct = default)
    {
        var med = await _db.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.MedicineId == medicineId, ct);
        if (med is null) return null;

        var form = new MedicineFormViewModel
        {
            MedicineId = med.MedicineId,
            MedicineCode = med.MedicineCode,
            MedicineName = med.MedicineName,
            Barcode = med.Barcode,
            CategoryId = med.CategoryId,
            ManufacturerId = med.ManufacturerId,
            DosageForm = med.DosageForm,
            Strength = med.Strength,
            RegistrationNumber = med.RegistrationNumber,
            CountryOfOrigin = med.CountryOfOrigin,
            IsPrescription = med.IsPrescription,
            IsControlledDrug = med.IsControlledDrug,
            IsInterconnected = med.IsInterconnected,
            BaseUnitId = med.BaseUnitId,
            DefaultPurchasePrice = med.DefaultPurchasePrice,
            DefaultSellingPrice = med.DefaultSellingPrice,
            Contraindications = med.Contraindications,
            Interactions = med.Interactions,
            UsageInstructions = med.UsageInstructions,
            MaxSingleDose = med.MaxSingleDose,
            MaxDailyDose = med.MaxDailyDose,
            MinPatientAgeYears = med.MinPatientAgeYears,
            Conversions = await _db.MedicineUnitConversions.AsNoTracking()
                .Where(c => c.MedicineId == medicineId).OrderBy(c => c.ConversionToBase)
                .Select(c => new ConversionLine
                {
                    ConversionId = c.ConversionId, UnitId = c.UnitId, ConversionToBase = c.ConversionToBase,
                    SellingPrice = c.SellingPrice, IsDefaultSaleUnit = c.IsDefaultSaleUnit
                }).ToListAsync(ct),
            Ingredients = await _db.MedicineIngredients.AsNoTracking()
                .Where(i => i.MedicineId == medicineId).OrderBy(i => i.Ingredient.IngredientName)
                .Select(i => new IngredientLine { Name = i.Ingredient.IngredientName, Strength = i.IngredientStrength })
                .ToListAsync(ct),
            ContraindicationKeywords = string.Join("\n", await _db.MedicineContraindications.AsNoTracking()
                .Where(k => k.MedicineId == medicineId).OrderBy(k => k.ConditionKeyword)
                .Select(k => k.ConditionKeyword).ToListAsync(ct))
        };
        if (form.Ingredients.Count == 0) form.Ingredients.Add(new IngredientLine());
        await PrepareFormAsync(form, rights, ct);
        return form;
    }

    public async Task PrepareFormAsync(MedicineFormViewModel form, MedicineEditRights rights, CancellationToken ct = default)
    {
        form.CanEditCategory = rights.CanEditCategory;
        form.CanEditConversions = rights.CanEditConversions;

        if (form.MedicineId is int id)
        {
            var med = await _db.Medicines.AsNoTracking().FirstOrDefaultAsync(m => m.MedicineId == id, ct);
            if (med is not null)
            {
                var dbConversions = await _db.MedicineUnitConversions.AsNoTracking().Where(c => c.MedicineId == id).ToListAsync(ct);
                form.MedicineCode = med.MedicineCode;
                form.IsUsed = await IsUsedAsync(id, ct);
                form.PricesLocked = true;
                form.DefaultSellingPrice = med.DefaultSellingPrice; // giá đã khai báo không đọc từ form
                if (form.IsUsed) form.BaseUnitId = med.BaseUnitId;
                if (!rights.CanEditCategory) form.CategoryId = med.CategoryId;
                if (!rights.CanEditConversions)
                {
                    form.Conversions = dbConversions.OrderBy(c => c.ConversionToBase).Select(c => new ConversionLine
                    {
                        ConversionId = c.ConversionId, UnitId = c.UnitId, ConversionToBase = c.ConversionToBase,
                        SellingPrice = c.SellingPrice, IsDefaultSaleUnit = c.IsDefaultSaleUnit
                    }).ToList();
                }
                foreach (var line in form.Conversions)
                {
                    var saved = line.ConversionId is null ? null : dbConversions.FirstOrDefault(c => c.ConversionId == line.ConversionId);
                    if (saved is null) { line.ConversionId = null; continue; }
                    line.PriceLocked = true;
                    line.SellingPrice = saved.SellingPrice;
                    if (form.IsUsed || !rights.CanEditConversions)
                    {
                        line.Locked = true;
                        line.UnitId = saved.UnitId;
                        line.ConversionToBase = saved.ConversionToBase;
                    }
                }
            }
        }
        else
        {
            form.MedicineCode ??= await NextCodeAsync(ct);
        }

        form.CategoryOptions = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive || c.CategoryId == form.CategoryId)
            .OrderBy(c => c.CategoryName).Select(c => new LookupItem(c.CategoryId, c.CategoryName)).ToListAsync(ct);
        form.UnitOptions = await _db.Units.AsNoTracking().OrderBy(u => u.UnitId)
            .Select(u => new LookupItem(u.UnitId, u.UnitName)).ToListAsync(ct);
        form.ManufacturerOptions = await _db.Manufacturers.AsNoTracking()
            .Where(x => x.IsActive || x.ManufacturerId == form.ManufacturerId)
            .OrderBy(x => x.ManufacturerName).Select(x => new LookupItem(x.ManufacturerId, x.ManufacturerName)).ToListAsync(ct);
        form.IngredientNames = await _db.ActiveIngredients.AsNoTracking().OrderBy(i => i.IngredientName)
            .Select(i => i.IngredientName).ToListAsync(ct);
    }

    // ================================================================== Thêm

    public async Task<OperationResult<int>> CreateAsync(MedicineFormViewModel form, MedicineEditRights rights, string actorUserId, CancellationToken ct = default)
    {
        if (!rights.CanEditCategory)
            return OperationResult<int>.Fail("Thêm thuốc cần cả quyền Phân loại thuốc (1.1.3) vì thuốc nào cũng phải thuộc một nhóm.");

        var clean = await ValidateAsync(form, existing: null, ct);
        if (clean.Error is not null) return OperationResult<int>.Fail(clean.Error, clean.Field);
        if (clean.Conversions.Count > 0 && !rights.CanEditConversions)
            return OperationResult<int>.Fail("Bạn không có quyền khai báo quy đổi đơn vị (1.1.4). Bỏ các dòng quy đổi rồi lưu lại.");

        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        var now = DateTime.Now;
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var medicine = new Medicine
            {
                MedicineCode = await NextCodeAsync(ct),
                MedicineName = clean.Name,
                Barcode = clean.Barcode,
                CategoryId = form.CategoryId!.Value,
                ManufacturerId = await ResolveManufacturerAsync(form, ct),
                BaseUnitId = form.BaseUnitId!.Value,
                Strength = Clean(form.Strength),
                DosageForm = Clean(form.DosageForm),
                RegistrationNumber = Clean(form.RegistrationNumber),
                CountryOfOrigin = Clean(form.CountryOfOrigin),
                DefaultPurchasePrice = form.DefaultPurchasePrice ?? 0,
                DefaultSellingPrice = form.DefaultSellingPrice!.Value,
                IsPrescription = form.IsPrescription,
                IsControlledDrug = form.IsControlledDrug,
                IsInterconnected = form.IsInterconnected,
                UsageInstructions = CleanText(form.UsageInstructions),
                Contraindications = CleanText(form.Contraindications),
                Interactions = CleanText(form.Interactions),
                IsActive = true,
                CreatedAt = now,
                MaxSingleDose = form.MaxSingleDose,
                MaxDailyDose = form.MaxDailyDose,
                MinPatientAgeYears = form.MinPatientAgeYears
            };
            _db.Medicines.Add(medicine);
            await _db.SaveChangesAsync(ct);

            var ingredientIds = await ResolveIngredientsAsync(clean.Ingredients.Select(i => i.Name), ct);
            foreach (var (name, strength) in clean.Ingredients)
                _db.MedicineIngredients.Add(new MedicineIngredient { MedicineId = medicine.MedicineId, IngredientId = ingredientIds[name], IngredientStrength = strength });

            foreach (var line in clean.Conversions)
            {
                _db.MedicineUnitConversions.Add(new MedicineUnitConversion
                {
                    MedicineId = medicine.MedicineId, UnitId = line.UnitId!.Value, ConversionToBase = line.ConversionToBase!.Value,
                    SellingPrice = line.SellingPrice, IsDefaultSaleUnit = line.IsDefaultSaleUnit
                });
            }

            foreach (var keyword in clean.Keywords)
                _db.MedicineContraindications.Add(new MedicineContraindication { MedicineId = medicine.MedicineId, ConditionKeyword = keyword });

            // Giá khai báo ban đầu (DEC-09): đơn vị cơ sở + từng đơn vị quy đổi có giá riêng
            _db.MedicinePriceHistories.Add(new MedicinePriceHistory
            {
                MedicineId = medicine.MedicineId, UnitId = medicine.BaseUnitId, PurchasePrice = medicine.DefaultPurchasePrice,
                SellingPrice = medicine.DefaultSellingPrice, EffectiveFrom = now, ChangedByEmployeeId = employeeId,
                ChangeReason = "Khai báo giá ban đầu khi thêm thuốc"
            });
            foreach (var line in clean.Conversions.Where(c => c.SellingPrice is not null))
            {
                _db.MedicinePriceHistories.Add(new MedicinePriceHistory
                {
                    MedicineId = medicine.MedicineId, UnitId = line.UnitId!.Value, SellingPrice = line.SellingPrice!.Value,
                    EffectiveFrom = now, ChangedByEmployeeId = employeeId, ChangeReason = "Khai báo giá ban đầu khi thêm thuốc"
                });
            }

            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("MEDICINE_CREATE", "Medicines", medicine.MedicineId.ToString(CultureInfo.InvariantCulture), null,
                Snapshot(medicine, clean), actorUserId, ct);
            await tx.CommitAsync(ct);
            return OperationResult<int>.Ok(medicine.MedicineId, $"Đã thêm thuốc {medicine.MedicineCode} · {medicine.MedicineName}.");
        }
        catch (DbUpdateException)
        {
            return OperationResult<int>.Fail("Không lưu được: mã thuốc, mã vạch, hoạt chất hoặc hãng vừa bị người khác dùng cùng lúc. Bấm Lưu lại.");
        }
    }

    // ================================================================== Sửa

    public async Task<OperationResult> UpdateAsync(MedicineFormViewModel form, MedicineEditRights rights, string actorUserId, CancellationToken ct = default)
    {
        if (form.MedicineId is not int id) return OperationResult.Fail("Thiếu mã thuốc.");
        var medicine = await _db.Medicines.FirstOrDefaultAsync(m => m.MedicineId == id, ct);
        if (medicine is null) return OperationResult.Fail("Không tìm thấy thuốc.");

        var isUsed = await IsUsedAsync(id, ct);
        if (!rights.CanEditCategory) form.CategoryId = medicine.CategoryId;
        if (isUsed) form.BaseUnitId = medicine.BaseUnitId;

        var clean = await ValidateAsync(form, medicine, ct);
        if (clean.Error is not null) return OperationResult.Fail(clean.Error, clean.Field);

        var existingConversions = await _db.MedicineUnitConversions.Where(c => c.MedicineId == id).ToListAsync(ct);
        var finalConversions = rights.CanEditConversions ? clean.Conversions : new List<ConversionLine>();
        if (rights.CanEditConversions)
        {
            var conversionError = CheckConversionChanges(existingConversions, finalConversions, isUsed);
            if (conversionError is not null) return OperationResult.Fail(conversionError);
        }

        var old = Snapshot(medicine, await CurrentDataAsync(id, ct));
        var employeeId = await EmployeeIdOfAsync(actorUserId, ct);
        var now = DateTime.Now;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            medicine.MedicineName = clean.Name;
            medicine.Barcode = clean.Barcode;
            medicine.CategoryId = form.CategoryId!.Value;
            medicine.ManufacturerId = await ResolveManufacturerAsync(form, ct);
            if (!isUsed) medicine.BaseUnitId = form.BaseUnitId!.Value;
            medicine.Strength = Clean(form.Strength);
            medicine.DosageForm = Clean(form.DosageForm);
            medicine.RegistrationNumber = Clean(form.RegistrationNumber);
            medicine.CountryOfOrigin = Clean(form.CountryOfOrigin);
            medicine.DefaultPurchasePrice = form.DefaultPurchasePrice ?? medicine.DefaultPurchasePrice; // giá nhập tham khảo, sửa được
            // DefaultSellingPrice KHÔNG đổi ở đây (DEC-09)
            medicine.IsPrescription = form.IsPrescription;
            medicine.IsControlledDrug = form.IsControlledDrug;
            medicine.IsInterconnected = form.IsInterconnected;
            medicine.UsageInstructions = CleanText(form.UsageInstructions);
            medicine.Contraindications = CleanText(form.Contraindications);
            medicine.Interactions = CleanText(form.Interactions);
            medicine.MaxSingleDose = form.MaxSingleDose;
            medicine.MaxDailyDose = form.MaxDailyDose;
            medicine.MinPatientAgeYears = form.MinPatientAgeYears;

            // Hoạt chất: so khác biệt (không xóa rồi thêm lại cùng khóa)
            var ingredientIds = await ResolveIngredientsAsync(clean.Ingredients.Select(i => i.Name), ct);
            var currentIngredients = await _db.MedicineIngredients.Where(i => i.MedicineId == id).ToListAsync(ct);
            var wanted = clean.Ingredients.ToDictionary(i => ingredientIds[i.Name], i => i.Strength);
            foreach (var row in currentIngredients)
            {
                if (wanted.TryGetValue(row.IngredientId, out var strength)) row.IngredientStrength = strength;
                else _db.MedicineIngredients.Remove(row);
            }
            foreach (var (ingredientId, strength) in wanted.Where(w => currentIngredients.All(r => r.IngredientId != w.Key)))
                _db.MedicineIngredients.Add(new MedicineIngredient { MedicineId = id, IngredientId = ingredientId, IngredientStrength = strength });

            // Từ khóa chống chỉ định
            var currentKeywords = await _db.MedicineContraindications.Where(k => k.MedicineId == id).ToListAsync(ct);
            foreach (var row in currentKeywords.Where(r => !clean.Keywords.Contains(r.ConditionKeyword, StringComparer.OrdinalIgnoreCase)))
                _db.MedicineContraindications.Remove(row);
            foreach (var keyword in clean.Keywords.Where(k => !currentKeywords.Any(r => string.Equals(r.ConditionKeyword, k, StringComparison.OrdinalIgnoreCase))))
                _db.MedicineContraindications.Add(new MedicineContraindication { MedicineId = id, ConditionKeyword = keyword });

            // Quy đổi đơn vị (1.1.4)
            if (rights.CanEditConversions)
            {
                foreach (var row in existingConversions.Where(r => finalConversions.All(l => l.ConversionId != r.ConversionId)))
                    _db.MedicineUnitConversions.Remove(row);
                foreach (var line in finalConversions)
                {
                    var row = line.ConversionId is null ? null : existingConversions.First(r => r.ConversionId == line.ConversionId);
                    if (row is null)
                    {
                        _db.MedicineUnitConversions.Add(new MedicineUnitConversion
                        {
                            MedicineId = id, UnitId = line.UnitId!.Value, ConversionToBase = line.ConversionToBase!.Value,
                            SellingPrice = line.SellingPrice, IsDefaultSaleUnit = line.IsDefaultSaleUnit
                        });
                        if (line.SellingPrice is not null)
                        {
                            _db.MedicinePriceHistories.Add(new MedicinePriceHistory
                            {
                                MedicineId = id, UnitId = line.UnitId!.Value, SellingPrice = line.SellingPrice.Value,
                                EffectiveFrom = now, ChangedByEmployeeId = employeeId, ChangeReason = "Khai báo giá cho đơn vị quy đổi mới"
                            });
                        }
                    }
                    else
                    {
                        if (!isUsed)
                        {
                            row.UnitId = line.UnitId!.Value;
                            row.ConversionToBase = line.ConversionToBase!.Value;
                        }
                        row.IsDefaultSaleUnit = line.IsDefaultSaleUnit; // giá của dòng đã có giữ nguyên
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("MEDICINE_UPDATE", "Medicines", id.ToString(CultureInfo.InvariantCulture), old,
                Snapshot(medicine, await CurrentDataAsync(id, ct)), actorUserId, ct);
            await tx.CommitAsync(ct);
            return OperationResult.Ok($"Đã lưu thay đổi của {medicine.MedicineCode} · {medicine.MedicineName}.");
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail("Không lưu được: mã vạch, hoạt chất, hãng hoặc đơn vị vừa bị trùng do thao tác cùng lúc. Tải lại trang rồi thử lại.");
        }
    }

    // ================================================================== Ngừng / mở bán

    public async Task<OperationResult> SetActiveAsync(int medicineId, bool active, string actorUserId, CancellationToken ct = default)
    {
        var medicine = await _db.Medicines.FirstOrDefaultAsync(m => m.MedicineId == medicineId, ct);
        if (medicine is null) return OperationResult.Fail("Không tìm thấy thuốc.");
        if (medicine.IsActive == active)
            return OperationResult.Ok(active ? "Thuốc đang kinh doanh." : "Thuốc đã ngừng kinh doanh từ trước.");

        medicine.IsActive = active;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync(active ? "MEDICINE_ACTIVATE" : "MEDICINE_DEACTIVATE", "Medicines",
            medicineId.ToString(CultureInfo.InvariantCulture), new { isActive = !active }, new { isActive = active }, actorUserId, ct);

        if (active) return OperationResult.Ok($"Đã mở lại kinh doanh {medicine.MedicineName}.");
        var onHand = await _db.MedicineBatches.Where(b => b.MedicineId == medicineId && b.QuantityOnHand > 0)
            .SumAsync(b => (decimal?)b.QuantityOnHand, ct) ?? 0;
        return onHand > 0
            ? OperationResult.Ok($"Đã ngừng kinh doanh {medicine.MedicineName}.",
                $"Đã ngừng kinh doanh {medicine.MedicineName}. Thuốc còn tồn {Format.Number(onHand)} đơn vị cơ sở — xử lý tồn bằng trả nhà cung cấp hoặc hủy thuốc.")
            : OperationResult.Ok($"Đã ngừng kinh doanh {medicine.MedicineName}.");
    }

    // ================================================================== Kiểm tra dữ liệu

    private sealed class CleanData
    {
        public string? Error { get; set; }
        public string? Field { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Barcode { get; set; }
        public List<(string Name, string? Strength)> Ingredients { get; } = new();
        public List<ConversionLine> Conversions { get; } = new();
        public List<string> Keywords { get; } = new();

        public CleanData Fail(string message, string? field = null)
        {
            Error = message;
            Field = field;
            return this;
        }
    }

    private async Task<CleanData> ValidateAsync(MedicineFormViewModel form, Medicine? existing, CancellationToken ct)
    {
        var data = new CleanData();
        var isCreate = existing is null;

        data.Name = AccountHelpers.NormalizeFullName(form.MedicineName);
        if (data.Name.Length == 0) return data.Fail("Nhập tên thuốc.", nameof(form.MedicineName));

        data.Barcode = Clean(form.Barcode);
        if (data.Barcode is not null)
        {
            var excludeId = existing?.MedicineId ?? 0;
            var barcode = data.Barcode;
            var owner = await _db.Medicines.AsNoTracking()
                .Where(m => m.Barcode == barcode && m.MedicineId != excludeId)
                .Select(m => m.MedicineCode + " · " + m.MedicineName).FirstOrDefaultAsync(ct);
            if (owner is not null) return data.Fail($"Mã vạch này đã dùng cho thuốc {owner}.", nameof(form.Barcode));
        }

        if (form.CategoryId is null) return data.Fail("Chọn nhóm thuốc.", nameof(form.CategoryId));
        var category = await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.CategoryId == form.CategoryId, ct);
        var currentCategoryId = existing?.CategoryId;
        if (category is null || (!category.IsActive && category.CategoryId != currentCategoryId))
            return data.Fail("Nhóm thuốc không tồn tại hoặc đã ngừng dùng.", nameof(form.CategoryId));

        var newManufacturer = AccountHelpers.NormalizeFullName(form.NewManufacturerName);
        if (newManufacturer.Length == 0 && form.ManufacturerId is not null
            && !await _db.Manufacturers.AnyAsync(x => x.ManufacturerId == form.ManufacturerId, ct))
            return data.Fail("Hãng sản xuất không tồn tại.", nameof(form.ManufacturerId));

        if (form.BaseUnitId is null) return data.Fail("Chọn đơn vị cơ sở (đơn vị nhỏ nhất khi bán, ví dụ Viên).", nameof(form.BaseUnitId));
        var unitIds = await _db.Units.AsNoTracking().Select(u => u.UnitId).ToListAsync(ct);
        if (!unitIds.Contains(form.BaseUnitId.Value)) return data.Fail("Đơn vị cơ sở không tồn tại.", nameof(form.BaseUnitId));

        if (form.DefaultPurchasePrice is < 0 or > MaxMoney) return data.Fail("Giá nhập phải từ 0 trở lên.", nameof(form.DefaultPurchasePrice));
        if (isCreate)
        {
            if (form.DefaultSellingPrice is null or <= 0)
                return data.Fail("Nhập giá bán cho đơn vị cơ sở (lớn hơn 0). Giá này chỉ khai báo một lần.", nameof(form.DefaultSellingPrice));
            if (form.DefaultSellingPrice > MaxMoney) return data.Fail("Giá bán quá lớn.", nameof(form.DefaultSellingPrice));
        }

        if (form.MaxSingleDose is <= 0) return data.Fail("Liều tối đa 1 lần phải lớn hơn 0.", nameof(form.MaxSingleDose));
        if (form.MaxDailyDose is <= 0) return data.Fail("Liều tối đa 1 ngày phải lớn hơn 0.", nameof(form.MaxDailyDose));
        if (form.MaxSingleDose is not null && form.MaxDailyDose is not null && form.MaxSingleDose > form.MaxDailyDose)
            return data.Fail("Liều tối đa 1 lần không được lớn hơn liều tối đa 1 ngày.", nameof(form.MaxSingleDose));

        // Hoạt chất
        var seenIngredients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in form.Ingredients)
        {
            var name = AccountHelpers.NormalizeFullName(line.Name);
            var strength = Clean(line.Strength);
            if (name.Length == 0)
            {
                if (strength is not null) return data.Fail($"Có hàm lượng “{strength}” nhưng chưa nhập tên hoạt chất.");
                continue;
            }
            if (name.Length > 200) return data.Fail($"Tên hoạt chất “{name[..40]}…” quá dài (tối đa 200 ký tự).");
            if (!seenIngredients.Add(name)) return data.Fail($"Hoạt chất “{name}” bị nhập hai lần.");
            data.Ingredients.Add((name, strength));
        }

        // Quy đổi
        var seenUnits = new HashSet<int>();
        var lineNo = 0;
        foreach (var line in form.Conversions)
        {
            lineNo++;
            var empty = line.UnitId is null && line.ConversionToBase is null && line.SellingPrice is null && line.ConversionId is null;
            if (empty) continue;
            var label = $"Dòng quy đổi {lineNo}";
            if (line.UnitId is null || !unitIds.Contains(line.UnitId.Value)) return data.Fail($"{label}: chọn đơn vị.");
            if (line.UnitId == form.BaseUnitId) return data.Fail($"{label}: đơn vị quy đổi phải khác đơn vị cơ sở.");
            if (!seenUnits.Add(line.UnitId.Value)) return data.Fail($"{label}: đơn vị này đã có ở dòng khác.");
            if (line.ConversionToBase is null or <= 0 || line.ConversionToBase > MaxConversion)
                return data.Fail($"{label}: nhập số đơn vị cơ sở trong 1 đơn vị này (lớn hơn 0).");
            if (line.ConversionToBase == 1) return data.Fail($"{label}: hệ số bằng 1 thì trùng đơn vị cơ sở — bỏ dòng này.");
            if (line.SellingPrice is < 0 or > MaxMoney) return data.Fail($"{label}: giá bán phải từ 0 trở lên, hoặc để trống để tự tính.");
            data.Conversions.Add(line);
        }
        if (data.Conversions.Count(c => c.IsDefaultSaleUnit) > 1)
            return data.Fail("Chỉ chọn một đơn vị mặc định khi bán.");

        // Từ khóa chống chỉ định: mỗi dòng (hoặc cách nhau bởi dấu ; ,) một từ khóa
        foreach (var raw in (form.ContraindicationKeywords ?? string.Empty).Split(new[] { '\n', '\r', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var keyword = AccountHelpers.NormalizeFullName(raw);
            if (keyword.Length == 0 || data.Keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase)) continue;
            if (keyword.Length > 150) return data.Fail($"Từ khóa “{keyword[..40]}…” quá dài (tối đa 150 ký tự).", nameof(form.ContraindicationKeywords));
            data.Keywords.Add(keyword);
        }
        if (data.Keywords.Count > MaxKeywords)
            return data.Fail($"Tối đa {MaxKeywords} từ khóa chống chỉ định.", nameof(form.ContraindicationKeywords));

        return data;
    }

    /// <summary>Thuốc đã dùng: không xóa dòng quy đổi cũ (đổi đơn vị/hệ số đã được khóa từ PrepareForm/Update).</summary>
    private static string? CheckConversionChanges(List<MedicineUnitConversion> existing, List<ConversionLine> final, bool isUsed)
    {
        foreach (var line in final.Where(l => l.ConversionId is not null))
        {
            if (existing.All(r => r.ConversionId != line.ConversionId))
                return "Dòng quy đổi không còn tồn tại (có thể vừa bị sửa ở nơi khác). Tải lại trang.";
        }
        if (isUsed)
        {
            var removed = existing.Where(r => final.All(l => l.ConversionId != r.ConversionId)).ToList();
            if (removed.Count > 0)
                return "Thuốc đã có lô hàng hoặc giao dịch nên không bỏ được đơn vị quy đổi đã khai báo.";
            foreach (var line in final.Where(l => l.ConversionId is not null))
            {
                var row = existing.First(r => r.ConversionId == line.ConversionId);
                if (row.UnitId != line.UnitId || row.ConversionToBase != line.ConversionToBase)
                    return "Thuốc đã có lô hàng hoặc giao dịch nên không đổi được đơn vị, hệ số quy đổi đã khai báo.";
            }
        }
        return null;
    }

    // ================================================================== Tiện ích

    private async Task<bool> IsUsedAsync(int medicineId, CancellationToken ct)
        => await _db.MedicineBatches.AnyAsync(x => x.MedicineId == medicineId, ct)
           || await _db.InventoryTransactions.AnyAsync(x => x.MedicineId == medicineId, ct)
           || await _db.PurchaseOrderDetails.AnyAsync(x => x.MedicineId == medicineId, ct)
           || await _db.GoodsReceiptDetails.AnyAsync(x => x.MedicineId == medicineId, ct)
           || await _db.SalesInvoiceDetails.AnyAsync(x => x.MedicineId == medicineId, ct)
           || await _db.PrescriptionDetails.AnyAsync(x => x.MedicineId == medicineId, ct);

    private async Task<string> NextCodeAsync(CancellationToken ct)
    {
        var codes = await _db.Medicines.AsNoTracking().Where(m => m.MedicineCode.StartsWith(CodePrefix))
            .Select(m => m.MedicineCode).ToListAsync(ct);
        var max = 0;
        foreach (var code in codes)
            if (int.TryParse(code.AsSpan(CodePrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > max) max = n;
        return CodePrefix + (max + 1).ToString("D4", CultureInfo.InvariantCulture);
    }

    private Task<int?> EmployeeIdOfAsync(string userId, CancellationToken ct)
        => _db.Employees.AsNoTracking().Where(e => e.UserId == userId).Select(e => (int?)e.EmployeeId).FirstOrDefaultAsync(ct);

    /// <summary>Hãng mới: dùng lại hãng trùng tên nếu đã có, không có thì tạo.</summary>
    private async Task<int?> ResolveManufacturerAsync(MedicineFormViewModel form, CancellationToken ct)
    {
        var name = AccountHelpers.NormalizeFullName(form.NewManufacturerName);
        if (name.Length == 0) return form.ManufacturerId;
        var existing = await _db.Manufacturers.FirstOrDefaultAsync(x => x.ManufacturerName == name, ct);
        if (existing is not null)
        {
            if (!existing.IsActive) existing.IsActive = true;
            return existing.ManufacturerId;
        }
        var created = new Manufacturer { ManufacturerName = name, Country = Clean(form.CountryOfOrigin), IsActive = true };
        _db.Manufacturers.Add(created);
        await _db.SaveChangesAsync(ct);
        return created.ManufacturerId;
    }

    /// <summary>Tên hoạt chất → Id; hoạt chất chưa có thì tạo (UQ_ActiveIngredients_Name chặn trùng).</summary>
    private async Task<Dictionary<string, int>> ResolveIngredientsAsync(IEnumerable<string> names, CancellationToken ct)
    {
        var list = names.ToList();
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (list.Count == 0) return result;
        var existing = await _db.ActiveIngredients.Where(a => list.Contains(a.IngredientName)).ToListAsync(ct);
        foreach (var a in existing) result[a.IngredientName] = a.IngredientId;
        var created = new List<ActiveIngredient>();
        foreach (var name in list.Where(n => !result.ContainsKey(n)))
        {
            var ingredient = new ActiveIngredient { IngredientName = name };
            _db.ActiveIngredients.Add(ingredient);
            created.Add(ingredient);
        }
        if (created.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            foreach (var a in created) result[a.IngredientName] = a.IngredientId;
        }
        return result;
    }

    private async Task<CleanData> CurrentDataAsync(int medicineId, CancellationToken ct)
    {
        var data = new CleanData();
        var ingredients = await _db.MedicineIngredients.AsNoTracking().Where(i => i.MedicineId == medicineId)
            .Select(i => new { i.Ingredient.IngredientName, i.IngredientStrength }).ToListAsync(ct);
        data.Ingredients.AddRange(ingredients.Select(i => (i.IngredientName, i.IngredientStrength)));
        data.Keywords.AddRange(await _db.MedicineContraindications.AsNoTracking().Where(k => k.MedicineId == medicineId)
            .Select(k => k.ConditionKeyword).ToListAsync(ct));
        data.Conversions.AddRange(await _db.MedicineUnitConversions.AsNoTracking().Where(c => c.MedicineId == medicineId)
            .Select(c => new ConversionLine { UnitId = c.UnitId, ConversionToBase = c.ConversionToBase, SellingPrice = c.SellingPrice, IsDefaultSaleUnit = c.IsDefaultSaleUnit })
            .ToListAsync(ct));
        return data;
    }

    /// <summary>Ảnh chụp dữ liệu để ghi nhật ký (SYS-09).</summary>
    private static object Snapshot(Medicine m, CleanData d) => new
    {
        m.MedicineCode, m.MedicineName, m.Barcode, m.CategoryId, m.ManufacturerId, m.BaseUnitId, m.Strength, m.DosageForm,
        m.RegistrationNumber, m.IsPrescription, m.IsControlledDrug, m.IsInterconnected,
        m.DefaultPurchasePrice, m.DefaultSellingPrice, m.MaxSingleDose, m.MaxDailyDose, m.MinPatientAgeYears,
        Ingredients = d.Ingredients.Select(i => i.Name + (i.Strength is null ? "" : " " + i.Strength)).ToList(),
        Conversions = d.Conversions.Select(c => new { c.UnitId, c.ConversionToBase, c.SellingPrice, c.IsDefaultSaleUnit }).ToList(),
        Contraindications = d.Keywords
    };

    /// <summary>Văn bản nhiều dòng: chỉ bỏ khoảng trắng đầu/cuối, giữ xuống dòng.</summary>
    private static string? CleanText(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n");
    }

    private static string? Clean(string? value)
    {
        var text = AccountHelpers.NormalizeFullName(value);
        return text.Length == 0 ? null : text;
    }
}
