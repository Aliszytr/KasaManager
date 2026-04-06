using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalculationParityDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalculationParityDrifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KasaScope = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LegacyValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DataFirstValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AbsoluteDifference = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RootCauseCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculationParityDrifts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParityDrift_Date_Scope_Key",
                table: "CalculationParityDrifts",
                columns: new[] { "TargetDate", "KasaScope", "FieldKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalculationParityDrifts");
        }
    }
}
