using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7TrustLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ResolutionStatus",
                table: "CalculationParityDrifts",
                type: "int",
                maxLength: 50,
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "DataFirstTrustSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: false),
                    KasaType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    ExactMatchCount = table.Column<int>(type: "int", nullable: false),
                    DriftCount = table.Column<int>(type: "int", nullable: false),
                    StaleCount = table.Column<int>(type: "int", nullable: false),
                    ConfidenceScore = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TrustLevel = table.Column<int>(type: "int", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataFirstTrustSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustSnapshots_Date_Scope",
                table: "DataFirstTrustSnapshots",
                columns: new[] { "TargetDate", "KasaType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataFirstTrustSnapshots");

            migrationBuilder.AlterColumn<string>(
                name: "ResolutionStatus",
                table: "CalculationParityDrifts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldMaxLength: 50);
        }
    }
}
