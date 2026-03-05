using System;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Migrations
{
    /// <summary>
    /// R17: Dynamic Field Chooser ve Calculated Kasa Snapshot tabloları.
    /// - CalculatedKasaSnapshots: Hesaplanan kasa değerlerinin versiyonlu kayıtları
    /// - UserFieldPreferences: Kullanıcı alan tercihleri
    /// </summary>
    [DbContext(typeof(KasaManagerDbContext))]
    [Migration("20260130120000_R17_DynamicFieldChooser")]
    public partial class R17_DynamicFieldChooser : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ===== CalculatedKasaSnapshots =====
            migrationBuilder.CreateTable(
                name: "CalculatedKasaSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaporTarihi = table.Column<DateTime>(type: "datetime2", nullable: false),
                    KasaTuru = table.Column<int>(type: "int", nullable: false),
                    FormulaSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FormulaSetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CalculatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    InputsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalculatedKasaSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalculatedKasaSnapshots_Date_Type_Active",
                table: "CalculatedKasaSnapshots",
                columns: new[] { "RaporTarihi", "KasaTuru", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CalculatedKasaSnapshots_IsDeleted",
                table: "CalculatedKasaSnapshots",
                column: "IsDeleted");

            // ===== UserFieldPreferences =====
            migrationBuilder.CreateTable(
                name: "UserFieldPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KasaType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SelectedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFieldPreferences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserFieldPreferences_KasaType_UserName",
                table: "UserFieldPreferences",
                columns: new[] { "KasaType", "UserName" },
                unique: true,
                filter: null);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "UserFieldPreferences");
            migrationBuilder.DropTable(name: "CalculatedKasaSnapshots");
        }
    }
}
