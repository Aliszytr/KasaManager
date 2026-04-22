using System;
using KasaManager.Domain.Reports;
using KasaManager.Domain.Reports.HesapKontrol;
using KasaManager.Domain.Reports.Snapshots;
using KasaManager.Domain.Settings;
using KasaManager.Domain.FormulaEngine;
using KasaManager.Domain.FormulaEngine.Authoring;
using KasaManager.Domain.Validation;
using KasaManager.Domain.Identity;
using KasaManager.Domain.Entities;
using KasaManager.Domain.FinancialExceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using KasaManager.Domain.Calculation.Data;
namespace KasaManager.Infrastructure.Persistence
{
    /// <summary>
    /// R6+: Snapshot DB.
    /// Not: JsonDocument EF Core tarafında entity/owned gibi algılanabildiği için,
    /// snapshot JSON alanlarını Domain'de string olarak saklıyoruz (ValuesJson, ColumnsJson, ...).
    /// </summary>
    public sealed class KasaManagerDbContext : DbContext
    {
        public KasaManagerDbContext(DbContextOptions<KasaManagerDbContext> options) : base(options) { }

        public DbSet<KasaRaporSnapshot> KasaRaporSnapshots => Set<KasaRaporSnapshot>();
        public DbSet<KasaRaporSnapshotRow> KasaRaporSnapshotRows => Set<KasaRaporSnapshotRow>();
        public DbSet<KasaRaporSnapshotInputs> KasaRaporSnapshotInputs => Set<KasaRaporSnapshotInputs>();
        public DbSet<KasaRaporSnapshotResults> KasaRaporSnapshotResults => Set<KasaRaporSnapshotResults>();

        // R9: Global kasa varsayılanları
        public DbSet<KasaGlobalDefaultsSettings> KasaGlobalDefaultsSettings => Set<KasaGlobalDefaultsSettings>();

    // R17C: FormulaSet CRUD
    public DbSet<PersistedFormulaSet> FormulaSets => Set<PersistedFormulaSet>();
    public DbSet<PersistedFormulaLine> FormulaLines => Set<PersistedFormulaLine>();
    public DbSet<PersistedFormulaRun> FormulaRuns => Set<PersistedFormulaRun>();

    // R17: Hesaplanmış Kasa Snapshotları
    public DbSet<CalculatedKasaSnapshot> CalculatedKasaSnapshots => Set<CalculatedKasaSnapshot>();

    // R17: Kullanıcı alan tercihleri
    public DbSet<UserFieldPreference> UserFieldPreferences => Set<UserFieldPreference>();

    // Banka Hesap Kontrol Modülü
    public DbSet<HesapKontrolKaydi> HesapKontrolKayitlari => Set<HesapKontrolKaydi>();

    // Validation: Kullanıcı tarafından dismiss edilen uyarılar
    public DbSet<DismissedValidation> DismissedValidations => Set<DismissedValidation>();

    // Authentication: Kullanıcılar
    public DbSet<KasaUser> KasaUsers => Set<KasaUser>();

    // Karşılaştırma kararları
    public DbSet<ComparisonDecision> ComparisonDecisions => Set<ComparisonDecision>();

    // Financial Exceptions (vNext)
    public DbSet<FinansalIstisna> FinansalIstisnalar => Set<FinansalIstisna>();
    public DbSet<FinansalIstisnaHistory> FinansalIstisnaHistory => Set<FinansalIstisnaHistory>();

    // ===== Data-First Architecture (Faz 1) =====
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<DailyFact> DailyFacts => Set<DailyFact>();
    public DbSet<DailyOverride> DailyOverrides => Set<DailyOverride>();
    public DbSet<DailyCalculationResult> DailyCalculationResults => Set<DailyCalculationResult>();
    public DbSet<DailyCalculationHistory> DailyCalculationHistories => Set<DailyCalculationHistory>();
    public DbSet<CalculationParityDrift> CalculationParityDrifts => Set<CalculationParityDrift>();
    public DbSet<DataFirstTrustSnapshot> DataFirstTrustSnapshots => Set<DataFirstTrustSnapshot>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ComparisonDecision: composite unique index
            modelBuilder.Entity<ComparisonDecision>()
                .HasIndex(d => new { d.ComparisonType, d.OnlineDosyaNo, d.OnlineMiktar, d.OnlineBirimAdi })
                .IsUnique()
                .HasDatabaseName("IX_ComparisonDecisions_UniqueRecord");

