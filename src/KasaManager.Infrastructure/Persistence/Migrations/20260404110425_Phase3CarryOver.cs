using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3CarryOver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "DailyCalculationResults",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousResultId",
                table: "DailyCalculationResults",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonHint",
                table: "CalculationParityDrifts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyCalcResults_PrevId",
                table: "DailyCalculationResults",
                column: "PreviousResultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DailyCalcResults_PrevId",
                table: "DailyCalculationResults");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "DailyCalculationResults");

            migrationBuilder.DropColumn(
                name: "PreviousResultId",
                table: "DailyCalculationResults");

            migrationBuilder.DropColumn(
                name: "ReasonHint",
                table: "CalculationParityDrifts");
        }
    }
}
