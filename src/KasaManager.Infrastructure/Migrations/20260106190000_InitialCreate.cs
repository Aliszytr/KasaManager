using System;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Migrations
{
    /// <summary>
    /// Initial schema for Snapshot DB (R6+) + Global Defaults (R9+).
    ///
    /// R14C ZipFix goal:
    /// - Ensure table KasaGlobalDefaultsSettings always exists when migrations are applied.
    /// - Provide a stable, deterministic schema for clean DB reset + migrate workflows.
    ///
    /// Notes:
    /// - KasaGlobalDefaultsSettings.Id is ValueGeneratedNever() (application-controlled, must be 1).
    /// - DateOnly is persisted as SQL 'date' (via converter in DbContext).
    /// </summary>
    [DbContext(typeof(KasaManagerDbContext))]
    [Migration("20260106190000_InitialCreate")]
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KasaGlobalDefaultsSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DefaultBozukPara = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DefaultDundenDevredenKasaNakit = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DefaultGenelKasaBaslangicTarihiSeed = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DefaultGenelKasaDevredenSeed = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DefaultKasaEksikFazla = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DefaultKaydenTahsilat = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DefaultNakitPara = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SelectedVeznedarlarJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaGlobalDefaultsSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KasaRaporSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaporTarihi = table.Column<DateTime>(type: "date", nullable: false),
                    RaporTuru = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SelectionTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    WarningsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaRaporSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KasaRaporSnapshotInputs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaRaporSnapshotInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KasaRaporSnapshotInputs_KasaRaporSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "KasaRaporSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KasaRaporSnapshotResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaRaporSnapshotResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KasaRaporSnapshotResults_KasaRaporSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "KasaRaporSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KasaRaporSnapshotRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Veznedar = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false),
                    Bakiye = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ColumnsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HeadersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsSummaryRow = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaRaporSnapshotRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KasaRaporSnapshotRows_KasaRaporSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "KasaRaporSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KasaRaporSnapshotInputs_SnapshotId",
                table: "KasaRaporSnapshotInputs",
                column: "SnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KasaRaporSnapshotResults_SnapshotId",
                table: "KasaRaporSnapshotResults",
                column: "SnapshotId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KasaRaporSnapshotRows_SnapshotId",
                table: "KasaRaporSnapshotRows",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_KasaRaporSnapshots_RaporTarihi_RaporTuru",
                table: "KasaRaporSnapshots",
                columns: new[] { "RaporTarihi", "RaporTuru" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "KasaRaporSnapshotInputs");
            migrationBuilder.DropTable(name: "KasaRaporSnapshotResults");
            migrationBuilder.DropTable(name: "KasaRaporSnapshotRows");
            migrationBuilder.DropTable(name: "KasaGlobalDefaultsSettings");
            migrationBuilder.DropTable(name: "KasaRaporSnapshots");
        }
    }
}