            // DataFirst Trust Snapshot - Unique per Date + KasaType
            modelBuilder.Entity<DataFirstTrustSnapshot>(b =>
            {
                b.ToTable("DataFirstTrustSnapshots");
                b.HasKey(x => x.Id);
                b.Property(x => x.ConfidenceScore).HasPrecision(5, 2);
                b.HasIndex(x => new { x.TargetDate, x.KasaType })
                 .IsUnique()
                 .HasDatabaseName("IX_TrustSnapshots_Date_Scope");
            });


            // DateOnly -> DateTime converter (SQLite)
            var dateOnlyConverter = new ValueConverter<DateOnly, DateTime>(
                d => d.ToDateTime(TimeOnly.MinValue),
                d => DateOnly.FromDateTime(d)
            );

            modelBuilder.Entity<KasaRaporSnapshot>(b =>
            {
                b.ToTable("KasaRaporSnapshots");
                b.HasKey(x => x.Id);

                b.Property(x => x.RaporTarihi).HasConversion(dateOnlyConverter);

                b.Property(x => x.RaporTuru)
                    .HasConversion<string>()
                    .HasMaxLength(20);

                // Domain'de int ama önceki denemelerde string like maxlen kullanılmıştı.
                // Burada net bırakıyoruz (int sütunu).
                b.Property(x => x.Version);

                
                b.Property(x => x.SelectionTotal)
                    .HasPrecision(18, 2);
b.Property(x => x.CreatedBy).HasMaxLength(100);

                // JSON string alanlar
                b.Property(x => x.WarningsJson)
                    .HasColumnType("nvarchar(max)");

                b.HasMany(x => x.Rows)
                    .WithOne(r => r.Snapshot!)
                    .HasForeignKey(r => r.SnapshotId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Inputs)
                    .WithOne(i => i.Snapshot!)
                    .HasForeignKey<KasaRaporSnapshotInputs>(i => i.SnapshotId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Results)
                    .WithOne(r => r.Snapshot!)
                    .HasForeignKey<KasaRaporSnapshotResults>(r => r.SnapshotId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(x => new { x.RaporTarihi, x.RaporTuru });
            });

            modelBuilder.Entity<KasaRaporSnapshotRow>(b =>
            {
                b.ToTable("KasaRaporSnapshotRows");
                b.HasKey(x => x.Id);

                b.Property(x => x.Veznedar).HasMaxLength(150);

                

                b.Property(x => x.Bakiye)
                    .HasPrecision(18, 2);
b.Property(x => x.ColumnsJson)
                    .IsRequired()
                    .HasColumnType("nvarchar(max)");

                b.Property(x => x.HeadersJson)
                    .HasColumnType("nvarchar(max)");
            });

            modelBuilder.Entity<KasaRaporSnapshotInputs>(b =>
            {
                b.ToTable("KasaRaporSnapshotInputs");
                b.HasKey(x => x.Id);

                b.Property(x => x.ValuesJson)
                    .IsRequired()
                    .HasColumnType("nvarchar(max)");
            });

            modelBuilder.Entity<KasaRaporSnapshotResults>(b =>
            {
                b.ToTable("KasaRaporSnapshotResults");
                b.HasKey(x => x.Id);

                b.Property(x => x.ValuesJson)
                    .IsRequired()
                    .HasColumnType("nvarchar(max)");
            });

            modelBuilder.Entity<KasaGlobalDefaultsSettings>(b =>
            {
                b.ToTable("KasaGlobalDefaultsSettings");
                b.HasKey(x => x.Id);

                // 🔒 Tek satır/Id=1 kuralı (deterministik):
                // SQL Server'da Identity kullanırsak farklı ortamlarda Id=1 garanti olmaz.
                // Bu da ayarların "kaydetmiyor/göstermiyor" gibi davranmasına (Id=1 beklenirken farklı Id oluşmasına)
                // ve sonraki kayıtlarda identity-insert hatalarına sebep olabilir.
                // Bu yüzden Id uygulama tarafından verilir ve EF bu alanı otomatik üretmez.
                b.Property(x => x.Id).ValueGeneratedNever();

                b.Property(x => x.SelectedVeznedarlarJson)
                    .IsRequired()
                    .HasColumnType("nvarchar(max)");

                b.Property(x => x.UpdatedBy)
                    .HasMaxLength(100);

                

                b.Property(x => x.DefaultBozukPara).HasPrecision(18, 2);
                b.Property(x => x.DefaultNakitPara).HasPrecision(18, 2);
                b.Property(x => x.DefaultKasaEksikFazla).HasPrecision(18, 2);
                b.Property(x => x.DefaultGenelKasaDevredenSeed).HasPrecision(18, 2);
                b.Property(x => x.DefaultKaydenTahsilat).HasPrecision(18, 2);
                b.Property(x => x.DefaultDundenDevredenKasaNakit).HasPrecision(18, 2);
// Tek satır için Id=1 kuralı (uygulama tarafında)
            });

        // ===== R17C: FormulaSet CRUD =====
        modelBuilder.Entity<PersistedFormulaSet>(b =>
        {
            b.ToTable("FormulaSets");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.ScopeType).HasMaxLength(30).IsRequired();
            b.Property(x => x.SelectedInputsJson).IsRequired();
            b.HasMany(x => x.Lines)
                .WithOne(x => x.Set)
                .HasForeignKey(x => x.SetId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Runs)
                .WithOne(x => x.Set)
                .HasForeignKey(x => x.SetId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.ScopeType, x.IsActive });
        });

