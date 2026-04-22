using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexDailyCalcResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyCalcResults_Date_Type",
                table: "DailyCalculationResults");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalcResults_Date_Type",
                table: "DailyCalculationResults",
                columns: new[] { "ForDate", "KasaTuru" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyCalcResults_Date_Type",
                table: "DailyCalculationResults");

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalcResults_Date_Type",
                table: "DailyCalculationResults",
                columns: new[] { "ForDate", "KasaTuru" });
        }
    }
}
