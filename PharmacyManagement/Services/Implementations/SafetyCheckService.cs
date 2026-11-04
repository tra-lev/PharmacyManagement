using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Data;
using PharmacyManagement.Models.Enums;
using PharmacyManagement.Models.ViewModels.Sales;
using PharmacyManagement.Services.Interfaces;

namespace PharmacyManagement.Services.Implementations;

public sealed class SafetyCheckService : ISafetyCheckService
{
    private readonly PharmacyDbContext _db;

    public SafetyCheckService(PharmacyDbContext db)
    {
        _db = db;
    }

    public async Task<List<SafetyWarning>> CheckAsync(int? customerId, IReadOnlyCollection<int> medicineIds, bool checkContraindications, string? diagnosis,
        CancellationToken ct = default)
    {
        var warnings = new List<SafetyWarning>();
        if (medicineIds.Count == 0) return warnings;
        var ids = medicineIds.ToList();

        var ingredients = await _db.MedicineIngredients.AsNoTracking()
            .Where(i => ids.Contains(i.MedicineId))
            .OrderBy(i => i.Medicine.MedicineName).ThenBy(i => i.MedicineId).ThenBy(i => i.IngredientId) // thứ tự cố định: dấu cảnh báo phải ổn định
            .Select(i => new { i.MedicineId, i.IngredientId, i.Ingredient.IngredientName, i.Medicine.MedicineName })
            .ToListAsync(ct);

        // ---- Dị ứng của khách (CustomerAllergies) trùng hoạt chất trong hóa đơn / đơn thuốc ----
        string? chronic = null;
        if (customerId is not null)
        {
            var id = customerId.Value;
            chronic = await _db.Customers.AsNoTracking().Where(c => c.CustomerId == id).Select(c => c.ChronicConditions).FirstOrDefaultAsync(ct);
            var allergies = await _db.CustomerAllergies.AsNoTracking()
                .Where(a => a.CustomerId == id)
                .OrderBy(a => a.Ingredient.IngredientName).ThenBy(a => a.CustomerAllergyId)
                .Select(a => new { a.IngredientId, a.Ingredient.IngredientName, a.ReactionDescription })
                .ToListAsync(ct);
            foreach (var a in allergies)
            {
                var meds = ingredients.Where(i => i.IngredientId == a.IngredientId).Select(i => i.MedicineName).Distinct().ToList();
                if (meds.Count == 0) continue;
                warnings.Add(new SafetyWarning("bad",
                    $"Khách dị ứng {a.IngredientName}{(string.IsNullOrWhiteSpace(a.ReactionDescription) ? "" : " (" + a.ReactionDescription.Trim() + ")")} — có trong {string.Join(", ", meds)}."));
            }
        }

        // ---- Tương tác giữa hoạt chất của các thuốc KHÁC NHAU (IngredientInteractions, IngredientId1 < IngredientId2) ----
        var ingredientIds = ingredients.Select(i => i.IngredientId).Distinct().ToList();
        if (ingredientIds.Count >= 2)
        {
            var interactions = await _db.IngredientInteractions.AsNoTracking()
                .Where(x => ingredientIds.Contains(x.IngredientId1) && ingredientIds.Contains(x.IngredientId2))
                .OrderByDescending(x => x.Severity).ThenBy(x => x.InteractionId)
                .Select(x => new
                {
                    x.IngredientId1, x.IngredientId2, x.Severity, x.Description, x.Recommendation,
                    Name1 = x.IngredientId1Navigation.IngredientName, Name2 = x.IngredientId2Navigation.IngredientName
                })
                .ToListAsync(ct);
            foreach (var x in interactions)
            {
                var a = ingredients.Where(i => i.IngredientId == x.IngredientId1).ToList();
                var b = ingredients.Where(i => i.IngredientId == x.IngredientId2).ToList();
                var pair = a.SelectMany(ia => b.Where(ib => ib.MedicineId != ia.MedicineId).Select(ib => (A: ia.MedicineName, B: ib.MedicineName))).FirstOrDefault();
                if (pair.A is null) continue;
                warnings.Add(new SafetyWarning(x.Severity == (byte)InteractionSeverity.Severe ? "bad" : "warn",
                    $"Tương tác {SeverityText(x.Severity)}: {x.Name1} ({pair.A}) + {x.Name2} ({pair.B}) — {x.Description.Trim()}"
                    + (string.IsNullOrWhiteSpace(x.Recommendation) ? "" : " Khuyến cáo: " + x.Recommendation.Trim())));
            }
        }

        // ---- Chống chỉ định (DEC-08): từ khóa của thuốc so với chẩn đoán + bệnh nền, không phân biệt hoa/thường, dấu; khớp nguyên từ ----
        if (checkContraindications)
        {
            var text = FoldWords((diagnosis ?? string.Empty) + " ; " + (chronic ?? string.Empty));
            if (text.Trim().Length > 0)
            {
                var keywords = await _db.MedicineContraindications.AsNoTracking()
                    .Where(c => ids.Contains(c.MedicineId))
                    .OrderBy(c => c.Medicine.MedicineName).ThenBy(c => c.MedicineContraindicationId)
                    .Select(c => new { c.ConditionKeyword, c.Note, c.Medicine.MedicineName })
                    .ToListAsync(ct);
                foreach (var k in keywords)
                {
                    var kw = FoldWords(k.ConditionKeyword).Trim();
                    if (kw.Length == 0 || !text.Contains(" " + kw + " ", StringComparison.Ordinal)) continue;
                    warnings.Add(new SafetyWarning("bad", $"{k.MedicineName} chống chỉ định khi “{k.ConditionKeyword.Trim()}” — có trong chẩn đoán hoặc bệnh nền của khách."));
                }
            }
        }
        return warnings;
    }

    /// <summary>Dấu ngắn của bộ cảnh báo (SHA-256 nội dung) — so khớp bộ người dùng đã xem với bộ tính lại khi lưu.</summary>
    public static string KeyOf(IEnumerable<SafetyWarning> warnings)
    {
        var text = string.Join("\n", warnings.Select(w => w.Text));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>Chữ thường, bỏ dấu tiếng Việt (đ → d), mọi ký tự không phải chữ/số thành khoảng trắng; có khoảng trắng hai đầu để khớp nguyên từ.</summary>
    private static string FoldWords(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(" ");
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = ch is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(ch);
            sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        sb.Append(' ');
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Insert(0, " ") + " ";
    }

    private static string SeverityText(byte severity) => severity switch
    {
        (byte)InteractionSeverity.Minor => "mức nhẹ",
        (byte)InteractionSeverity.Moderate => "mức trung bình",
        (byte)InteractionSeverity.Severe => "nghiêm trọng",
        _ => "chưa rõ mức độ"
    };
}
