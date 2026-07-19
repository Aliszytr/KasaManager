using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHesapKontrolActorAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "HesapKontrolKayitlari",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CancelledByUserId",
                table: "HesapKontrolKayitlari",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "HesapKontrolKayitlari",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResolvedByUserId",
                table: "HesapKontrolKayitlari",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrackingStartedByUserId",
                table: "HesapKontrolKayitlari",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "ResolvedByUserId",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "TrackingStartedByUserId",
                table: "HesapKontrolKayitlari");
        }
    }
}
