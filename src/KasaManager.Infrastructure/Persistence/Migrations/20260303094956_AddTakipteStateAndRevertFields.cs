using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTakipteStateAndRevertFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeriAlanKullanici",
                table: "HesapKontrolKayitlari",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GeriAlmaTarihi",
                table: "HesapKontrolKayitlari",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeriAlanKullanici",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "GeriAlmaTarihi",
                table: "HesapKontrolKayitlari");
        }
    }
}