        modelBuilder.Entity<PersistedFormulaLine>(b =>
        {
            b.ToTable("FormulaLines");
            b.HasKey(x => x.Id);
            b.Property(x => x.TargetKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.Mode).HasMaxLength(20).IsRequired();
            b.Property(x => x.SourceKey).HasMaxLength(200);
            b.Property(x => x.Expression).HasMaxLength(2000);
            b.HasIndex(x => new { x.SetId, x.SortOrder });
        });

        modelBuilder.Entity<PersistedFormulaRun>(b =>
        {
            b.ToTable("FormulaRuns");
            b.HasKey(x => x.Id);
            b.Property(x => x.InputsJson).IsRequired();
            b.Property(x => x.OutputsJson).IsRequired();
            b.Property(x => x.IssuesJson).IsRequired();
            b.HasIndex(x => new { x.SetId, x.RunAtUtc });
        });

        // ===== R17: CalculatedKasaSnapshot =====
        modelBuilder.Entity<CalculatedKasaSnapshot>(b =>
        {
            b.ToTable("CalculatedKasaSnapshots");
            b.HasKey(x => x.Id);
            
            b.Property(x => x.RaporTarihi).HasConversion(dateOnlyConverter);
            b.Property(x => x.KasaTuru).HasConversion<int>();
            b.Property(x => x.FormulaSetName).HasMaxLength(200);
            b.Property(x => x.CalculatedBy).HasMaxLength(256);
            b.Property(x => x.DeletedBy).HasMaxLength(256);
            b.Property(x => x.InputsJson).IsRequired().HasColumnType("nvarchar(max)");
            b.Property(x => x.OutputsJson).IsRequired().HasColumnType("nvarchar(max)");
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.KasaRaporDataJson).HasColumnType("nvarchar(max)");
            
            b.HasIndex(x => new { x.RaporTarihi, x.KasaTuru, x.IsActive })
                .HasDatabaseName("IX_CalculatedKasaSnapshots_Date_Type_Active");
            b.HasIndex(x => x.IsDeleted)
                .HasDatabaseName("IX_CalculatedKasaSnapshots_IsDeleted");
        });

        // ===== R17: UserFieldPreference =====
        modelBuilder.Entity<UserFieldPreference>(b =>
        {
            b.ToTable("UserFieldPreferences");
            b.HasKey(x => x.Id);
            
            b.Property(x => x.KasaType).HasMaxLength(50).IsRequired();
            b.Property(x => x.UserName).HasMaxLength(256);
            b.Property(x => x.SelectedFieldsJson).IsRequired().HasColumnType("nvarchar(max)");
            
            b.HasIndex(x => new { x.KasaType, x.UserName })
                .IsUnique()
                .HasDatabaseName("IX_UserFieldPreferences_KasaType_UserName");
        });

        // ===== Banka Hesap Kontrol =====
        modelBuilder.Entity<HesapKontrolKaydi>(b =>
        {
            b.ToTable("HesapKontrolKayitlari");
            b.HasKey(x => x.Id);

            b.Property(x => x.AnalizTarihi).HasConversion(dateOnlyConverter);
            b.Property(x => x.HesapTuru).HasConversion<int>();
            b.Property(x => x.Yon).HasConversion<int>();
            b.Property(x => x.Sinif).HasConversion<int>();
            b.Property(x => x.Durum).HasConversion<int>();
            b.Property(x => x.CozulmeTarihi).HasConversion(dateOnlyConverter);

            b.Property(x => x.Tutar).HasPrecision(18, 2);
            b.Property(x => x.Aciklama).HasMaxLength(1000);
            b.Property(x => x.DosyaNo).HasMaxLength(100);
            b.Property(x => x.BirimAdi).HasMaxLength(200);
            b.Property(x => x.TespitEdilenTip).HasMaxLength(50);
            b.Property(x => x.KarsilastirmaTuru).HasMaxLength(30);
            b.Property(x => x.OnaylayanKullanici).HasMaxLength(256);
            b.Property(x => x.Notlar).HasMaxLength(2000);
            b.Property(x => x.CreatedBy).HasMaxLength(256);

            // Faz 2: Akıllı Takip Motoru alanları
            b.Property(x => x.TakipBaslangicTarihi).HasConversion(dateOnlyConverter);
            b.Ignore(x => x.TakipteGunSayisi); // Computed — DB'ye yazılmaz

            b.HasIndex(x => new { x.AnalizTarihi, x.HesapTuru, x.Durum })
                .HasDatabaseName("IX_HesapKontrol_Tarih_Hesap_Durum");
            b.HasIndex(x => x.Durum)
                .HasDatabaseName("IX_HesapKontrol_Durum");
        });

        // ===== Validation: DismissedValidation =====
        modelBuilder.Entity<DismissedValidation>(b =>
        {
            b.ToTable("DismissedValidations");
            b.HasKey(x => x.Id);

            b.Property(x => x.RaporTarihi).HasConversion(dateOnlyConverter);
            b.Property(x => x.KasaTuru).HasMaxLength(30).IsRequired();
            b.Property(x => x.RuleCode).HasMaxLength(100).IsRequired();
            b.Property(x => x.DismissedBy).HasMaxLength(256);
            b.Property(x => x.Note).HasMaxLength(500);

            b.HasIndex(x => new { x.RaporTarihi, x.KasaTuru, x.RuleCode })
                .IsUnique()
                .HasDatabaseName("IX_DismissedValidations_Date_Kasa_Rule");
        });

        // ── KasaGlobalDefaultsSettings ──
        modelBuilder.Entity<KasaGlobalDefaultsSettings>(b =>
        {
            b.Property(x => x.VergideBirikenSeed).HasPrecision(18, 2);
        });

        // ── KasaUser (Authentication) ──
        modelBuilder.Entity<KasaUser>(b =>
        {
            b.ToTable("KasaUsers");
            b.HasKey(x => x.Id);
            b.Property(x => x.Username).HasMaxLength(100).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Role).HasMaxLength(50).HasDefaultValue("User");

            b.HasIndex(x => x.Username)
                .IsUnique()
                .HasDatabaseName("IX_KasaUsers_Username");
        });

        // ===== Financial Exceptions (vNext) =====
        modelBuilder.Entity<FinansalIstisna>(b =>
        {
            b.ToTable("FinansalIstisnalar");
            b.HasKey(x => x.Id);

            b.Property(x => x.IslemTarihi).HasConversion(dateOnlyConverter);
            b.Property(x => x.SistemGirisTarihi).HasConversion(dateOnlyConverter);
            b.Property(x => x.CozulmeTarihi).HasConversion(dateOnlyConverter);

            b.Property(x => x.Tur).HasConversion<int>();
            b.Property(x => x.Kategori).HasConversion<int>();
            b.Property(x => x.HesapTuru).HasConversion<int>();
            b.Property(x => x.EtkiYonu).HasConversion<int>();
            b.Property(x => x.KararDurumu).HasConversion<int>();
            b.Property(x => x.Durum).HasConversion<int>();

            b.Property(x => x.BeklenenTutar).HasPrecision(18, 2);
            b.Property(x => x.GerceklesenTutar).HasPrecision(18, 2);
            b.Property(x => x.SistemeGirilenTutar).HasPrecision(18, 2);

            b.Property(x => x.HedefHesapAciklama).HasMaxLength(500);
            b.Property(x => x.Neden).HasMaxLength(1000);
            b.Property(x => x.Aciklama).HasMaxLength(2000);
            b.Property(x => x.OlusturanKullanici).HasMaxLength(256);
            b.Property(x => x.GuncelleyenKullanici).HasMaxLength(256);
            b.Property(x => x.KararVerenKullanici).HasMaxLength(256);

            b.HasIndex(x => new { x.IslemTarihi, x.Durum, x.KararDurumu })
                .HasDatabaseName("IX_FinansalIstisnalar_Tarih_Durum_Karar");
        });

        // ===== Financial Exceptions History (vNext Faz 3.1) =====
        modelBuilder.Entity<FinansalIstisnaHistory>(b =>
        {
            b.ToTable("FinansalIstisnaHistory");
            b.HasKey(x => x.Id);

            b.Property(x => x.EventType).HasConversion<int>();
            b.Property(x => x.OldKararDurumu).HasConversion<int?>();
            b.Property(x => x.NewKararDurumu).HasConversion<int?>();
            b.Property(x => x.OldDurum).HasConversion<int?>();
            b.Property(x => x.NewDurum).HasConversion<int?>();

            b.Property(x => x.OldBeklenenTutar).HasPrecision(18, 2);
            b.Property(x => x.NewBeklenenTutar).HasPrecision(18, 2);
            b.Property(x => x.OldGerceklesenTutar).HasPrecision(18, 2);
            b.Property(x => x.NewGerceklesenTutar).HasPrecision(18, 2);
            b.Property(x => x.OldSistemeGirilenTutar).HasPrecision(18, 2);
            b.Property(x => x.NewSistemeGirilenTutar).HasPrecision(18, 2);

            b.Property(x => x.EventKullanici).HasMaxLength(256);
            b.Property(x => x.Aciklama).HasMaxLength(2000);

            // DB-4 FIX: Explicit FK — audit log korunması için Restrict.
            // İstisna silindiğinde history kayıtları korunmalı (audit ihtiyacı).
            // Domain kuralı: İstisna entity silinmez, ama FK konfigürasyonu explicit olmalı.
            b.HasOne<FinansalIstisna>()
                .WithMany()
                .HasForeignKey(x => x.FinansalIstisnaId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => x.FinansalIstisnaId)
                .HasDatabaseName("IX_FinExHistory_IstisnaId");
            b.HasIndex(x => x.EventTarihiUtc)
                .HasDatabaseName("IX_FinExHistory_EventTarihi");
        });

        // ===== Faz 1: Data-First Architecture Entities =====
        modelBuilder.Entity<ImportBatch>(b =>
        {
            b.ToTable("ImportBatches");
            b.HasKey(x => x.Id);
            b.Property(x => x.TargetDate).HasConversion(dateOnlyConverter);
            b.Property(x => x.SourceFileName).HasMaxLength(255);
            b.Property(x => x.FileHash).HasMaxLength(255);
            b.Property(x => x.ImportedBy).HasMaxLength(255);
            b.Property(x => x.ImportProfileVersion).HasMaxLength(50);
            
            b.HasIndex(x => x.TargetDate).HasDatabaseName("IX_ImportBatches_TargetDate");
        });

        modelBuilder.Entity<DailyFact>(b =>
        {
            b.ToTable("DailyFacts");
            b.HasKey(x => x.Id);
            b.Property(x => x.ForDate).HasConversion(dateOnlyConverter);
            b.Property(x => x.CanonicalKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.RawValue).HasMaxLength(2000);
            b.Property(x => x.TextValue).HasColumnType("nvarchar(max)");
            b.Property(x => x.SourceFileName).HasMaxLength(255);
            b.Property(x => x.NumericValue).HasPrecision(18, 2);
            b.Property(x => x.Confidence).HasPrecision(5, 4);

            b.HasIndex(x => new { x.ForDate, x.CanonicalKey })
                .HasDatabaseName("IX_DailyFacts_Date_Key");
        });

        modelBuilder.Entity<DailyOverride>(b =>
        {
            b.ToTable("DailyOverrides");
            b.HasKey(x => x.Id);
            b.Property(x => x.ForDate).HasConversion(dateOnlyConverter);
            b.Property(x => x.CanonicalKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.TextValue).HasColumnType("nvarchar(max)");
            b.Property(x => x.Reason).HasMaxLength(1000);
            b.Property(x => x.CreatedBy).HasMaxLength(255);
            b.Property(x => x.NumericValue).HasPrecision(18, 2);
            
            b.HasIndex(x => new { x.ForDate, x.CanonicalKey })
                .HasDatabaseName("IX_DailyOverrides_Date_Key");
        });

        modelBuilder.Entity<DailyCalculationResult>(b =>
        {
            b.ToTable("DailyCalculationResults");
            b.HasKey(x => x.Id);
            b.Property(x => x.ForDate).HasConversion(dateOnlyConverter);
            b.Property(x => x.KasaTuru).HasMaxLength(50).IsRequired();
            b.Property(x => x.NormalizationVersion).HasMaxLength(50);
            b.Property(x => x.CalculationEngineVersion).HasMaxLength(50);
            b.Property(x => x.CarryOverPolicyVersion).HasMaxLength(50);
            b.Property(x => x.InputsFingerprint).HasMaxLength(255);
            b.Property(x => x.ResultsJson).HasColumnType("nvarchar(max)");
            b.HasIndex(x => new { x.ForDate, x.KasaTuru })
                .IsUnique()
                .HasDatabaseName("IX_DailyCalcResults_Date_Type");
            b.HasIndex(x => x.PreviousResultId)
                .HasDatabaseName("IX_DailyCalcResults_PrevId");
        });

        modelBuilder.Entity<DailyCalculationHistory>(b =>
        {
            b.ToTable("DailyCalculationHistories");
            b.HasKey(x => x.Id);
            b.Property(x => x.ForDate).HasConversion(dateOnlyConverter);
            b.Property(x => x.KasaTuru).HasMaxLength(50).IsRequired();
            b.Property(x => x.InputsFingerprint).HasMaxLength(255);
            b.Property(x => x.ArchivedBy).HasMaxLength(255);
            b.Property(x => x.ResultsJson).HasColumnType("nvarchar(max)");
            
            b.HasIndex(x => new { x.ForDate, x.KasaTuru, x.VersionNumber })
                .HasDatabaseName("IX_DailyCalcHistory_Date_Type_Ver");
            b.HasIndex(x => x.DailyCalculationResultId)
                .HasDatabaseName("IX_DailyCalcHistory_ResultId");
        });
        modelBuilder.Entity<CalculationParityDrift>(b =>
        {
            b.ToTable("CalculationParityDrifts");
            b.HasKey(x => x.Id);
            b.Property(x => x.TargetDate).HasConversion(dateOnlyConverter);
            b.Property(x => x.KasaScope).HasMaxLength(50).IsRequired();
            b.Property(x => x.FieldKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(50);
            b.Property(x => x.ReasonHint).HasMaxLength(100);
            b.Property(x => x.RootCauseCategory).HasMaxLength(100);
            b.Property(e => e.LegacyValue).HasPrecision(18, 2);
            b.Property(e => e.DataFirstValue).HasPrecision(18, 2);
            b.Property(e => e.AbsoluteDifference).HasPrecision(18, 2);
            
            b.Property(x => x.ReviewedBy).HasMaxLength(100);
            b.Property(x => x.ResolutionStatus).HasMaxLength(50);
            b.Property(x => x.ResolutionNote).HasMaxLength(500);

            b.HasIndex(x => new { x.TargetDate, x.KasaScope, x.FieldKey })
                .HasDatabaseName("IX_ParityDrift_Date_Scope_Key");
        });
        }
    }
}
