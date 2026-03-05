using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHesapKontrolTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SonBildirimTarihi",
                table: "HesapKontrolKayitlari",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TakipBaslangicTarihi",
                table: "HesapKontrolKayitlari",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SonBildirimTarihi",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "TakipBaslangicTarihi",
                table: "HesapKontrolKayitlari");
        }
    }
}
