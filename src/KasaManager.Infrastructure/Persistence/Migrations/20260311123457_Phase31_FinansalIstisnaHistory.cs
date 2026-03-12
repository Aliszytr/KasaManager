using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase31_FinansalIstisnaHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FinansalIstisnaHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinansalIstisnaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    EventTarihiUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EventKullanici = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Aciklama = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    OldKararDurumu = table.Column<int>(type: "int", nullable: true),
                    OldDurum = table.Column<int>(type: "int", nullable: true),
                    OldBeklenenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OldGerceklesenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OldSistemeGirilenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NewKararDurumu = table.Column<int>(type: "int", nullable: true),
                    NewDurum = table.Column<int>(type: "int", nullable: true),
                    NewBeklenenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NewGerceklesenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NewSistemeGirilenTutar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinansalIstisnaHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinExHistory_EventTarihi",
                table: "FinansalIstisnaHistory",
                column: "EventTarihiUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinExHistory_IstisnaId",
                table: "FinansalIstisnaHistory",
                column: "FinansalIstisnaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinansalIstisnaHistory");
        }
    }
}
