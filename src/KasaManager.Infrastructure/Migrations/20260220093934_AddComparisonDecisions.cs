using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddComparisonDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComparisonDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComparisonType = table.Column<int>(type: "int", nullable: false),
                    OnlineDosyaNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    OnlineMiktar = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OnlineBirimAdi = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    BankaTutar = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    BankaAciklamaSummary = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OriginalConfidence = table.Column<double>(type: "float", nullable: false),
                    OriginalMatchReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonDecisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonDecisions_UniqueRecord",
                table: "ComparisonDecisions",
                columns: new[] { "ComparisonType", "OnlineDosyaNo", "OnlineMiktar", "OnlineBirimAdi" },
                unique: true,
                filter: "[OnlineBirimAdi] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComparisonDecisions");
        }
    }
}
