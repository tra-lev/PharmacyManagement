using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmacyManagement.Authorization;
using PharmacyManagement.Configuration;
using PharmacyManagement.Data;
using PharmacyManagement.Identity;
using PharmacyManagement.Infrastructure;
using PharmacyManagement.Middleware;
using PharmacyManagement.Models.Validation;
using PharmacyManagement.Services.Implementations;
using PharmacyManagement.Services.Interfaces;

// Lệnh khôi phục Owner chạy trên máy chủ, không khởi động web: dotnet run -- owner-recovery --email ...
var isOwnerRecovery = args.Length > 0 && args[0] == "owner-recovery";
var builder = WebApplication.CreateBuilder(isOwnerRecovery ? Array.Empty<string>() : args);

// ---- CSDL (Database-first: PharmacyDbContext do tools\02-scaffold.ps1 sinh ra) ----
var connectionString = builder.Configuration.GetConnectionString("PharmacyDb")
    ?? throw new InvalidOperationException("Thiếu ConnectionStrings:PharmacyDb trong appsettings.json hoặc User Secrets.");
builder.Services.AddDbContext<PharmacyDbContext>(options => options.UseSqlServer(connectionString));

// ---- Identity: 3 vai trò cố định, mỗi email một tài khoản, mật khẩu băm ----
builder.Services
    .AddIdentity<IdentityUser, IdentityRole>(options =>
    {
        // CSDL v2 dùng 7 bảng AspNet* theo schema gốc (không có bảng passkey).
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version1;
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = PasswordRules.MinLength; // dùng chung với kiểm tra trên form
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.SignIn.RequireConfirmedAccount = false; // Owner cần đăng nhập để đổi mật khẩu tạm rồi mới xác minh email
        options.SignIn.RequireConfirmedEmail = false;
    })
    .AddEntityFrameworkStores<PharmacyDbContext>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<VietnameseIdentityErrorDescriber>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.Name = "PharmacyAuth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
});

// ---- Phân quyền theo mã chức năng BFD; mặc định mọi trang đều phải đăng nhập ----
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// ---- Cấu hình (bí mật đặt bằng User Secrets / biến môi trường) ----
builder.Services.Configure<PharmacyOptions>(builder.Configuration.GetSection(PharmacyOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
builder.Services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.SectionName));

// ---- Dịch vụ ----
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IEmailService, SmtpEmailService>();
builder.Services.AddScoped<IVerificationCodeService, VerificationCodeService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddScoped<IAccountFlowService, AccountFlowService>();
builder.Services.AddScoped<OwnerBootstrapService>();
builder.Services.AddScoped<OwnerRecoveryService>();
builder.Services.AddScoped<UserNameSyncService>();
builder.Services.AddScoped<IMedicineCatalogService, MedicineCatalogService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();

builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    // Ô số (input type=number) luôn gửi dấu chấm thập phân; văn hóa vi-VN coi dấu chấm là phân cách nghìn → đọc số theo InvariantCulture.
    o.ModelBinderProviders.Insert(0, new InvariantDecimalModelBinderProvider());
    // Thông báo lỗi ràng buộc dữ liệu bằng tiếng Việt (mặc định là tiếng Anh).
    var m = o.ModelBindingMessageProvider;
    m.SetValueMustBeANumberAccessor(field => $"{field} phải là số.");
    m.SetAttemptedValueIsInvalidAccessor((value, field) => $"“{value}” không hợp lệ cho {field}.");
    m.SetValueIsInvalidAccessor(value => $"“{value}” không hợp lệ.");
    m.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"“{value}” không hợp lệ.");
    m.SetUnknownValueIsInvalidAccessor(field => $"Giá trị của {field} không hợp lệ.");
    m.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Giá trị không hợp lệ.");
    m.SetValueMustNotBeNullAccessor(field => $"Nhập {field}.");
    m.SetMissingBindRequiredValueAccessor(field => $"Thiếu {field}.");
    m.SetMissingKeyOrValueAccessor(() => "Thiếu giá trị.");
    m.SetMissingRequestBodyRequiredValueAccessor(() => "Thiếu dữ liệu gửi lên.");
    m.SetNonPropertyValueMustBeANumberAccessor(() => "Giá trị phải là số.");
});

var app = builder.Build();

if (isOwnerRecovery)
{
    using var scope = app.Services.CreateScope();
    var baseUrl = app.Configuration["Email:PublicBaseUrl"]
        ?? app.Configuration["urls"]?.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
        ?? "http://localhost:5000";
    return await scope.ServiceProvider.GetRequiredService<OwnerRecoveryService>().RunAsync(args.Skip(1).ToArray(), baseUrl);
}

// Khởi tạo Owner đầu tiên (chỉ Development, chỉ một lần — DEC-18)
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<OwnerBootstrapService>().RunAsync();
        await scope.ServiceProvider.GetRequiredService<UserNameSyncService>().RunAsync(); // DEC-21: tên tài khoản = phần trước @
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Không kiểm tra/khởi tạo được tài khoản Owner (kiểm tra kết nối CSDL và đã chạy script v2.1 chưa).");
    }
}

var vietnamese = new CultureInfo("vi-VN");
CultureInfo.DefaultThreadCurrentCulture = vietnamese;
CultureInfo.DefaultThreadCurrentUICulture = vietnamese;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(vietnamese),
    SupportedCultures = new[] { vietnamese },
    SupportedUICultures = new[] { vietnamese }
});
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseMiddleware<RequirePasswordChangeMiddleware>();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
app.MapControllerRoute(
        name: "areas",
        pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
return 0;
