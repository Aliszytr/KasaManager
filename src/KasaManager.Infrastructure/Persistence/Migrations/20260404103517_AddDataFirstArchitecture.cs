using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDataFirstArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyCalculationHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyCalculationResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ForDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KasaTuru = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ResultsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    InputsFingerprint = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCalculationHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyCalculationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ForDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KasaTuru = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FormulaSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NormalizationVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CalculationEngineVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CarryOverPolicyVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CalculatedVersion = table.Column<int>(type: "int", nullable: false),
                    IsStale = table.Column<bool>(type: "bit", nullable: false),
                    InputsFingerprint = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ResultsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCalculationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ForDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanonicalKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RawValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NumericValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TextValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DateValue = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    SourceRowNo = table.Column<int>(type: "int", nullable: true),
                    SourceColumnNo = table.Column<int>(type: "int", nullable: true),
                    Confidence = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyFacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DailyOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ForDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CanonicalKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NumericValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TextValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ImportedBy = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ImportProfileVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalcHistory_Date_Type_Ver",
                table: "DailyCalculationHistories",
                columns: new[] { "ForDate", "KasaTuru", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalcHistory_ResultId",
                table: "DailyCalculationHistories",
                column: "DailyCalculationResultId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalcResults_Date_Type",
                table: "DailyCalculationResults",
                columns: new[] { "ForDate", "KasaTuru" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyFacts_Date_Key",
                table: "DailyFacts",
                columns: new[] { "ForDate", "CanonicalKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DailyOverrides_Date_Key",
                table: "DailyOverrides",
                columns: new[] { "ForDate", "CanonicalKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_TargetDate",
                table: "ImportBatches",
                column: "TargetDate");

            migrationBuilder.AddForeignKey(
                name: "FK_FinansalIstisnaHistory_FinansalIstisnalar_FinansalIstisnaId",
                table: "FinansalIstisnaHistory",
                column: "FinansalIstisnaId",
                principalTable: "FinansalIstisnalar",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_FinansalIstisnaHistory_FinansalIstisnalar_FinansalIstisnaId",
                table: "FinansalIstisnaHistory");

            migrationBuilder.DropTable(
                name: "DailyCalculationHistories");

            migrationBuilder.DropTable(
                name: "DailyCalculationResults");

            migrationBuilder.DropTable(
                name: "DailyFacts");

            migrationBuilder.DropTable(
                name: "DailyOverrides");

            migrationBuilder.DropTable(
                name: "ImportBatches");
        }
    }
}
