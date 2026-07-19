using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KasaManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalculatedKasaSnapshotActorAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CalculatedByUserId",
                table: "CalculatedKasaSnapshots",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedByUserId",
                table: "CalculatedKasaSnapshots",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalculatedByUserId",
                table: "CalculatedKasaSnapshots");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "CalculatedKasaSnapshots");
        }
    }
}
