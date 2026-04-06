using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase4DriftResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolutionNote",
                table: "CalculationParityDrifts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolutionStatus",
                table: "CalculationParityDrifts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "CalculationParityDrifts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "CalculationParityDrifts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolutionNote",
                table: "CalculationParityDrifts");

            migrationBuilder.DropColumn(
                name: "ResolutionStatus",
                table: "CalculationParityDrifts");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "CalculationParityDrifts");

            migrationBuilder.DropColumn(
                name: "ReviewedBy",
                table: "CalculationParityDrifts");
        }
    }
}
