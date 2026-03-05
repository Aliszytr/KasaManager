using KasaManager.Application.Abstractions;
using KasaManager.Web;
using KasaManager.Web.Middleware;
using KasaManager.Application.Services;
using KasaManager.Application.Orchestration;

using KasaManager.Application.Services.Comparison;
using KasaManager.Infrastructure;              // ✅ AddProcessingWorkspaceInMemory burada
using KasaManager.Infrastructure.Excel;
using KasaManager.Infrastructure.Export;
using KasaManager.Infrastructure.Legacy;
using KasaManager.Infrastructure.Persistence;
using KasaManager.Infrastructure.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using KasaManager.Domain.Identity;
using System.Linq;

// Bu satır olmadan ExcelDataReader hata verir!
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Kestrel: büyük dosya upload'ları için limit (yıllık MasrafveReddiyat vb.)
builder.WebHost.ConfigureKestrel(opt =>
    opt.Limits.MaxRequestBodySize = 200 * 1024 * 1024); // 200 MB

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// ✅ Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt =>
    {
        opt.LoginPath = "/Account/Login";
        opt.LogoutPath = "/Account/Logout";
        opt.AccessDeniedPath = "/Account/AccessDenied";
        opt.ExpireTimeSpan = TimeSpan.FromHours(8);
        opt.SlidingExpiration = true;
        opt.Cookie.HttpOnly = true;
        opt.Cookie.Name = "KasaManager.Auth";
    });
builder.Services.AddAuthorization();

// Performans: Response sıkıştırma (JSON, HTML, CSS, JS)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// ✅ HttpContextAccessor (FormulaDesigner için gerekli)
builder.Services.AddHttpContextAccessor();

// ✅ Distributed Memory Cache + Session (Kasa Draft auto-save için)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = "KasaManager.Session";
});

// ✅ R6+: Snapshot DB altyapısı
// Default: SQLite (portable)
// Alternatif: SQL Server (appsettings.json Database:Provider=SqlServer)
builder.Services.AddSnapshotPersistence(builder.Configuration, builder.Environment);

// Infrastructure wiring
builder.Services.AddScoped<IExcelTableReader, ExcelDataReaderTableReader>();
// MS4: IAppCache — provider config ile seçilebilir (default=Memory)
builder.Services.AddSingleton<IAppCache, KasaManager.Infrastructure.Caching.MemoryAppCache>();

// Decorator pattern: CachingImportOrchestrator wraps ImportOrchestrator
builder.Services.AddScoped<ImportOrchestrator>();
builder.Services.AddScoped<IImportOrchestrator, CachingImportOrchestrator>();
builder.Services.AddScoped<IKasaReportDateRulesService, KasaReportDateRulesService>();
builder.Services.AddScoped<IKasaDraftService, KasaDraftService>();
builder.Services.AddScoped<IExcelValidationService, KasaManager.Application.Services.Import.ExcelValidationService>();
builder.Services.AddScoped<IKasaOrchestrator, KasaOrchestrator>();
builder.Services.AddScoped<IGenelKasaRaporService, GenelKasaRaporService>();

builder.Services.AddScoped<IFormulaEngineService, FormulaEngineService>();

// ✅ Comparison Service (Banka-Online karşılaştırma)
builder.Services.AddSingleton<BankaAciklamaParser>();
builder.Services.AddScoped<IComparisonService, ComparisonService>();
builder.Services.AddScoped<IComparisonExportService, ComparisonExportService>();
builder.Services.AddScoped<IComparisonDecisionService, KasaManager.Infrastructure.Services.ComparisonDecisionService>();
builder.Services.AddScoped<IComparisonArchiveService, ComparisonArchiveService>();

// ✅ Banka Hesap Kontrol Service
builder.Services.AddScoped<IBankaHesapKontrolService, KasaManager.Infrastructure.Services.BankaHesapKontrolService>();
builder.Services.AddScoped<IHesapKontrolExportService, KasaManager.Infrastructure.Export.HesapKontrolExportService>();

// ✅ Kasa Validation (Uyarı) Service
builder.Services.AddScoped<IKasaValidationService, KasaManager.Infrastructure.Services.KasaValidationService>();

// ✅ Legacy Kasa DB (Eski Veritabanı — Read-Only)
if (builder.Configuration.GetValue<bool>("LegacyDatabase:Enabled"))
{
    builder.Services.AddDbContext<LegacyKasaDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("LegacyKasaConnection"),
            sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
    builder.Services.AddScoped<ILegacyKasaService, LegacyKasaService>();
}

// ✅ Vergide Biriken Akıllı Ledger Servisi
builder.Services.AddScoped<IVergideBirikenLedgerService, KasaManager.Infrastructure.Services.VergideBirikenLedgerService>();

// ✅ R20: Formula Engine Pipeline
builder.Services.AddFormulaPipeline();

// File storage (webroot bazlı)
builder.Services.AddScoped<IFileStorage>(sp =>
{
    var env = sp.GetRequiredService<IWebHostEnvironment>();
    return new FileStorage(env.WebRootPath);
});

// QuestPDF Community License (A5 Banka Fişi PDF)
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var app = builder.Build();

// 🔎 DEV Debug: uygulamanın gerçekten hangi DB'ye bağlandığını logla.
// Bu sayede "SSMS'te tablo boş" gibi durumlarda doğru instance/database anında görünür.
// Production'da hassas bilgi yazmamak için sadece Development'ta çalışır.
if (app.Environment.IsDevelopment())
{
    using var scopeDbg = app.Services.CreateScope();
    var dbDbg = scopeDbg.ServiceProvider.GetRequiredService<KasaManagerDbContext>();
    var dataSource = dbDbg.Database.GetDbConnection().DataSource;
    var database = dbDbg.Database.GetDbConnection().Database;
    var connStr = dbDbg.Database.GetConnectionString();

    // Bu loglar tanı amaçlıdır; warning değil informational olmalı.
    app.Logger.LogInformation("ACTIVE DB: {DataSource} / {Database}", dataSource, database);
    app.Logger.LogInformation("CONN STRING: {ConnStr}", connStr);
}

// ✅ DB otomatik bootstrapping (DbBootstrapExtensions.cs)
await app.UseDbBootstrapAsync();



// Global hata yakalama — Development'ta detaylı hata sayfası göster
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseGlobalExceptionHandler();
    app.UseHsts();
}

app.UseResponseCompression();

// Kurumsal ağda HTTPS sertifikası olmayabilir — Production'da sadece HTTP kullan
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseCorrelationId(); // MS9: Her request'e Correlation ID atar — log takibi için
app.UseRouting();
app.UseSession();

// ✅ Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
