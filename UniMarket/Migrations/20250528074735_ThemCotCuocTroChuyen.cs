using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMarket.Migrations
{
    /// <inheritdoc />
    public partial class ThemCotCuocTroChuyen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnhDaiDienTinDang",
                table: "CuocTroChuyens",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GiaTinDang",
                table: "CuocTroChuyens",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "MaTinDang",
                table: "CuocTroChuyens",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TieuDeTinDang",
                table: "CuocTroChuyens",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnhDaiDienTinDang",
                table: "CuocTroChuyens");

            migrationBuilder.DropColumn(
                name: "GiaTinDang",
                table: "CuocTroChuyens");

            migrationBuilder.DropColumn(
                name: "MaTinDang",
                table: "CuocTroChuyens");

            migrationBuilder.DropColumn(
                name: "TieuDeTinDang",
                table: "CuocTroChuyens");
        }
    }
}
