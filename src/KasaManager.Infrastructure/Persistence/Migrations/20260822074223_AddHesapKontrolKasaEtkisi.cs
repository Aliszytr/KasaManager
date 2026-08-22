using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHesapKontrolKasaEtkisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "KasaEtkisiIsTarihi",
                table: "HesapKontrolKayitlari",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KasaEtkisiTersDonusIsTarihi",
                table: "HesapKontrolKayitlari",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KasaEtkisiTutari",
                table: "HesapKontrolKayitlari",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_HesapKontrol_KasaEtkisiIsTarihi",
                table: "HesapKontrolKayitlari",
                column: "KasaEtkisiIsTarihi");

            migrationBuilder.CreateIndex(
                name: "IX_HesapKontrol_KasaEtkisiTersDonusIsTarihi",
                table: "HesapKontrolKayitlari",
                column: "KasaEtkisiTersDonusIsTarihi");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HesapKontrol_KasaEtkisiIsTarihi",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropIndex(
                name: "IX_HesapKontrol_KasaEtkisiTersDonusIsTarihi",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "KasaEtkisiIsTarihi",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "KasaEtkisiTersDonusIsTarihi",
                table: "HesapKontrolKayitlari");

            migrationBuilder.DropColumn(
                name: "KasaEtkisiTutari",
                table: "HesapKontrolKayitlari");
        }
    }
}
