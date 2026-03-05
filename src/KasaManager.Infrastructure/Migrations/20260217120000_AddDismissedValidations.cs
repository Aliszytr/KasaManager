using System;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Migrations
{
    /// <summary>
    /// Validation: Kullanıcı tarafından dismiss edilen uyarıları saklayan tablo.
    /// </summary>
    [DbContext(typeof(KasaManagerDbContext))]
    [Migration("20260217120000_AddDismissedValidations")]
    public partial class AddDismissedValidations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DismissedValidations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RaporTarihi = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KasaTuru = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DismissedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DismissedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DismissedValidations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DismissedValidations_Date_Kasa_Rule",
                table: "DismissedValidations",
                columns: new[] { "RaporTarihi", "KasaTuru", "RuleCode" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "DismissedValidations");
        }
    }
}
