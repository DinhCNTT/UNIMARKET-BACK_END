using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class ThemMaTinDangChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaTinDang",
                table: "CuocTroChuyens",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CuocTroChuyens_MaTinDang",
                table: "CuocTroChuyens",
                column: "MaTinDang");

            migrationBuilder.AddForeignKey(
                name: "FK_CuocTroChuyens_TinDangs_MaTinDang",
                table: "CuocTroChuyens",
                column: "MaTinDang",
                principalTable: "TinDangs",
                principalColumn: "MaTinDang",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CuocTroChuyens_TinDangs_MaTinDang",
                table: "CuocTroChuyens");

            migrationBuilder.DropIndex(
                name: "IX_CuocTroChuyens_MaTinDang",
                table: "CuocTroChuyens");

            migrationBuilder.DropColumn(
                name: "MaTinDang",
                table: "CuocTroChuyens");
        }
    }
}
