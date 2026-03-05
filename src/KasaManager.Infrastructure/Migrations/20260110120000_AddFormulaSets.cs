using System;
using KasaManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Migrations
{
    [DbContext(typeof(KasaManagerDbContext))]
    [Migration("20260110120000_AddFormulaSets")]
    public partial class AddFormulaSets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormulaSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ScopeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SelectedInputsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormulaSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormulaLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Expression = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormulaLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormulaLines_FormulaSets_SetId",
                        column: x => x.SetId,
                        principalTable: "FormulaSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormulaRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InputsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OutputsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IssuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormulaRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormulaRuns_FormulaSets_SetId",
                        column: x => x.SetId,
                        principalTable: "FormulaSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormulaLines_SetId_SortOrder",
                table: "FormulaLines",
                columns: new[] { "SetId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_FormulaRuns_SetId_RunAtUtc",
                table: "FormulaRuns",
                columns: new[] { "SetId", "RunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FormulaSets_ScopeType_IsActive",
                table: "FormulaSets",
                columns: new[] { "ScopeType", "IsActive" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "FormulaRuns");
            migrationBuilder.DropTable(name: "FormulaLines");
            migrationBuilder.DropTable(name: "FormulaSets");
        }
    }
}
