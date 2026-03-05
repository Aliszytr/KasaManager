using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKasaUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════════
            // Bu migration tamamen idempotent yazıldı.
            // Önceki kısmi çalışmalardan kalan nesneler varsa hata vermez.
            // ═══════════════════════════════════════════════════════════════

            // ═══ UserFieldPreferences index: filter ekle ═══
            DropIndexIfExists(migrationBuilder, "IX_UserFieldPreferences_KasaType_UserName", "UserFieldPreferences");

            // ═══ KasaGlobalDefaultsSettings: yeni sütunlar ═══
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "HesapAdiHarc", "nvarchar(max)", true);
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "HesapAdiMasraf", "nvarchar(max)", true);
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "HesapAdiStopaj", "nvarchar(max)", true);
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "IbanHarc", "nvarchar(max)", true);
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "IbanMasraf", "nvarchar(max)", true);
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "IbanPostaPulu", "nvarchar(max)", true);
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "IbanStopaj", "nvarchar(max)", true);
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "VergideBirikenSeed", "decimal(18,2)", true);
            AddColumnIfNotExists(migrationBuilder, "KasaGlobalDefaultsSettings", "VergideBirikenSeedUpdatedAt", "datetime2", true);

            // ═══ CalculatedKasaSnapshots: yeni sütunlar ═══
            AddColumnIfNotExists(migrationBuilder, "CalculatedKasaSnapshots", "Description", "nvarchar(500)", true);
            AddColumnIfNotExists(migrationBuilder, "CalculatedKasaSnapshots", "KasaRaporDataJson", "nvarchar(max)", true);
            AddColumnIfNotExists(migrationBuilder, "CalculatedKasaSnapshots", "Name", "nvarchar(200)", true);

            // ═══ Yeni Tablolar (idempotent) ═══

            CreateTableIfNotExists_DismissedValidations(migrationBuilder);
            CreateTableIfNotExists_HesapKontrolKayitlari(migrationBuilder);
            CreateTableIfNotExists_KasaUsers(migrationBuilder);

            // ═══ Index'ler (idempotent) ═══

            CreateIndexIfNotExists(migrationBuilder, "IX_UserFieldPreferences_KasaType_UserName", "UserFieldPreferences",
                "[KasaType], [UserName]", isUnique: true, filter: "[UserName] IS NOT NULL");

            CreateIndexIfNotExists(migrationBuilder, "IX_KasaRaporSnapshotRows_SnapshotId", "KasaRaporSnapshotRows",
                "[SnapshotId]", isUnique: false);

            CreateIndexIfNotExists(migrationBuilder, "IX_KasaRaporSnapshotResults_SnapshotId", "KasaRaporSnapshotResults",
                "[SnapshotId]", isUnique: true);

            CreateIndexIfNotExists(migrationBuilder, "IX_KasaRaporSnapshotInputs_SnapshotId", "KasaRaporSnapshotInputs",
                "[SnapshotId]", isUnique: true);

            CreateIndexIfNotExists(migrationBuilder, "IX_DismissedValidations_Date_Kasa_Rule", "DismissedValidations",
                "[RaporTarihi], [KasaTuru], [RuleCode]", isUnique: true);

            CreateIndexIfNotExists(migrationBuilder, "IX_HesapKontrol_Durum", "HesapKontrolKayitlari",
                "[Durum]", isUnique: false);

            CreateIndexIfNotExists(migrationBuilder, "IX_HesapKontrol_Tarih_Hesap_Durum", "HesapKontrolKayitlari",
                "[AnalizTarihi], [HesapTuru], [Durum]", isUnique: false);

            CreateIndexIfNotExists(migrationBuilder, "IX_KasaUsers_Username", "KasaUsers",
                "[Username]", isUnique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DismissedValidations");
            migrationBuilder.DropTable(name: "HesapKontrolKayitlari");
            migrationBuilder.DropTable(name: "KasaUsers");

            DropIndexIfExists(migrationBuilder, "IX_UserFieldPreferences_KasaType_UserName", "UserFieldPreferences");
            DropIndexIfExists(migrationBuilder, "IX_KasaRaporSnapshotRows_SnapshotId", "KasaRaporSnapshotRows");
            DropIndexIfExists(migrationBuilder, "IX_KasaRaporSnapshotResults_SnapshotId", "KasaRaporSnapshotResults");
            DropIndexIfExists(migrationBuilder, "IX_KasaRaporSnapshotInputs_SnapshotId", "KasaRaporSnapshotInputs");

            migrationBuilder.DropColumn(name: "HesapAdiHarc", table: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropColumn(name: "HesapAdiMasraf", table: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropColumn(name: "HesapAdiStopaj", table: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropColumn(name: "IbanHarc", table: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropColumn(name: "IbanMasraf", table: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropColumn(name: "IbanPostaPulu", table: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropColumn(name: "IbanStopaj", table: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropColumn(name: "VergideBirikenSeed", table: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropColumn(name: "VergideBirikenSeedUpdatedAt", table: "KasaGlobalDefaultsSettings");

            migrationBuilder.DropColumn(name: "Description", table: "CalculatedKasaSnapshots");
            migrationBuilder.DropColumn(name: "KasaRaporDataJson", table: "CalculatedKasaSnapshots");
            migrationBuilder.DropColumn(name: "Name", table: "CalculatedKasaSnapshots");

            migrationBuilder.CreateIndex(
                name: "IX_UserFieldPreferences_KasaType_UserName",
                table: "UserFieldPreferences",
                columns: new[] { "KasaType", "UserName" },
                unique: true);
        }

        // ═══════════════════════════════════════════════════
        // Idempotent Helper Methods
        // ═══════════════════════════════════════════════════

        private static void AddColumnIfNotExists(MigrationBuilder mb,
            string table, string column, string type, bool nullable)
        {
            mb.Sql($@"
IF NOT EXISTS (SELECT 1 FROM sys.columns 
               WHERE object_id = OBJECT_ID(N'{table}') AND name = N'{column}')
    ALTER TABLE [{table}] ADD [{column}] {type} {(nullable ? "NULL" : "NOT NULL")};");
        }

        private static void DropIndexIfExists(MigrationBuilder mb, string indexName, string table)
        {
            mb.Sql($@"
IF EXISTS (SELECT 1 FROM sys.indexes 
           WHERE name = N'{indexName}' AND object_id = OBJECT_ID(N'{table}'))
    DROP INDEX [{indexName}] ON [{table}];");
        }

        private static void CreateIndexIfNotExists(MigrationBuilder mb,
            string indexName, string table, string columns, bool isUnique, string filter = "")
        {
            var unique = isUnique ? "UNIQUE " : "";
            var where = filter.Length > 0 ? $" WHERE {filter}" : "";
            mb.Sql($@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes 
               WHERE name = N'{indexName}' AND object_id = OBJECT_ID(N'{table}'))
    CREATE {unique}INDEX [{indexName}] ON [{table}] ({columns}){where};");
        }

        private static void CreateTableIfNotExists_DismissedValidations(MigrationBuilder mb)
        {
            mb.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'DismissedValidations')
BEGIN
    CREATE TABLE [DismissedValidations] (
        [Id] int NOT NULL IDENTITY(1,1),
        [RaporTarihi] datetime2 NOT NULL,
        [KasaTuru] nvarchar(30) NOT NULL,
        [RuleCode] nvarchar(100) NOT NULL,
        [DismissedBy] nvarchar(256) NULL,
        [DismissedAt] datetime2 NOT NULL,
        [Note] nvarchar(500) NULL,
        CONSTRAINT [PK_DismissedValidations] PRIMARY KEY ([Id])
    );
END");
        }

        private static void CreateTableIfNotExists_HesapKontrolKayitlari(MigrationBuilder mb)
        {
            mb.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'HesapKontrolKayitlari')
BEGIN
    CREATE TABLE [HesapKontrolKayitlari] (
        [Id] uniqueidentifier NOT NULL,
        [AnalizTarihi] datetime2 NOT NULL,
        [OlusturmaTarihi] datetime2 NOT NULL,
        [HesapTuru] int NOT NULL,
        [Yon] int NOT NULL,
        [Tutar] decimal(18,2) NOT NULL,
        [Aciklama] nvarchar(1000) NULL,
        [DosyaNo] nvarchar(100) NULL,
        [BirimAdi] nvarchar(200) NULL,
        [Sinif] int NOT NULL,
        [TespitEdilenTip] nvarchar(50) NULL,
        [KarsilastirmaSatirIndex] int NULL,
        [KarsilastirmaTuru] nvarchar(30) NULL,
        [Durum] int NOT NULL,
        [CozulmeTarihi] datetime2 NULL,
        [CozulmeKaynakId] uniqueidentifier NULL,
        [KullaniciOnay] bit NOT NULL,
        [OnaylayanKullanici] nvarchar(256) NULL,
        [OnayTarihi] datetime2 NULL,
        [Notlar] nvarchar(2000) NULL,
        [CreatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_HesapKontrolKayitlari] PRIMARY KEY ([Id])
    );
END");
        }

        private static void CreateTableIfNotExists_KasaUsers(MigrationBuilder mb)
        {
            mb.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'KasaUsers')
BEGIN
    CREATE TABLE [KasaUsers] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Username] nvarchar(100) NOT NULL,
        [PasswordHash] nvarchar(256) NOT NULL,
        [DisplayName] nvarchar(200) NOT NULL,
        [Role] nvarchar(50) NOT NULL DEFAULT N'User',
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastLoginAt] datetime2 NULL,
        CONSTRAINT [PK_KasaUsers] PRIMARY KEY ([Id])
    );
END");
        }
    }
}
