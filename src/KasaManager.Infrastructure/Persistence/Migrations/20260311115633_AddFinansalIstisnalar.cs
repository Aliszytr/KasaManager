using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinansalIstisnalar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinansalIstisnalar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IslemTarihi = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SistemGirisTarihi = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Tur = table.Column<int>(type: "int", nullable: false),
                    Kategori = table.Column<int>(type: "int", nullable: false),
                    HesapTuru = table.Column<int>(type: "int", nullable: false),
                    HedefHesapAciklama = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BeklenenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GerceklesenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SistemeGirilenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EtkiYonu = table.Column<int>(type: "int", nullable: false),
                    KararDurumu = table.Column<int>(type: "int", nullable: false),
                    Durum = table.Column<int>(type: "int", nullable: false),
                    Neden = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Aciklama = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OlusturmaTarihiUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    OlusturanKullanici = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    GuncellemeTarihiUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GuncelleyenKullanici = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CozulmeTarihi = table.Column<DateTime>(type: "datetime2", nullable: true),
                    KararVerenKullanici = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    KararTarihiUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinansalIstisnalar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinansalIstisnalar_Tarih_Durum_Karar",
                table: "FinansalIstisnalar",
                columns: new[] { "IslemTarihi", "Durum", "KararDurumu" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinansalIstisnalar");
        }
    }
}
